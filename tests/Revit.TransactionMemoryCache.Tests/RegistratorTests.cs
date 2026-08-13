using Microsoft.Extensions.DependencyInjection;
using Revit.Context.DI;
using Revit.TransactionMemoryCache.Abstractions.Services;
using Revit.TransactionMemoryCache.DI;
using Xunit;

namespace Revit.TransactionMemoryCache.Tests;

public class RegistratorTests
{
    [Fact]
    public void AddTransactionMemoryCache_RegistersRevitTransactionMemoryCacheAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddRevitContext();
        services.AddTransactionMemoryCache();

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IRevitTransactionMemoryCache>();
        var second = provider.GetRequiredService<IRevitTransactionMemoryCache>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddTransactionMemoryCache_ResolvesSameInstanceForCacheAndInitializer()
    {
        var services = new ServiceCollection();
        services.AddRevitContext();
        services.AddTransactionMemoryCache();

        using var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredService<IRevitTransactionMemoryCache>();
        var initializer = provider.GetRequiredService<IRevitTransactionMemoryCacheInitializer>();

        Assert.Same(cache, initializer);
    }

    [Fact]
    public void AddTransactionMemoryCache_ReturnsSameServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddTransactionMemoryCache();

        Assert.Same(services, result);
    }
}
