using Autodesk.Revit.DB;
using Revit.TransactionMemoryCache.Abstractions.Services;

namespace Revit.TransactionMemoryCache.Services;

/// <inheritdoc cref="ICachedElementCollectorFactory" />
internal sealed class CachedElementCollectorFactory(
    IRevitTransactionMemoryCache cache,
    IRevitTransactionMemoryCacheInitializer initializer) : ICachedElementCollectorFactory
{
    /// <inheritdoc />
    public CachedElementCollector Create(Document document)
    {
        ThrowHelper.ThrowIfNull(document);

        if (!initializer.IsInitialized)
        {
            throw new InvalidOperationException(
                $"The Revit transaction memory cache has not been initialized. Call " +
                $"{nameof(IRevitTransactionMemoryCacheInitializer)}.{nameof(IRevitTransactionMemoryCacheInitializer.Initialize)}() " +
                $"during OnStartup before using {nameof(CachedElementCollector)} - otherwise cached results are " +
                "never invalidated when the document changes.");
        }

        return new CachedElementCollector(document, cache);
    }
}
