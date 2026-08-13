using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI.Events;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Revit.Context.Abstractions.Services;
using Revit.TransactionMemoryCache.Abstractions.Services;

namespace Revit.TransactionMemoryCache.Services;

/// <summary>
/// Default implementation of <see cref="IRevitTransactionMemoryCache"/> and <see cref="IRevitTransactionMemoryCacheInitializer"/>.
/// Wraps an <see cref="IMemoryCache"/> and invalidates every cached entry by cancelling a shared
/// <see cref="CancellationTokenSource"/> whenever the active Revit document changes or the active view is switched.
/// </summary>
internal sealed class RevitTransactionMemoryCache(IRevitContext revitContext, IMemoryCache memoryCache)
    : IRevitTransactionMemoryCache, IRevitTransactionMemoryCacheInitializer, IDisposable
{
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _refreshCancellationTokenSource = new();
    private bool _isInitialized;
    private bool _isDisposed;

    /// <inheritdoc />
    public TItem? GetOrCreate<TItem>(object key, Func<TItem> factory) {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
#else
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (factory is null) throw new ArgumentNullException(nameof(factory));
#endif

        CancellationToken refreshToken;
        lock (_lifecycleLock) {
            ThrowIfDisposed();
            refreshToken = _refreshCancellationTokenSource!.Token;
        }

        return memoryCache.GetOrCreate(key, entry => {
            entry.AddExpirationToken(new CancellationChangeToken(refreshToken));
            entry.SlidingExpiration = TimeSpan.FromMinutes(10);
            return factory();
        });
    }

    /// <inheritdoc />
    public void Initialize() {
        lock (_lifecycleLock) {
            ThrowIfDisposed();
            if (_isInitialized) return;

            revitContext.ControlledApplication!.DocumentChanged += OnDocumentChanged;
            revitContext.UIControlledApplication!.ViewActivated += OnViewActivated;
            _isInitialized = true;
        }
    }

    /// <inheritdoc />
    public void Deinitialize() {
        lock (_lifecycleLock) {
            DeinitializeCore();
        }
    }

    /// <summary>
    /// Unsubscribes from Revit events (if initialized) and cancels/disposes the shared refresh token source.
    /// </summary>
    public void Dispose() {
        CancellationTokenSource? cancellationTokenSource;

        lock (_lifecycleLock) {
            if (_isDisposed) return;

            DeinitializeCore();
            _isDisposed = true;
            cancellationTokenSource = _refreshCancellationTokenSource;
            _refreshCancellationTokenSource = null;
        }

        if (cancellationTokenSource is not null) {
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
        }
    }

    /// <summary>Handles the <c>DocumentChanged</c> event by invalidating all cached entries.</summary>
    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e) => Refresh();

    /// <summary>Handles the <c>ViewActivated</c> event by invalidating all cached entries.</summary>
    private void OnViewActivated(object? sender, ViewActivatedEventArgs e) => Refresh();

    /// <summary>Unsubscribes from Revit events, if currently subscribed. Must be called under <see cref="_lifecycleLock"/>.</summary>
    private void DeinitializeCore() {
        if (!_isInitialized) return;

        revitContext.ControlledApplication!.DocumentChanged -= OnDocumentChanged;
        revitContext.UIControlledApplication!.ViewActivated -= OnViewActivated;
        _isInitialized = false;
    }

    /// <summary>Throws <see cref="ObjectDisposedException"/> if the cache has already been disposed.</summary>
    private void ThrowIfDisposed() {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_isDisposed, this);
#else
        if (_isDisposed) throw new ObjectDisposedException(nameof(RevitTransactionMemoryCache));
#endif
    }

    /// <summary>
    /// Invalidates every entry currently cached via <see cref="GetOrCreate{TItem}"/> by cancelling and
    /// replacing the shared <see cref="CancellationTokenSource"/> that all cache entries are linked to.
    /// </summary>
    private void Refresh() {
        CancellationTokenSource cancellationTokenSource;

        lock (_lifecycleLock) {
            if (_isDisposed) return;
            cancellationTokenSource = _refreshCancellationTokenSource!;
            _refreshCancellationTokenSource = new CancellationTokenSource();
        }

        cancellationTokenSource.Cancel();
        cancellationTokenSource.Dispose();
    }
}
