using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Nice3point.TUnit.Revit;
using Nice3point.TUnit.Revit.Executors;
using Revit.TransactionMemoryCache.Abstractions.Services;
using Revit.TransactionMemoryCache.Services;
using TUnit.Core.Executors;

namespace Revit.TransactionMemoryCache.RevitTests;

/// <summary>
/// Integration tests for <see cref="CachedElementCollector"/> and <see cref="CachedElementCollectorFactory"/>
/// running inside a live Revit process, against a real <see cref="Document"/>/<see cref="FilteredElementCollector"/>.
/// Uses a hand-written in-memory <see cref="IRevitTransactionMemoryCache"/> (<see cref="TestRevitTransactionMemoryCache"/>)
/// rather than the real event-driven <c>RevitTransactionMemoryCache</c>, since wiring up real automatic
/// invalidation requires <c>IRevitContextInitializer.Initialize(UIControlledApplication)</c>, which isn't
/// reachable from this test harness - the pure unit tests already cover key composition/canonicalization,
/// so this project's job is verifying behaviour against the real Revit API that headless tests can't touch:
/// actual element creation/filtering/exclusion and caching (same-instance-on-hit) semantics, plus the
/// once-only/mutual-exclusion validation that <c>CachedElementCollectorTests</c> (headless) could not cover -
/// merely loading <see cref="CachedElementCollector"/> forces the CLR to resolve its <see cref="Document"/>/
/// <see cref="FilteredElementCollector"/>-typed fields, which fails outside a live Revit process.
/// </summary>
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit test naming convention: MethodName_Scenario_ExpectedResult.")]
public sealed class CachedElementCollectorRevitTests : RevitApiTest
{
    private Document? _document;
    private Wall? _wall;
    private Level? _level;
    private IRevitTransactionMemoryCache _cache = null!;

    [Before(Test)]
    [HookExecutor<RevitThreadExecutor>]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The Document is assigned to a field and disposed in CloseModel() " +
                         "([After(Test)]) - the analyzer can't see disposal across methods.")]
    public void CreateModel()
    {
        _document = Application.NewProjectDocument(UnitSystem.Metric);
        _cache = new TestRevitTransactionMemoryCache();

        using Transaction transaction = new(_document, "Seed CachedElementCollector tests");
        transaction.Start();
        _level = Level.Create(_document, 0);
        _wall = Wall.Create(_document, Line.CreateBound(XYZ.Zero, new XYZ(10, 0, 0)), _level.Id, false);
        transaction.Commit();
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

        await AssertThrows<ArgumentNullException>(() => collector.OfClass(null!)).ConfigureAwait(false);
    }

    [Test]
    public async Task Of_Generic_CalledAfterOfClass_Throws()
    {
        var collector = CreateCollector().OfClass(typeof(Wall));

        await AssertThrows<InvalidOperationException>(() => collector.Of<Floor>()).ConfigureAwait(false);
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

        await AssertThrows<ArgumentNullException>(() => collector.OfCategories(null!)).ConfigureAwait(false);
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

        await AssertThrows<ArgumentNullException>(
            () => collector.WhereParameterEquals(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, (string)null!)).ConfigureAwait(false);
    }

    [Test]
    public async Task WhereParameterEquals_NullElementIdValue_ThrowsArgumentNullException()
    {
        var collector = CreateCollector();

        await AssertThrows<ArgumentNullException>(
            () => collector.WhereParameterEquals(BuiltInParameter.WALL_BASE_CONSTRAINT, (ElementId)null!)).ConfigureAwait(false);
    }

    [Test]
    public async Task WhereParameterEquals_NullParameterId_ThrowsArgumentNullException()
    {
        var collector = CreateCollector();

        await AssertThrows<ArgumentNullException>(
            () => collector.WhereParameterEquals((ElementId)null!, 1)).ConfigureAwait(false);
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

        await AssertThrows<ArgumentNullException>(() => collector.Excluding(null!)).ConfigureAwait(false);
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
    /// <see cref="FilteredElementCollector"/>/<see cref="CachedElementCollector"/>) - a real finding about the Revit
    /// API, not about caching correctness. Isolation is proven instead by the result list sizes: a
    /// <see cref="FilteredElementCollector"/> scoped to one <see cref="Document"/> can never return another
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
