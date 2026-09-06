namespace Revit.TransactionMemoryCache.Abstractions.Services;

/// <summary>
/// Controls the lifecycle of an <see cref="IRevitTransactionMemoryCache"/> implementation:
/// subscribing to and unsubscribing from the Revit events that invalidate the cache.
/// </summary>
public interface IRevitTransactionMemoryCacheInitializer
{
    /// <summary>
    /// Whether <see cref="Initialize"/> has been called and not since undone by <see cref="Deinitialize"/>.
    /// Callers that rely on automatic invalidation (e.g. <c>CachedElementCollector</c>) should check this
    /// before caching anything, since a cache that is never invalidated will silently return stale results
    /// forever once the document changes.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Unsubscribes from Revit lifecycle events. Safe to call multiple times or before <see cref="Initialize"/>.
    /// </summary>
    void Deinitialize();

    /// <summary>
    /// Subscribes to Revit lifecycle events (document changes, view activation) so the cache is invalidated
    /// automatically. Requires the underlying <c>IRevitContext</c> to already be initialized. Safe to call multiple times.
    /// </summary>
    void Initialize();
}
