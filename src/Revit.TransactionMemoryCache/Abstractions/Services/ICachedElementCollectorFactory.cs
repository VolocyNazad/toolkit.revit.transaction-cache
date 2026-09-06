using Autodesk.Revit.DB;
using Revit.TransactionMemoryCache.Services;

namespace Revit.TransactionMemoryCache.Abstractions.Services;

/// <summary>
/// Creates <see cref="CachedElementCollector"/> instances bound to a specific document, resolving
/// <see cref="IRevitTransactionMemoryCache"/> internally so callers don't have to thread it through manually.
/// </summary>
public interface ICachedElementCollectorFactory
{
    /// <summary>
    /// Creates a new <see cref="CachedElementCollector"/> for <paramref name="document"/>.
    /// </summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.InvalidOperationException">
    /// The underlying cache has not been initialized (see <see cref="IRevitTransactionMemoryCacheInitializer.Initialize"/>) -
    /// caching without automatic invalidation would silently return stale results after the document changes.
    /// </exception>
    CachedElementCollector Create(Document document);
}
