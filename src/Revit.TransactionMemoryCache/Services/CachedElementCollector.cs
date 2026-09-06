using System.Runtime.CompilerServices;
using Autodesk.Revit.DB;
using Revit.TransactionMemoryCache.Abstractions.Services;

namespace Revit.TransactionMemoryCache.Services;

/// <summary>
/// A cached, fluent alternative to <see cref="FilteredElementCollector"/>: the same kind of chained
/// configuration (<see cref="OfClass"/>, <see cref="OfCategory"/>, ...), but nothing touches Revit's API until
/// a terminal call (<see cref="ToElements"/>/<see cref="ToElementIds"/>) - and even then, only on a cache miss.
///
/// Build one via <see cref="ICachedElementCollectorFactory"/> (resolved through DI), not directly - the
/// factory is what enforces that the underlying <see cref="IRevitTransactionMemoryCache"/> has actually been
/// initialized (see <see cref="IRevitTransactionMemoryCacheInitializer"/>), which caching depends on for
/// automatic invalidation when the document changes.
///
/// Every instance is immutable: each fluent call returns a *new* <see cref="CachedElementCollector"/> rather
/// than mutating the current one, and the underlying <see cref="FilteredElementCollector"/> is never built
/// during the fluent chain - only inside the cache's factory delegate, on a miss. Key fragments recorded by
/// each call are sorted (see <see cref="CachedElementCollectorKeyBuilder"/>) before being combined into the
/// cache key, so the same logical query built in a different call order produces the same key.
///
/// <see cref="OfClass"/>/<see cref="OfCategory"/>/<see cref="WhereElementIsElementType"/>/
/// <see cref="WhereElementIsNotElementType"/>/<see cref="Excluding"/> may each be called at most once per
/// chain (mirroring <see cref="FilteredElementCollector"/>'s own restrictions), and
/// <see cref="WhereElementIsElementType"/>/<see cref="WhereElementIsNotElementType"/> are mutually exclusive -
/// violating either throws immediately, rather than waiting for a cache miss to surface a Revit-side error.
/// <see cref="Of{TElement}"/> is generic sugar for <see cref="OfClass"/>
/// and shares the same once-only restriction. <see cref="NotOfClass"/>/<see cref="NotOf{TElement}"/> mirror
/// <see cref="OfClass"/>/<see cref="Of{TElement}"/> but are a *separate* once-only slot - composable with the
/// positive class filter, same relationship as <see cref="NotOfCategory"/> to <see cref="OfCategory"/> below.
///
/// <c>WherePasses(ElementFilter)</c> is intentionally not supported: most <see cref="Autodesk.Revit.DB.ElementFilter"/>
/// subclasses don't implement value equality, so two independently constructed but logically identical
/// filters would not produce the same cache key. Instead, a few narrower fluent wrappers are exposed for the
/// most common filtering needs, whose parameters (categories, a <see cref="System.Type"/>, parameter
/// values) *do* have proper value equality, so no such problem arises: <see cref="OfCategories"/>/
/// <see cref="NotOfCategories"/> and
/// <see cref="WhereParameterEquals(Autodesk.Revit.DB.BuiltInParameter,int)"/>/
/// <see cref="WhereParameterNotEquals(Autodesk.Revit.DB.BuiltInParameter,int)"/> - overloaded for
/// <see langword="int"/>/<see langword="string"/>/<see cref="Autodesk.Revit.DB.ElementId"/>/
/// <see langword="double"/> (the last requires an explicit epsilon) values, and for both
/// <see cref="Autodesk.Revit.DB.BuiltInParameter"/> and arbitrary shared/project parameters identified by
/// their definition's <see cref="Autodesk.Revit.DB.ElementId"/>. Unlike the once-only methods above,
/// the <c>WhereParameterEquals</c>/<c>WhereParameterNotEquals</c> family may be called any number of
/// times per chain - each call narrows the result further, mirroring how multiple
/// <see cref="Autodesk.Revit.DB.FilteredElementCollector.WherePasses(Autodesk.Revit.DB.ElementFilter)"/> calls compose.
/// <see cref="NotOfCategory"/>/<see cref="NotOfCategories"/> are once-only (mirroring
/// <see cref="OfCategory"/>/<see cref="OfCategories"/>) but are a *separate* slot from the positive
/// category filters - the two can be combined in the same chain, they're independent quick filters.
///
/// The values returned by <see cref="ToElements"/>/<see cref="ToElementIds"/> are shared across every caller
/// that hits the same cache entry - do not cast them to a mutable collection type and modify them, that would
/// silently corrupt the cached result for everyone else who queries the same chain.
/// </summary>
public sealed class CachedElementCollector
{
    private readonly Document _document;
    private readonly IRevitTransactionMemoryCache _cache;
    private readonly string[] _keyFragments;
    private readonly Action<FilteredElementCollector>[] _steps;
    private readonly bool _hasClassFilter;
    private readonly bool _hasNotClassFilter;
    private readonly bool _hasCategoryFilter;
    private readonly bool _hasNotCategoryFilter;
    private readonly bool _hasElementTypeFilter;
    private readonly bool _hasExcluding;

