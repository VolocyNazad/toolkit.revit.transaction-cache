using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Revit.TransactionMemoryCache.Abstractions.Services;
using Revit.TransactionMemoryCache.Services;

namespace Revit.TransactionMemoryCache.DI;

/// <summary>
/// Provides dependency injection registration extensions for the Revit transaction memory cache services.
/// </summary>
public static class Registrator
{
	extension(IServiceCollection services)
	{
        /// <summary>
        /// Registers <see cref="RevitTransactionMemoryCache"/> (and the underlying <see cref="IMemoryCache"/>
        /// via <see cref="MemoryCacheServiceCollectionExtensions.AddMemoryCache(IServiceCollection)"/>) as a singleton,
        /// exposing it as both <see cref="IRevitTransactionMemoryCache"/> and <see cref="IRevitTransactionMemoryCacheInitializer"/>,
        /// plus <see cref="ICachedElementCollectorFactory"/> for building <see cref="CachedElementCollector"/> instances.
        /// Requires <c>IRevitContext</c> to also be registered (e.g. via <c>AddRevitContext()</c>).
        /// </summary>
        /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
        public IServiceCollection AddTransactionMemoryCache() => services
            .AddMemoryCache()
            .AddSingleton<RevitTransactionMemoryCache>()
            .AddSingleton<IRevitTransactionMemoryCache>(i => i.GetRequiredService<RevitTransactionMemoryCache>())
            .AddSingleton<IRevitTransactionMemoryCacheInitializer>(i => i.GetRequiredService<RevitTransactionMemoryCache>())
            .AddSingleton<ICachedElementCollectorFactory, CachedElementCollectorFactory>()
       ;
    }
}
