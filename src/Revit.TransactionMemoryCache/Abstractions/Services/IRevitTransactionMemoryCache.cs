namespace Revit.TransactionMemoryCache.Abstractions.Services;

/// <summary>
/// Provides in-memory memoization of values whose lifetime is tied to the currently open Revit document,
/// automatically invalidated on document changes or view activation.
/// </summary>
public interface IRevitTransactionMemoryCache
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, computing and caching it via <paramref name="factory"/>
    /// if it is not already present or has been invalidated.
    /// </summary>
    /// <typeparam name="TItem">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">Produces the value to cache when it is missing.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="key"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ObjectDisposedException">The cache has been disposed.</exception>
    TItem? GetOrCreate<TItem>(object key, Func<TItem> factory);
}