    internal CachedElementCollector(Document document, IRevitTransactionMemoryCache cache)
        : this(document, cache, [], [], false, false, false, false, false, false)
    {
    }

    private CachedElementCollector(
        Document document,
        IRevitTransactionMemoryCache cache,
        string[] keyFragments,
        Action<FilteredElementCollector>[] steps,
        bool hasClassFilter,
        bool hasNotClassFilter,
        bool hasCategoryFilter,
        bool hasNotCategoryFilter,
        bool hasElementTypeFilter,
        bool hasExcluding)
    {
        _document = document;
        _cache = cache;
        _keyFragments = keyFragments;
        _steps = steps;
        _hasClassFilter = hasClassFilter;
        _hasNotClassFilter = hasNotClassFilter;
        _hasCategoryFilter = hasCategoryFilter;
        _hasNotCategoryFilter = hasNotCategoryFilter;
        _hasElementTypeFilter = hasElementTypeFilter;
        _hasExcluding = hasExcluding;
    }

    /// <summary>Equivalent to <see cref="Autodesk.Revit.DB.FilteredElementCollector.OfClass(Type)"/>. May be called at most once.</summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="elementClass"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.InvalidOperationException"><see cref="OfClass"/> has already been called on this chain.</exception>
    public CachedElementCollector OfClass(Type elementClass)
    {
        ArgumentNullException.ThrowIfNull(elementClass);

        if (_hasClassFilter)
            throw new InvalidOperationException($"{nameof(OfClass)} has already been called on this {nameof(CachedElementCollector)}.");

        return With(
            $"OfClass:{elementClass.FullName}",
            collector => collector.OfClass(elementClass),
            hasClassFilter: true);
    }

    /// <summary>
    /// Sugar for <see cref="OfClass"/> - equivalent to <c>OfClass(typeof(TElement))</c>.
    /// May be called at most once (same restriction as <see cref="OfClass"/>).
    /// </summary>
    /// <typeparam name="TElement">The element class to filter to.</typeparam>
    /// <exception cref="System.InvalidOperationException"><see cref="OfClass"/> has already been called on this chain.</exception>
    public CachedElementCollector Of<TElement>()
        where TElement : Element =>
        OfClass(typeof(TElement));

