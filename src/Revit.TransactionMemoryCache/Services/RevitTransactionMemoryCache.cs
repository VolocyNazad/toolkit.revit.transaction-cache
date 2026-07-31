using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI.Events;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Revit.Context.Abstractions.Services;
using Revit.TransactionMemoryCache.Abstractions.Services;

namespace Revit.TransactionMemoryCache.Services;

internal sealed class RevitTransactionMemoryCache(IRevitContext revitContext, IMemoryCache memoryCache) 
    : IRevitTransactionMemoryCache, IRevitTransactionMemoryCacheInitializer, IDisposable
{
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _refreshCancellationTokenSource = new();
    private bool _isInitialized;
    private bool _isDisposed;

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

    public void Initialize() {
        lock (_lifecycleLock) {
            ThrowIfDisposed();
            if (_isInitialized) return;

            revitContext.ControlledApplication!.DocumentChanged += OnDocumentChanged;
            revitContext.UIControlledApplication!.ViewActivated += OnViewActivated;
            _isInitialized = true;
        }
    }

    public void Deinitialize() {
        lock (_lifecycleLock) {
            DeinitializeCore();
        }
    }

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

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e) => Refresh();
    private void OnViewActivated(object? sender, ViewActivatedEventArgs e) => Refresh();

    private void DeinitializeCore() {
        if (!_isInitialized) return;

        revitContext.ControlledApplication!.DocumentChanged -= OnDocumentChanged;
        revitContext.UIControlledApplication!.ViewActivated -= OnViewActivated;
        _isInitialized = false;
    }

    private void ThrowIfDisposed() {
#if NET8_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_isDisposed, this);
#else
        if (_isDisposed) throw new ObjectDisposedException(nameof(RevitTransactionMemoryCache));
#endif
    }

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
