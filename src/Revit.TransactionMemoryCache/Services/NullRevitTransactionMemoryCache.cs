using Revit.TransactionMemoryCache.Abstractions.Services;

namespace Revit.TransactionMemoryCache.Services;

/// <summary>
/// No-op implementation of <see cref="IRevitTransactionMemoryCache"/> and <see cref="IRevitTransactionMemoryCacheInitializer"/>.
/// <see cref="GetOrCreate{TItem}"/> always invokes <c>factory</c> without caching the result, and
/// <see cref="Initialize"/>/<see cref="Deinitialize"/> do nothing. Intended as a safe default (e.g. an optional
/// constructor parameter, or a test double) for callers that need an <see cref="IRevitTransactionMemoryCache"/>
/// but don't have — or don't want — a real Revit document/context, mirroring how
/// <c>Microsoft.Extensions.Logging.Abstractions.NullLogger</c> is used.
/// </summary>
public sealed class NullRevitTransactionMemoryCache : IRevitTransactionMemoryCache, IRevitTransactionMemoryCacheInitializer
{
    /// <summary>The singleton instance.</summary>
    public static readonly NullRevitTransactionMemoryCache Instance = new();

    private NullRevitTransactionMemoryCache() { }

    /// <summary>
    /// Always <see langword="true"/> - a null cache never caches anything, so there is nothing to invalidate
    /// and no reason to require <see cref="Initialize"/> to have been called.
    /// </summary>
    public bool IsInitialized => true;

    /// <summary>Invokes <paramref name="factory"/> and returns its result directly, without caching it.</summary>
    /// <exception cref="System.ArgumentNullException"><paramref name="key"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    public TItem? GetOrCreate<TItem>(object key, Func<TItem> factory) {
#if NET8_0_OR_GREATER
        ThrowHelper.ThrowIfNull(key);
        ThrowHelper.ThrowIfNull(factory);
#else
        if (key is null) throw new System.ArgumentNullException(nameof(key));
        if (factory is null) throw new System.ArgumentNullException(nameof(factory));
#endif

        return factory();
    }

    /// <summary>Does nothing.</summary>
    public void Initialize() { }

    /// <summary>Does nothing.</summary>
    public void Deinitialize() { }
}