    /// <summary>
    /// Equivalent to <c>WherePasses(new ElementClassFilter(elementClass, inverted: true))</c> - filters to
    /// elements *not* of <paramref name="elementClass"/>. May be called at most once, and not combined with
    /// <see cref="NotOf{TElement}"/> (same "excluded class" slot). Composable with <see cref="OfClass"/>/
    /// <see cref="Of{TElement}"/> - unlike those, this is a separate filter, not an alternative way of
    /// expressing the same one.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="elementClass"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.InvalidOperationException">
    /// <see cref="NotOfClass"/> or <see cref="NotOf{TElement}"/> has already been called on this chain.
    /// </exception>
    public CachedElementCollector NotOfClass(Type elementClass)
    {
        ArgumentNullException.ThrowIfNull(elementClass);

        if (_hasNotClassFilter)
            throw new InvalidOperationException(
                $"{nameof(NotOfClass)}/{nameof(NotOf)} has already been called on this {nameof(CachedElementCollector)}.");

        return With(
            $"NotOfClass:{elementClass.FullName}",
            collector => collector.WherePasses(new ElementClassFilter(elementClass, inverted: true)),
            hasNotClassFilter: true);
    }

    /// <summary>
    /// Sugar for <see cref="NotOfClass"/> - equivalent to <c>NotOfClass(typeof(TElement))</c>.
    /// May be called at most once (same restriction as <see cref="NotOfClass"/>).
    /// </summary>
    /// <typeparam name="TElement">The element class to exclude.</typeparam>
    /// <exception cref="System.InvalidOperationException">
    /// <see cref="NotOfClass"/> or <see cref="NotOf{TElement}"/> has already been called on this chain.
    /// </exception>
    public CachedElementCollector NotOf<TElement>()
        where TElement : Element =>
        NotOfClass(typeof(TElement));

    /// <summary>
    /// Equivalent to <see cref="Autodesk.Revit.DB.FilteredElementCollector.OfCategory(Autodesk.Revit.DB.BuiltInCategory)"/>.
    /// May be called at most once, and not combined with <see cref="OfCategories"/> (both target categories -
    /// use one or the other).
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// <see cref="OfCategory"/> or <see cref="OfCategories"/> has already been called on this chain.
    /// </exception>
    public CachedElementCollector OfCategory(BuiltInCategory category)
    {
        if (_hasCategoryFilter)
            throw new InvalidOperationException(
                $"{nameof(OfCategory)}/{nameof(OfCategories)} has already been called on this {nameof(CachedElementCollector)}.");

        return With(
            $"OfCategory:{category}",
            collector => collector.OfCategory(category),
            hasCategoryFilter: true);
    }

    /// <summary>
    /// Equivalent to <c>WherePasses(new ElementMulticategoryFilter(categories))</c> - filters to elements
    /// belonging to any of <paramref name="categories"/>. May be called at most once, and not combined with
    /// <see cref="OfCategory"/> (both target categories - use one or the other).
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="categories"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException"><paramref name="categories"/> is empty.</exception>
    /// <exception cref="System.InvalidOperationException">
    /// <see cref="OfCategory"/> or <see cref="OfCategories"/> has already been called on this chain.
    /// </exception>
    public CachedElementCollector OfCategories(IEnumerable<BuiltInCategory> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        var categoryList = categories.ToArray();
        if (categoryList.Length == 0)
            throw new ArgumentException("Must specify at least one category.", nameof(categories));

        if (_hasCategoryFilter)
            throw new InvalidOperationException(
                $"{nameof(OfCategory)}/{nameof(OfCategories)} has already been called on this {nameof(CachedElementCollector)}.");

        var sortedCategoryValues = categoryList.Select(category => (int)category).OrderBy(value => value);

        return With(
            $"OfCategories:{string.Join(",", sortedCategoryValues)}",
            collector => collector.WherePasses(new ElementMulticategoryFilter(categoryList)),
            hasCategoryFilter: true);
    }

