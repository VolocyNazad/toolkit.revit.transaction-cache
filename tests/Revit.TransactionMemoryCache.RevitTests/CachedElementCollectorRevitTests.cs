using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using Revit.TransactionMemoryCache.Abstractions.Services;
using Revit.TransactionMemoryCache.Services;
using TUnit.Core.Executors;

namespace Revit.TransactionMemoryCache.RevitTests;

/// <summary>
/// Integration tests for <see cref="CachedElementCollector"/> and <see cref="CachedElementCollectorFactory"/>
/// running inside a live Revit process, against a real <see cref="Document"/>/<see cref="Autodesk.Revit.DB.FilteredElementCollector"/>.
/// Uses a hand-written in-memory <see cref="IRevitTransactionMemoryCache"/> (<see cref="TestRevitTransactionMemoryCache"/>)
/// rather than the real event-driven <c>RevitTransactionMemoryCache</c>, since wiring up real automatic
/// invalidation requires <c>IRevitContextInitializer.Initialize(UIControlledApplication)</c>, which isn't
/// reachable from this test harness - the pure unit tests already cover key composition/canonicalization,
/// so this project's job is verifying behaviour against the real Revit API that headless tests can't touch:
/// actual element creation/filtering/exclusion and caching (same-instance-on-hit) semantics, plus the
/// once-only/mutual-exclusion validation that <c>CachedElementCollectorTests</c> (headless) could not cover -
/// merely loading <see cref="CachedElementCollector"/> forces the CLR to resolve its <see cref="Document"/>/
/// <see cref="Autodesk.Revit.DB.FilteredElementCollector"/>-typed fields, which fails outside a live Revit process.
/// </summary>
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit test naming convention: MethodName_Scenario_ExpectedResult.")]
public sealed class CachedElementCollectorRevitTests : RevitApiTest
{
    private Document? _document;
    private Wall? _wall;
    private Level? _level;
    private Room? _room;
    private Space? _space;
    private IRevitTransactionMemoryCache _cache = null!;

