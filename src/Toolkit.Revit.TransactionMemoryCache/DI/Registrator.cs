using Microsoft.Extensions.DependencyInjection;
using Revit.TransactionMemoryCache.Abstractions.Services;
using Revit.TransactionMemoryCache.Services;

namespace Revit.TransactionMemoryCache.DI;

public static class Registrator
{
	extension(IServiceCollection services)
	{
        public IServiceCollection AddTransactionMemoryCache() => services
            .AddMemoryCache()
            .AddSingleton<RevitTransactionMemoryCache>()
            .AddSingleton<IRevitTransactionMemoryCache>(i => i.GetRequiredService<RevitTransactionMemoryCache>())
            .AddSingleton<IRevitTransactionMemoryCacheInitializer>(i => i.GetRequiredService<RevitTransactionMemoryCache>())
       ;
    }
}