    /// <summary>
    /// Equivalent to <c>WherePasses(new ElementCategoryFilter(category, inverted: true))</c> - filters to
    /// elements *not* belonging to <paramref name="category"/>. May be called at most once, and not combined
    /// with <see cref="NotOfCategories"/> (same "excluded category" slot). Composable with
    /// <see cref="OfCategory"/>/<see cref="OfCategories"/> - unlike those, this is a separate filter, not an
    /// alternative way of expressing the same one.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// <see cref="NotOfCategory"/> or <see cref="NotOfCategories"/> has already been called on this chain.
    /// </exception>
    public CachedElementCollector NotOfCategory(BuiltInCategory category)
    {
        if (_hasNotCategoryFilter)
            throw new InvalidOperationException(
                $"{nameof(NotOfCategory)}/{nameof(NotOfCategories)} has already been called on this {nameof(CachedElementCollector)}.");

        return With(
            $"NotOfCategory:{category}",
            collector => collector.WherePasses(new ElementCategoryFilter(category, inverted: true)),
            hasNotCategoryFilter: true);
    }

    /// <summary>
    /// Equivalent to <c>WherePasses(new ElementMulticategoryFilter(categories, inverted: true))</c> - filters
    /// to elements not belonging to any of <paramref name="categories"/>. May be called at most once, and not
    /// combined with <see cref="NotOfCategory"/> (same "excluded category" slot). Composable with
    /// <see cref="OfCategory"/>/<see cref="OfCategories"/>, same as <see cref="NotOfCategory"/>.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="categories"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException"><paramref name="categories"/> is empty.</exception>
    /// <exception cref="System.InvalidOperationException">
    /// <see cref="NotOfCategory"/> or <see cref="NotOfCategories"/> has already been called on this chain.
    /// </exception>
    public CachedElementCollector NotOfCategories(IEnumerable<BuiltInCategory> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        var categoryList = categories.ToArray();
        if (categoryList.Length == 0)
            throw new ArgumentException("Must specify at least one category.", nameof(categories));

        if (_hasNotCategoryFilter)
            throw new InvalidOperationException(
                $"{nameof(NotOfCategory)}/{nameof(NotOfCategories)} has already been called on this {nameof(CachedElementCollector)}.");

        var sortedCategoryValues = categoryList.Select(category => (int)category).OrderBy(value => value);

        return With(
            $"NotOfCategories:{string.Join(",", sortedCategoryValues)}",
            collector => collector.WherePasses(new ElementMulticategoryFilter(categoryList, inverted: true)),
            hasNotCategoryFilter: true);
    }

    /// <summary>
    /// Equivalent to <see cref="Autodesk.Revit.DB.FilteredElementCollector.WhereElementIsElementType()"/>. May be called at most
    /// once, and not combined with <see cref="WhereElementIsNotElementType"/>.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// <see cref="WhereElementIsElementType"/> or <see cref="WhereElementIsNotElementType"/> has already been called on this chain.
    /// </exception>
    public CachedElementCollector WhereElementIsElementType()
    {
        if (_hasElementTypeFilter)
            throw new InvalidOperationException(
                $"{nameof(WhereElementIsElementType)}/{nameof(WhereElementIsNotElementType)} has already been called on this {nameof(CachedElementCollector)}.");

        return With(
            nameof(WhereElementIsElementType),
            collector => collector.WhereElementIsElementType(),
            hasElementTypeFilter: true);
    }

    /// <summary>
    /// Equivalent to <see cref="Autodesk.Revit.DB.FilteredElementCollector.WhereElementIsNotElementType()"/>. May be called at
    /// most once, and not combined with <see cref="WhereElementIsElementType"/>.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// <see cref="WhereElementIsElementType"/> or <see cref="WhereElementIsNotElementType"/> has already been called on this chain.
    /// </exception>
    public CachedElementCollector WhereElementIsNotElementType()
    {
        if (_hasElementTypeFilter)
            throw new InvalidOperationException(
                $"{nameof(WhereElementIsElementType)}/{nameof(WhereElementIsNotElementType)} has already been called on this {nameof(CachedElementCollector)}.");

        return With(
            nameof(WhereElementIsNotElementType),
            collector => collector.WhereElementIsNotElementType(),
            hasElementTypeFilter: true);
    }

