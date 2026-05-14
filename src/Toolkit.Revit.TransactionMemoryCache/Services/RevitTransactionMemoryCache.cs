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
    private CancellationTokenSource _refreshCancellationTokenSource = new();
    private bool _isInitialized = false;

    public TItem? GetOrCreate<TItem>(object key, Func<TItem> factory) {
        CancellationTokenSource? cancellationTokenSource = _refreshCancellationTokenSource;
        return memoryCache.GetOrCreate(key, entry => {
            entry.AddExpirationToken(new CancellationChangeToken(cancellationTokenSource.Token));
            entry.SlidingExpiration = TimeSpan.FromMinutes(10);
            return factory();
        });
    }

    public void Initialize() {
        if (_isInitialized) return;
        revitContext.ControlledApplication!.DocumentChanged += OnDocumentChanged;
        revitContext.UIControlledApplication!.ViewActivated += OnViewActivated;
        _isInitialized = true;
    }

    public void Deinitialize() {
        if (!_isInitialized) return;
        revitContext.ControlledApplication!.DocumentChanged -= OnDocumentChanged;
        revitContext.UIControlledApplication!.ViewActivated -= OnViewActivated;
        _isInitialized = false;
    }

    public void Dispose() {
        _refreshCancellationTokenSource.Cancel();
        _refreshCancellationTokenSource.Dispose();
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e) => Refresh();
    private void OnViewActivated(object sender, ViewActivatedEventArgs e) => Refresh();
    private void Refresh() {
        _refreshCancellationTokenSource.Cancel();
        _refreshCancellationTokenSource.Dispose();
        _refreshCancellationTokenSource = new();
    }
}
