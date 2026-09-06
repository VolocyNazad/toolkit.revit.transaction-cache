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
/// and shares the same once-only restriction.
///
/// <c>WherePasses(ElementFilter)</c> is intentionally not supported: most <see cref="Autodesk.Revit.DB.ElementFilter"/>
/// subclasses don't implement value equality, so two independently constructed but logically identical
/// filters would not produce the same cache key.
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
    private readonly bool _hasCategoryFilter;
    private readonly bool _hasElementTypeFilter;
    private readonly bool _hasExcluding;

    internal CachedElementCollector(Document document, IRevitTransactionMemoryCache cache)
        : this(document, cache, [], [], false, false, false, false)
    {
    }

    private CachedElementCollector(
        Document document,
        IRevitTransactionMemoryCache cache,
        string[] keyFragments,
        Action<FilteredElementCollector>[] steps,
        bool hasClassFilter,
        bool hasCategoryFilter,
        bool hasElementTypeFilter,
        bool hasExcluding)
    {
        _document = document;
        _cache = cache;
        _keyFragments = keyFragments;
        _steps = steps;
        _hasClassFilter = hasClassFilter;
        _hasCategoryFilter = hasCategoryFilter;
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

    /// <summary>Equivalent to <see cref="Autodesk.Revit.DB.FilteredElementCollector.OfCategory(Autodesk.Revit.DB.BuiltInCategory)"/>. May be called at most once.</summary>
    /// <exception cref="System.InvalidOperationException"><see cref="OfCategory"/> has already been called on this chain.</exception>
    public CachedElementCollector OfCategory(BuiltInCategory category)
    {
        if (_hasCategoryFilter)
            throw new InvalidOperationException($"{nameof(OfCategory)} has already been called on this {nameof(CachedElementCollector)}.");

        return With(
            $"OfCategory:{category}",
            collector => collector.OfCategory(category),
            hasCategoryFilter: true);
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
        bool hasCategoryFilter = false,
        bool hasElementTypeFilter = false,
        bool hasExcluding = false) =>
        new(
            _document,
            _cache,
            [.. _keyFragments, keyFragment],
            [.. _steps, step],
            _hasClassFilter || hasClassFilter,
            _hasCategoryFilter || hasCategoryFilter,
            _hasElementTypeFilter || hasElementTypeFilter,
            _hasExcluding || hasExcluding);

    private string BuildKey(string terminal) =>
        CachedElementCollectorKeyBuilder.Build(RuntimeHelpers.GetHashCode(_document), terminal, _keyFragments);
}