    /// <summary>
    /// Equivalent to <see cref="Autodesk.Revit.DB.FilteredElementCollector.Excluding(ICollection{Autodesk.Revit.DB.ElementId})"/>. May be called at most once - pass the
    /// full set of ids to exclude in a single call.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="elementIds"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.InvalidOperationException"><see cref="Excluding"/> has already been called on this chain.</exception>
    public CachedElementCollector Excluding(ICollection<ElementId> elementIds)
    {
        ArgumentNullException.ThrowIfNull(elementIds);

        if (_hasExcluding)
            throw new InvalidOperationException($"{nameof(Excluding)} has already been called on this {nameof(CachedElementCollector)}.");

        var ids = elementIds.ToArray();
        var sortedIdValues = ids.Select(id => id.Value).OrderBy(value => value);

        return With(
            $"Excluding:{string.Join(",", sortedIdValues)}",
            collector => collector.Excluding(ids),
            hasExcluding: true);
    }

    /// <summary>
    /// Equivalent to <c>WherePasses(new ElementParameterFilter(ParameterFilterRuleFactory.CreateEqualsRule(...)))</c>
    /// for the built-in integer-valued parameter <paramref name="parameter"/>. Unlike <see cref="OfClass"/>/
    /// <see cref="OfCategory"/>/<see cref="OfCategories"/>, may be called any number of times per chain - each
    /// call narrows the result further, matching how multiple
    /// <see cref="Autodesk.Revit.DB.FilteredElementCollector.WherePasses(Autodesk.Revit.DB.ElementFilter)"/> calls compose.
    /// </summary>
    public CachedElementCollector WhereParameterEquals(BuiltInParameter parameter, int value) =>
        WithParameterFilter(
            nameof(WhereParameterEquals),
            $"BuiltIn:{(int)parameter}",
            $"Int:{value}",
            ParameterFilterRuleFactory.CreateEqualsRule(new ElementId(parameter), value));