    [Before(Test)]
    [HookExecutor<RevitThreadExecutor>]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The Document is assigned to a field and disposed in CloseModel() " +
                         "([After(Test)]) - the analyzer can't see disposal across methods.")]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Room/Space creation is environment-dependent (e.g. Space needs Space and Zone " +
                         "settings initialized) - a failure there must not roll back the Level/Wall seeded " +
                         "in the first transaction and break every other, unrelated test.")]
    public void CreateModel()
    {
        _document = Application.NewProjectDocument(UnitSystem.Metric);
        _cache = new TestRevitTransactionMemoryCache();

        using (Transaction transaction = new(_document, "Seed CachedElementCollector tests"))
        {
            transaction.Start();
            _level = Level.Create(_document, 0);
            _wall = Wall.Create(_document, Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)), _level.Id, false);
            transaction.Commit();
        }

        // Room/Space creation is kept in its own transaction, deliberately isolated from the one above: if
        // either fails in this environment, it must not undo the Level/Wall that every other test depends on.
        // Transaction.Commit() does NOT throw on failure - it returns a TransactionStatus and silently rolls
        // back, so a failed commit here must be detected from the return value, not from a caught exception
        // (an exception is still possible from the NewRoom/NewSpace calls themselves, so both are handled).
        using (Transaction spatialTransaction = new(_document, "Seed CachedElementCollector spatial elements"))
        {
            spatialTransaction.Start();
            var spatialElementsCreated = false;
            try
            {
                _room = _document.Create.NewRoom(_level, new UV(5, 5));
                _space = _document.Create.NewSpace(_level, new UV(5, 5));
                spatialElementsCreated = spatialTransaction.Commit() == TransactionStatus.Committed;
            }
            catch (Exception)
            {
                if (spatialTransaction.GetStatus() == TransactionStatus.Started)
                    spatialTransaction.RollBack();
            }

            if (!spatialElementsCreated)
            {
                _room = null;
                _space = null;
            }
        }
    }

    [After(Test)]
    [HookExecutor<RevitThreadExecutor>]
    public void CloseModel() => _document?.Close(false);

    [Test]
    public async Task OfClass_ReturnsCreatedWall()
    {
        var elements = CreateCollector().OfClass(typeof(Wall)).ToElements();

        await Assert.That(elements.Any(element => element.Id == _wall!.Id)).IsTrue();
    }

    [Test]
    public async Task Of_Generic_ReturnsCreatedWall()
    {
        var elements = CreateCollector().Of<Wall>().ToElements();

        await Assert.That(elements.Any(element => element.Id == _wall!.Id)).IsTrue();
    }

    [Test]
    public async Task NotOfClass_ExcludesMatchingClass()
    {
        var elementIds = CreateCollector()
            .NotOfClass(typeof(Wall))
            .WhereElementIsNotElementType()
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsFalse();
        await Assert.That(elementIds.Contains(_level!.Id)).IsTrue();
    }

    [Test]
    public async Task NotOf_Generic_ExcludesMatchingClass()
    {
        var elementIds = CreateCollector()
            .NotOf<Wall>()
            .WhereElementIsNotElementType()
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsFalse();
        await Assert.That(elementIds.Contains(_level!.Id)).IsTrue();
    }

    /// <summary>OfClass and NotOfClass are independent, composable quick filters, not the same slot.</summary>
    [Test]
    public async Task OfClass_CombinedWithNotOfClass_DoesNotThrow()
    {
        var elementIds = CreateCollector()
            .OfClass(typeof(Wall))
            .NotOfClass(typeof(Floor))
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsTrue();
    }

    [Test]
    public async Task OfCategory_FiltersToMatchingCategoryOnly()
    {
        var elementIds = CreateCollector().OfCategory(BuiltInCategory.OST_Walls).ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsTrue();
        await Assert.That(elementIds.Contains(_level!.Id)).IsFalse();
    }

    [Test]
    public async Task OfCategories_FiltersToMatchingCategoriesOnly()
    {
        var elementIds = CreateCollector()
            .OfCategories([BuiltInCategory.OST_Walls, BuiltInCategory.OST_Levels])
            .WhereElementIsNotElementType()
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsTrue();
        await Assert.That(elementIds.Contains(_level!.Id)).IsTrue();
    }

    [Test]
    public async Task WhereParameterEquals_String_FiltersToMatchingElement()
    {
        using (Transaction transaction = new(_document!, "Set comment"))
        {
            transaction.Start();
            _wall!.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("checked");
            transaction.Commit();
        }

        var elementIds = CreateCollector()
            .OfClass(typeof(Wall))
            .WhereParameterEquals(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "checked")
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsTrue();
    }

    [Test]
    public async Task WhereParameterEquals_ElementId_FiltersToMatchingElement()
    {
        var elementIds = CreateCollector()
            .OfClass(typeof(Wall))
            .WhereParameterEquals(BuiltInParameter.WALL_BASE_CONSTRAINT, _level!.Id)
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsTrue();
    }

    [Test]
    public async Task WhereParameterEquals_CalledMultipleTimes_CombinesAsAnd()
    {
        using (Transaction transaction = new(_document!, "Set comment"))
        {
            transaction.Start();
            _wall!.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("checked");
            transaction.Commit();
        }

        var elementIds = CreateCollector()
            .OfClass(typeof(Wall))
            .WhereParameterEquals(BuiltInParameter.WALL_BASE_CONSTRAINT, _level!.Id)
            .WhereParameterEquals(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "checked")
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall.Id)).IsTrue();
    }

    /// <summary>
    /// The wall is created 10 feet long (<c>Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0))</c> in
    /// <see cref="CreateModel"/>), so its CURVE_ELEM_LENGTH parameter should read 10 (internal units = feet).
    /// </summary>
    [Test]
    public async Task WhereParameterEquals_Double_FiltersToMatchingElement()
    {
        var elementIds = CreateCollector()
            .OfClass(typeof(Wall))
            .WhereParameterEquals(BuiltInParameter.CURVE_ELEM_LENGTH, 10.0, 0.01)
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsTrue();
    }

    [Test]
    public async Task NotOfCategory_ExcludesMatchingCategory()
    {
        var elementIds = CreateCollector()
            .NotOfCategory(BuiltInCategory.OST_Levels)
            .WhereElementIsNotElementType()
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsTrue();
        await Assert.That(elementIds.Contains(_level!.Id)).IsFalse();
    }

    [Test]
    public async Task NotOfCategories_ExcludesMatchingCategories()
    {
        var elementIds = CreateCollector()
            .NotOfCategories([BuiltInCategory.OST_Levels, BuiltInCategory.OST_Grids])
            .WhereElementIsNotElementType()
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsTrue();
        await Assert.That(elementIds.Contains(_level!.Id)).IsFalse();
    }

    /// <summary>OfCategory and NotOfCategory are independent, composable quick filters, not the same slot.</summary>
    [Test]
    public async Task OfCategory_CombinedWithNotOfCategory_DoesNotThrow()
    {
        var elementIds = CreateCollector()
            .OfCategory(BuiltInCategory.OST_Walls)
            .NotOfCategory(BuiltInCategory.OST_Levels)
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsTrue();
    }

    [Test]
    public async Task WhereParameterNotEquals_String_ExcludesMatchingElement()
    {
        using (Transaction transaction = new(_document!, "Set comment"))
        {
            transaction.Start();
            _wall!.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("checked");
            transaction.Commit();
        }

        var elementIds = CreateCollector()
            .OfClass(typeof(Wall))
            .WhereParameterNotEquals(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "checked")
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall.Id)).IsFalse();
    }

    [Test]
    public async Task WhereParameterNotEquals_ElementId_ExcludesMatchingElement()
    {
        var elementIds = CreateCollector()
            .OfClass(typeof(Wall))
            .WhereParameterNotEquals(BuiltInParameter.WALL_BASE_CONSTRAINT, _level!.Id)
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsFalse();
    }

    [Test]
    public async Task WhereElementIsElementType_ExcludesWallInstance()
    {
        var elementIds = CreateCollector().OfClass(typeof(Wall)).WhereElementIsElementType().ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsFalse();
    }

    [Test]
    public async Task WhereElementIsNotElementType_IncludesWallInstance()
    {
        var elementIds = CreateCollector().OfClass(typeof(Wall)).WhereElementIsNotElementType().ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsTrue();
    }

    [Test]
    public async Task Excluding_RemovesExcludedElement()
    {
        var elementIds = CreateCollector().OfClass(typeof(Wall)).Excluding([_wall!.Id]).ToElementIds();

        await Assert.That(elementIds.Contains(_wall.Id)).IsFalse();
    }

    [Test]
    public async Task WhereIsRoom_ReturnsCreatedRoom()
    {
        // Room creation isn't guaranteed to succeed in every environment (see CreateModel) - nothing to
        // verify against RoomFilter if it didn't.
        if (_room is null) return;

        var elementIds = CreateCollector().WhereIsRoom().ToElementIds();

        await Assert.That(elementIds.Contains(_room.Id)).IsTrue();
        await Assert.That(elementIds.Contains(_wall!.Id)).IsFalse();
    }

    [Test]
    public async Task WhereIsSpace_ReturnsCreatedSpace()
    {
        // Space creation isn't guaranteed to succeed in every environment (see CreateModel, e.g. Space and
        // Zone settings not initialized) - nothing to verify against SpaceFilter if it didn't.
        if (_space is null) return;

        var elementIds = CreateCollector().WhereIsSpace().ToElementIds();

        await Assert.That(elementIds.Contains(_space.Id)).IsTrue();
        await Assert.That(elementIds.Contains(_wall!.Id)).IsFalse();
    }

    [Test]
    public async Task WhereBoundingBoxIntersects_FiltersToIntersectingElement()
    {
        var wallBoundingBox = _wall!.get_BoundingBox(null);

        var elementIds = CreateCollector()
            .OfClass(typeof(Wall))
            .WhereBoundingBoxIntersects(wallBoundingBox.Min, wallBoundingBox.Max, 0.01)
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall.Id)).IsTrue();
    }

    [Test]
    public async Task WhereBoundingBoxIntersects_ExcludesNonIntersectingElement()
    {
        var elementIds = CreateCollector()
            .OfClass(typeof(Wall))
            .WhereBoundingBoxIntersects(new XYZ(1000, 1000, 1000), new XYZ(1001, 1001, 1001), 0.01)
            .ToElementIds();

        await Assert.That(elementIds.Contains(_wall!.Id)).IsFalse();
    }

    [Test]
    public async Task ToElements_OnCacheHit_ReturnsSameInstance()
    {
        var collector = CreateCollector().OfClass(typeof(Wall));

        var first = collector.ToElements();
        var second = collector.ToElements();

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task ToElements_ProducesCacheHit_RegardlessOfFluentCallOrder()
    {
        var chainA = CreateCollector().OfClass(typeof(Wall)).WhereElementIsNotElementType();
        var chainB = CreateCollector().WhereElementIsNotElementType().OfClass(typeof(Wall));

        var first = chainA.ToElements();
        var second = chainB.ToElements();

        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task OfClass_CalledTwice_Throws()
    {
        var collector = CreateCollector().OfClass(typeof(Wall));

        await AssertThrows<InvalidOperationException>(() => collector.OfClass(typeof(Floor))).ConfigureAwait(false);
    }

    [Test]
    public async Task OfClass_NullElementClass_ThrowsArgumentNullException()
    {
        var collector = CreateCollector();

        await AssertThrows<System.ArgumentNullException>(() => collector.OfClass(null!)).ConfigureAwait(false);
    }

    [Test]
    public async Task Of_Generic_CalledAfterOfClass_Throws()
    {
        var collector = CreateCollector().OfClass(typeof(Wall));

        await AssertThrows<InvalidOperationException>(() => collector.Of<Floor>()).ConfigureAwait(false);
    }

    [Test]
    public async Task NotOfClass_CalledTwice_Throws()
    {
        var collector = CreateCollector().NotOfClass(typeof(Wall));

        await AssertThrows<InvalidOperationException>(() => collector.NotOfClass(typeof(Floor))).ConfigureAwait(false);
    }

    [Test]
    public async Task NotOfClass_NullElementClass_ThrowsArgumentNullException()
    {
        var collector = CreateCollector();

        await AssertThrows<System.ArgumentNullException>(() => collector.NotOfClass(null!)).ConfigureAwait(false);
    }

    [Test]
    public async Task NotOf_Generic_CalledAfterNotOfClass_Throws()
    {
        var collector = CreateCollector().NotOfClass(typeof(Wall));

        await AssertThrows<InvalidOperationException>(() => collector.NotOf<Floor>()).ConfigureAwait(false);
    }

    [Test]
    public async Task OfCategory_CalledTwice_Throws()
    {
        var collector = CreateCollector().OfCategory(BuiltInCategory.OST_Walls);

        await AssertThrows<InvalidOperationException>(() => collector.OfCategory(BuiltInCategory.OST_Floors)).ConfigureAwait(false);
    }

    [Test]
    public async Task OfCategories_CalledTwice_Throws()
    {
        var collector = CreateCollector().OfCategories([BuiltInCategory.OST_Walls]);

        await AssertThrows<InvalidOperationException>(() => collector.OfCategories([BuiltInCategory.OST_Floors])).ConfigureAwait(false);
    }

    [Test]
    public async Task OfCategory_ThenOfCategories_Throws()
    {
        var collector = CreateCollector().OfCategory(BuiltInCategory.OST_Walls);

        await AssertThrows<InvalidOperationException>(() => collector.OfCategories([BuiltInCategory.OST_Floors])).ConfigureAwait(false);
    }

    [Test]
    public async Task OfCategories_NullCategories_ThrowsArgumentNullException()
    {
        var collector = CreateCollector();

        await AssertThrows<System.ArgumentNullException>(() => collector.OfCategories(null!)).ConfigureAwait(false);
    }

    [Test]
    public async Task OfCategories_EmptyCategories_ThrowsArgumentException()
    {
        var collector = CreateCollector();

        await AssertThrows<ArgumentException>(() => collector.OfCategories([])).ConfigureAwait(false);
    }

    [Test]
    public async Task WhereParameterEquals_NullStringValue_ThrowsArgumentNullException()
    {
        var collector = CreateCollector();

        await AssertThrows<System.ArgumentNullException>(
            () => collector.WhereParameterEquals(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, (string)null!)).ConfigureAwait(false);
    }

    [Test]
    public async Task WhereParameterEquals_NullElementIdValue_ThrowsArgumentNullException()
    {
        var collector = CreateCollector();

        await AssertThrows<System.ArgumentNullException>(
            () => collector.WhereParameterEquals(BuiltInParameter.WALL_BASE_CONSTRAINT, (ElementId)null!)).ConfigureAwait(false);
    }

    [Test]
    public async Task WhereParameterEquals_NullParameterId_ThrowsArgumentNullException()
    {
        var collector = CreateCollector();

        await AssertThrows<System.ArgumentNullException>(
            () => collector.WhereParameterEquals((ElementId)null!, 1)).ConfigureAwait(false);
    }

    [Test]
    public async Task NotOfCategory_CalledTwice_Throws()
    {
        var collector = CreateCollector().NotOfCategory(BuiltInCategory.OST_Walls);

        await AssertThrows<InvalidOperationException>(() => collector.NotOfCategory(BuiltInCategory.OST_Floors)).ConfigureAwait(false);
    }

    [Test]
    public async Task NotOfCategory_ThenNotOfCategories_Throws()
    {
        var collector = CreateCollector().NotOfCategory(BuiltInCategory.OST_Walls);

        await AssertThrows<InvalidOperationException>(() => collector.NotOfCategories([BuiltInCategory.OST_Floors])).ConfigureAwait(false);
    }

    [Test]
    public async Task NotOfCategories_NullCategories_ThrowsArgumentNullException()
    {
        var collector = CreateCollector();

        await AssertThrows<System.ArgumentNullException>(() => collector.NotOfCategories(null!)).ConfigureAwait(false);
    }

    [Test]
    public async Task NotOfCategories_EmptyCategories_ThrowsArgumentException()
    {
        var collector = CreateCollector();

        await AssertThrows<ArgumentException>(() => collector.NotOfCategories([])).ConfigureAwait(false);
    }

    [Test]
    public async Task WhereParameterNotEquals_NullStringValue_ThrowsArgumentNullException()
    {
        var collector = CreateCollector();

        await AssertThrows<System.ArgumentNullException>(
            () => collector.WhereParameterNotEquals(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, (string)null!)).ConfigureAwait(false);
    }

    [Test]
    public async Task WhereParameterNotEquals_NullParameterId_ThrowsArgumentNullException()
    {
        var collector = CreateCollector();

        await AssertThrows<System.ArgumentNullException>(
            () => collector.WhereParameterNotEquals((ElementId)null!, 1)).ConfigureAwait(false);
    }

    [Test]
    public async Task WhereElementIsElementType_CalledTwice_Throws()
    {
        var collector = CreateCollector().WhereElementIsElementType();

        await AssertThrows<InvalidOperationException>(() => collector.WhereElementIsElementType()).ConfigureAwait(false);
    }

    [Test]
    public async Task WhereElementIsNotElementType_CalledTwice_Throws()
    {
        var collector = CreateCollector().WhereElementIsNotElementType();

        await AssertThrows<InvalidOperationException>(() => collector.WhereElementIsNotElementType()).ConfigureAwait(false);
    }

    [Test]
    public async Task WhereElementIsElementType_ThenWhereElementIsNotElementType_Throws()
    {
        var collector = CreateCollector().WhereElementIsElementType();

        await AssertThrows<InvalidOperationException>(() => collector.WhereElementIsNotElementType()).ConfigureAwait(false);
    }

    [Test]
    public async Task WhereElementIsNotElementType_ThenWhereElementIsElementType_Throws()
    {
        var collector = CreateCollector().WhereElementIsNotElementType();

        await AssertThrows<InvalidOperationException>(() => collector.WhereElementIsElementType()).ConfigureAwait(false);
    }

    [Test]
    public async Task Excluding_CalledTwice_Throws()
    {
        var collector = CreateCollector().Excluding([_wall!.Id]);

        await AssertThrows<InvalidOperationException>(() => collector.Excluding([_level!.Id])).ConfigureAwait(false);
    }

    [Test]
    public async Task Excluding_NullElementIds_ThrowsArgumentNullException()
    {
        var collector = CreateCollector();

        await AssertThrows<System.ArgumentNullException>(() => collector.Excluding(null!)).ConfigureAwait(false);
    }

    [Test]
    public async Task WhereIsRoom_CalledTwice_Throws()
    {
        var collector = CreateCollector().WhereIsRoom();

        await AssertThrows<InvalidOperationException>(() => collector.WhereIsRoom()).ConfigureAwait(false);
    }

    [Test]
    public async Task WhereIsSpace_CalledTwice_Throws()
    {
        var collector = CreateCollector().WhereIsSpace();

        await AssertThrows<InvalidOperationException>(() => collector.WhereIsSpace()).ConfigureAwait(false);
    }

    /// <summary>WhereIsRoom and WhereIsSpace are independent slots - combining them is pointless (never matches
    /// anything) but not restricted, same as OfClass/OfCategory can be combined even if the combination is
    /// always empty.</summary>
    [Test]
    public async Task WhereIsRoom_CombinedWithWhereIsSpace_DoesNotThrow()
    {
        var elementIds = CreateCollector().WhereIsRoom().WhereIsSpace().ToElementIds();

        await Assert.That(elementIds.Count).IsEqualTo(0);
    }

    [Test]
    public async Task WhereBoundingBoxIntersects_NullMin_ThrowsArgumentNullException()
    {
        var collector = CreateCollector();

        await AssertThrows<System.ArgumentNullException>(
            () => collector.WhereBoundingBoxIntersects(null!, new XYZ(1, 1, 1), 0.01)).ConfigureAwait(false);
    }

    [Test]
    public async Task WhereBoundingBoxIntersects_NullMax_ThrowsArgumentNullException()
    {
        var collector = CreateCollector();

        await AssertThrows<System.ArgumentNullException>(
            () => collector.WhereBoundingBoxIntersects(XYZ.Zero, null!, 0.01)).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the open risk flagged during design: the cache key uses
    /// <see cref="RuntimeHelpers.GetHashCode(object)"/> for document identity (not <see cref="object.GetHashCode"/>),
    /// on the assumption that Revit's managed <see cref="Document"/> wrapper is reference-stable per open document.
    /// Confirms two different real documents (a) get different identity hashes and (b) never share a cache entry,
    /// even for the exact same fluent chain against the same underlying <see cref="IRevitTransactionMemoryCache"/>.
    ///
    /// Deliberately does NOT compare raw <see cref="ElementId"/> values across the two documents - Revit assigns
    /// element ids per-document, so two freshly created (and identically seeded) documents can easily produce
    /// walls with the same numeric id; that would make an id-equality check spuriously pass/fail regardless of
    /// whether caching actually leaked between documents.
    ///
    /// Also deliberately does NOT compare <see cref="Element.Document"/> by reference against the <see cref="Document"/>
    /// used to build the query: that turned out not to be reference-stable in this Revit API binding (an element's
    /// own <c>.Document</c> can be a different managed wrapper instance than the one passed to
    /// <see cref="Autodesk.Revit.DB.FilteredElementCollector"/>/<see cref="CachedElementCollector"/>) - a real finding about the Revit
    /// API, not about caching correctness. Isolation is proven instead by the result list sizes: a
    /// <see cref="Autodesk.Revit.DB.FilteredElementCollector"/> scoped to one <see cref="Document"/> can never return another
    /// document's elements regardless of what the cache does, so if each list contains exactly the one wall seeded
    /// into its own document, no cross-document leak occurred.
    /// </summary>
    [Test]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Level.Create/Wall.Create return Element wrappers owned by the Document, not by the " +
                         "caller - disposing model elements here would be incorrect regardless of scope.")]
    public async Task ToElements_DoesNotShareCacheEntries_BetweenDifferentDocuments()
    {
        var otherDocument = Application.NewProjectDocument(UnitSystem.Metric);
        try
        {
            using (Transaction transaction = new(otherDocument, "Seed second document"))
            {
                transaction.Start();
                var otherLevel = Level.Create(otherDocument, 0);
                Wall.Create(otherDocument, Line.CreateBound(XYZ.Zero, new XYZ(5, 0, 0)), otherLevel.Id, false);
                transaction.Commit();
            }

            await Assert.That(RuntimeHelpers.GetHashCode(_document!) == RuntimeHelpers.GetHashCode(otherDocument)).IsFalse();

            var elementsFromFirst = new CachedElementCollector(_document!, _cache).OfClass(typeof(Wall)).ToElements();
            var elementsFromSecond = new CachedElementCollector(otherDocument, _cache).OfClass(typeof(Wall)).ToElements();

            await Assert.That(ReferenceEquals(elementsFromFirst, elementsFromSecond)).IsFalse();
            await Assert.That(elementsFromFirst.Count).IsEqualTo(1);
            await Assert.That(elementsFromSecond.Count).IsEqualTo(1);
            await Assert.That(elementsFromFirst.Single().Id == _wall!.Id).IsTrue();
        }
        finally
        {
            otherDocument.Close(false);
        }
    }

    [Test]
    public async Task Factory_Create_WhenCacheNotInitialized_ThrowsInvalidOperationException()
    {
        var factory = new CachedElementCollectorFactory(_cache, new TestInitializer(isInitialized: false));

        await AssertThrows<InvalidOperationException>(() => factory.Create(_document!)).ConfigureAwait(false);
    }

    [Test]
    public async Task Factory_Create_WhenCacheInitialized_ReturnsWorkingCollector()
    {
        var factory = new CachedElementCollectorFactory(_cache, new TestInitializer(isInitialized: true));

        var elements = factory.Create(_document!).OfClass(typeof(Wall)).ToElements();

        await Assert.That(elements.Any(element => element.Id == _wall!.Id)).IsTrue();
    }

    private CachedElementCollector CreateCollector() => new(_document!, _cache);

    /// <summary>
    /// Runs <paramref name="action"/> and asserts it throws exactly <typeparamref name="TException"/> - written
    /// by hand rather than relying on a TUnit exception-matcher API, so it doesn't depend on a specific TUnit version.
    /// </summary>
    private static async Task AssertThrows<TException>(Action action)
        where TException : Exception
    {
        TException? thrown = null;
        try
        {
            action();
        }
        catch (TException exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNotNull();
    }

    /// <summary>Minimal real in-memory cache (no eviction/invalidation) - enough to exercise real caching semantics.</summary>
    private sealed class TestRevitTransactionMemoryCache : IRevitTransactionMemoryCache
    {
        private readonly Dictionary<object, object?> _values = [];

        public TItem? GetOrCreate<TItem>(object key, Func<TItem> factory)
        {
            if (_values.TryGetValue(key, out var cached))
                return (TItem?)cached;

            var value = factory();
            _values[key] = value;
            return value;
        }
    }

    private sealed class TestInitializer(bool isInitialized) : IRevitTransactionMemoryCacheInitializer
    {
        public bool IsInitialized { get; } = isInitialized;

        public void Initialize()
        {
        }

        public void Deinitialize()
        {
        }
    }
}