    /// <summary>
    /// String overload of <see cref="WhereParameterEquals(Autodesk.Revit.DB.BuiltInParameter,int)"/> - the comparison is
    /// case-insensitive. May be called any number of times per chain, same as the <see langword="int"/> overload.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public CachedElementCollector WhereParameterEquals(BuiltInParameter parameter, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return WithParameterFilter(
            nameof(WhereParameterEquals),
            $"BuiltIn:{(int)parameter}",
            $"String:{value}",
            ParameterFilterRuleFactory.CreateEqualsRule(new ElementId(parameter), value, caseSensitive: false));
    }

    /// <summary>
    /// <see cref="Autodesk.Revit.DB.ElementId"/> overload of
    /// <see cref="WhereParameterEquals(Autodesk.Revit.DB.BuiltInParameter,int)"/> - for parameters that reference
    /// another element (e.g. a level/type). May be called any number of times per chain, same as the
    /// <see langword="int"/> overload.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public CachedElementCollector WhereParameterEquals(BuiltInParameter parameter, ElementId value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return WithParameterFilter(
            nameof(WhereParameterEquals),
            $"BuiltIn:{(int)parameter}",
            $"ElementId:{value.Value}",
            ParameterFilterRuleFactory.CreateEqualsRule(new ElementId(parameter), value));
    }

    /// <summary>
    /// <see langword="double"/> overload of <see cref="WhereParameterEquals(Autodesk.Revit.DB.BuiltInParameter,int)"/>.
    /// Unlike the other overloads, requires an explicit <paramref name="epsilon"/> - exact <see langword="double"/>
    /// equality is rarely what's intended, and Revit stores lengths/areas/angles in internal units, where a
    /// "reasonable" tolerance depends on what the parameter actually measures, so no default is assumed.
    /// </summary>
    public CachedElementCollector WhereParameterEquals(BuiltInParameter parameter, double value, double epsilon) =>
        WithParameterFilter(
            nameof(WhereParameterEquals),
            $"BuiltIn:{(int)parameter}",
            $"Double:{value}:{epsilon}",
            ParameterFilterRuleFactory.CreateEqualsRule(new ElementId(parameter), value, epsilon));

    /// <summary>
    /// Shared/project-parameter overload of <see cref="WhereParameterEquals(Autodesk.Revit.DB.BuiltInParameter,int)"/> -
    /// for parameters that have no <see cref="BuiltInParameter"/>, identified instead by their definition's
    /// <paramref name="parameterId"/> (e.g. <c>SharedParameterElement.Id</c>). May be called any number of
    /// times per chain, same as the <see cref="BuiltInParameter"/> overloads.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="parameterId"/> is <see langword="null"/>.</exception>
    public CachedElementCollector WhereParameterEquals(ElementId parameterId, int value)
    {
        ArgumentNullException.ThrowIfNull(parameterId);

        return WithParameterFilter(
            nameof(WhereParameterEquals),
            $"Id:{parameterId.Value}",
            $"Int:{value}",
            ParameterFilterRuleFactory.CreateEqualsRule(parameterId, value));
    }

    /// <summary>
    /// String overload of <see cref="WhereParameterEquals(Autodesk.Revit.DB.ElementId,int)"/> - the comparison is
    /// case-insensitive, same as the <see cref="BuiltInParameter"/> overload.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="parameterId"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    public CachedElementCollector WhereParameterEquals(ElementId parameterId, string value)
    {
        ArgumentNullException.ThrowIfNull(parameterId);
        ArgumentNullException.ThrowIfNull(value);

        return WithParameterFilter(
            nameof(WhereParameterEquals),
            $"Id:{parameterId.Value}",
            $"String:{value}",
            ParameterFilterRuleFactory.CreateEqualsRule(parameterId, value, caseSensitive: false));
    }

    /// <summary>
    /// <see cref="Autodesk.Revit.DB.ElementId"/>-value overload of <see cref="WhereParameterEquals(Autodesk.Revit.DB.ElementId,int)"/>.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="parameterId"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    public CachedElementCollector WhereParameterEquals(ElementId parameterId, ElementId value)
    {
        ArgumentNullException.ThrowIfNull(parameterId);
        ArgumentNullException.ThrowIfNull(value);

        return WithParameterFilter(
            nameof(WhereParameterEquals),
            $"Id:{parameterId.Value}",
            $"ElementId:{value.Value}",
            ParameterFilterRuleFactory.CreateEqualsRule(parameterId, value));
    }

    /// <summary>
    /// <see langword="double"/> overload of <see cref="WhereParameterEquals(Autodesk.Revit.DB.ElementId,int)"/> -
    /// requires an explicit <paramref name="epsilon"/>, same as the <see cref="BuiltInParameter"/> overload.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="parameterId"/> is <see langword="null"/>.</exception>
    public CachedElementCollector WhereParameterEquals(ElementId parameterId, double value, double epsilon)
    {
        ArgumentNullException.ThrowIfNull(parameterId);

        return WithParameterFilter(
            nameof(WhereParameterEquals),
            $"Id:{parameterId.Value}",
            $"Double:{value}:{epsilon}",
            ParameterFilterRuleFactory.CreateEqualsRule(parameterId, value, epsilon));
    }

    /// <summary>
    /// Negated overload of <see cref="WhereParameterEquals(Autodesk.Revit.DB.BuiltInParameter,int)"/> -
    /// equivalent to <c>WherePasses(new ElementParameterFilter(rule, inverted: true))</c>. Same call-any-number-
    /// of-times behaviour as <see cref="WhereParameterEquals(Autodesk.Revit.DB.BuiltInParameter,int)"/>.
    /// </summary>
    public CachedElementCollector WhereParameterNotEquals(BuiltInParameter parameter, int value) =>
        WithParameterFilter(
            nameof(WhereParameterNotEquals),
            $"BuiltIn:{(int)parameter}",
            $"Int:{value}",
            ParameterFilterRuleFactory.CreateEqualsRule(new ElementId(parameter), value),
            inverted: true);

    /// <summary>
    /// String overload of <see cref="WhereParameterNotEquals(Autodesk.Revit.DB.BuiltInParameter,int)"/> - the
    /// comparison is case-insensitive, same as <see cref="WhereParameterEquals(Autodesk.Revit.DB.BuiltInParameter,string)"/>.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public CachedElementCollector WhereParameterNotEquals(BuiltInParameter parameter, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return WithParameterFilter(
            nameof(WhereParameterNotEquals),
            $"BuiltIn:{(int)parameter}",
            $"String:{value}",
            ParameterFilterRuleFactory.CreateEqualsRule(new ElementId(parameter), value, caseSensitive: false),
            inverted: true);
    }

    /// <summary>
    /// <see cref="Autodesk.Revit.DB.ElementId"/> overload of <see cref="WhereParameterNotEquals(Autodesk.Revit.DB.BuiltInParameter,int)"/>.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public CachedElementCollector WhereParameterNotEquals(BuiltInParameter parameter, ElementId value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return WithParameterFilter(
            nameof(WhereParameterNotEquals),
            $"BuiltIn:{(int)parameter}",
            $"ElementId:{value.Value}",
            ParameterFilterRuleFactory.CreateEqualsRule(new ElementId(parameter), value),
            inverted: true);
    }

    /// <summary>
    /// <see langword="double"/> overload of <see cref="WhereParameterNotEquals(Autodesk.Revit.DB.BuiltInParameter,int)"/> -
    /// requires an explicit <paramref name="epsilon"/>, same as <see cref="WhereParameterEquals(Autodesk.Revit.DB.BuiltInParameter,double,double)"/>.
    /// </summary>
    public CachedElementCollector WhereParameterNotEquals(BuiltInParameter parameter, double value, double epsilon) =>
        WithParameterFilter(
            nameof(WhereParameterNotEquals),
            $"BuiltIn:{(int)parameter}",
            $"Double:{value}:{epsilon}",
            ParameterFilterRuleFactory.CreateEqualsRule(new ElementId(parameter), value, epsilon),
            inverted: true);

    /// <summary>
    /// Shared/project-parameter overload of <see cref="WhereParameterNotEquals(Autodesk.Revit.DB.BuiltInParameter,int)"/>,
    /// same as <see cref="WhereParameterEquals(Autodesk.Revit.DB.ElementId,int)"/> is to
    /// <see cref="WhereParameterEquals(Autodesk.Revit.DB.BuiltInParameter,int)"/>.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="parameterId"/> is <see langword="null"/>.</exception>
    public CachedElementCollector WhereParameterNotEquals(ElementId parameterId, int value)
    {
        ArgumentNullException.ThrowIfNull(parameterId);

        return WithParameterFilter(
            nameof(WhereParameterNotEquals),
            $"Id:{parameterId.Value}",
            $"Int:{value}",
            ParameterFilterRuleFactory.CreateEqualsRule(parameterId, value),
            inverted: true);
    }

    /// <summary>
    /// String overload of <see cref="WhereParameterNotEquals(Autodesk.Revit.DB.ElementId,int)"/> - the
    /// comparison is case-insensitive.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="parameterId"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    public CachedElementCollector WhereParameterNotEquals(ElementId parameterId, string value)
    {
        ArgumentNullException.ThrowIfNull(parameterId);
        ArgumentNullException.ThrowIfNull(value);

        return WithParameterFilter(
            nameof(WhereParameterNotEquals),
            $"Id:{parameterId.Value}",
            $"String:{value}",
            ParameterFilterRuleFactory.CreateEqualsRule(parameterId, value, caseSensitive: false),
            inverted: true);
    }

    /// <summary>
    /// <see cref="Autodesk.Revit.DB.ElementId"/>-value overload of <see cref="WhereParameterNotEquals(Autodesk.Revit.DB.ElementId,int)"/>.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="parameterId"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    public CachedElementCollector WhereParameterNotEquals(ElementId parameterId, ElementId value)
    {
        ArgumentNullException.ThrowIfNull(parameterId);
        ArgumentNullException.ThrowIfNull(value);

        return WithParameterFilter(
            nameof(WhereParameterNotEquals),
            $"Id:{parameterId.Value}",
            $"ElementId:{value.Value}",
            ParameterFilterRuleFactory.CreateEqualsRule(parameterId, value),
            inverted: true);
    }

    /// <summary>
    /// <see langword="double"/> overload of <see cref="WhereParameterNotEquals(Autodesk.Revit.DB.ElementId,int)"/> -
    /// requires an explicit <paramref name="epsilon"/>.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="parameterId"/> is <see langword="null"/>.</exception>
    public CachedElementCollector WhereParameterNotEquals(ElementId parameterId, double value, double epsilon)
    {
        ArgumentNullException.ThrowIfNull(parameterId);

        return WithParameterFilter(
            nameof(WhereParameterNotEquals),
            $"Id:{parameterId.Value}",
            $"Double:{value}:{epsilon}",
            ParameterFilterRuleFactory.CreateEqualsRule(parameterId, value, epsilon),
            inverted: true);
    }

    private CachedElementCollector WithParameterFilter(
        string methodName, string parameterKeyFragment, string valueFragment, FilterRule rule, bool inverted = false) =>
        With(
            $"{methodName}:{parameterKeyFragment}:{valueFragment}",
            collector => collector.WherePasses(new ElementParameterFilter(rule, inverted)));

    /// <summary>
    /// Equivalent to <see cref="Autodesk.Revit.DB.FilteredElementCollector.ToElements()"/>. On a cache hit, returns the same
    /// shared list every caller with an equivalent chain gets - see the type-level remarks about mutation.
    /// </summary>
    public IReadOnlyList<Element> ToElements() =>
        _cache.GetOrCreate(BuildKey(nameof(ToElements)), () =>
        {
            var collector = new FilteredElementCollector(_document);
            foreach (var step in _steps)
            {
                step(collector);
            }

            return (IReadOnlyList<Element>)collector.ToElements().ToList();
        })!;

    /// <summary>
    /// Equivalent to <see cref="Autodesk.Revit.DB.FilteredElementCollector.ToElementIds()"/>. On a cache hit, returns the same
    /// shared list every caller with an equivalent chain gets - see the type-level remarks about mutation.
    /// </summary>
    public IReadOnlyList<ElementId> ToElementIds() =>
        _cache.GetOrCreate(BuildKey(nameof(ToElementIds)), () =>
        {
            var collector = new FilteredElementCollector(_document);
            foreach (var step in _steps)
            {
                step(collector);
            }

            return (IReadOnlyList<ElementId>)collector.ToElementIds().ToList();
        })!;

    private CachedElementCollector With(
        string keyFragment,
        Action<FilteredElementCollector> step,
        bool hasClassFilter = false,
        bool hasNotClassFilter = false,
        bool hasCategoryFilter = false,
        bool hasNotCategoryFilter = false,
        bool hasElementTypeFilter = false,
        bool hasExcluding = false) =>
        new(
            _document,
            _cache,
            [.. _keyFragments, keyFragment],
            [.. _steps, step],
            _hasClassFilter || hasClassFilter,
            _hasNotClassFilter || hasNotClassFilter,
            _hasCategoryFilter || hasCategoryFilter,
            _hasNotCategoryFilter || hasNotCategoryFilter,
            _hasElementTypeFilter || hasElementTypeFilter,
            _hasExcluding || hasExcluding);

    private string BuildKey(string terminal) =>
        CachedElementCollectorKeyBuilder.Build(RuntimeHelpers.GetHashCode(_document), terminal, _keyFragments);
}
