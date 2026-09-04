using Microsoft.Extensions.DependencyInjection;
using Revit.Context.DI;
using Revit.TransactionMemoryCache.Abstractions.Services;
using Revit.TransactionMemoryCache.DI;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Revit.TransactionMemoryCache.Tests;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit test naming convention: MethodName_Scenario_ExpectedResult.")]
public class RevitTransactionMemoryCacheTests
{
    [Fact]
    public void GetOrCreate_ReturnsFactoryResult_AndCachesSubsequentCalls()
    {
        using var provider = CreateProvider();
        var cache = provider.GetRequiredService<IRevitTransactionMemoryCache>();
        var callCount = 0;

        var first = cache.GetOrCreate("key", () => { callCount++; return 42; });
        var second = cache.GetOrCreate("key", () => { callCount++; return 99; });

        Assert.Equal(42, first);
        Assert.Equal(42, second);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void GetOrCreate_ThrowsArgumentNullException_WhenKeyIsNull()
    {
        using var provider = CreateProvider();
        var cache = provider.GetRequiredService<IRevitTransactionMemoryCache>();

        Assert.Throws<ArgumentNullException>(() => cache.GetOrCreate<int>(null!, () => 1));
    }

    [Fact]
    public void GetOrCreate_ThrowsArgumentNullException_WhenFactoryIsNull()
    {
        using var provider = CreateProvider();
        var cache = provider.GetRequiredService<IRevitTransactionMemoryCache>();

        Assert.Throws<ArgumentNullException>(() => cache.GetOrCreate<int>("key", null!));
    }

    [Fact]
    public void GetOrCreate_ThrowsObjectDisposedException_AfterProviderDisposed()
    {
        var provider = CreateProvider();
        var cache = provider.GetRequiredService<IRevitTransactionMemoryCache>();
        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cache.GetOrCreate("key", () => 1));
    }

    [Fact(Skip = "Требует установленного Revit: подписка на события ControlledApplication/UIControlledApplication " +
                 "заставляет CLR грузить RevitAPI/RevitAPIUI, а они не запускаются вне процесса/установки Revit.")]
    public void Initialize_SubscribesToRevitEvents()
    {
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddRevitContext();
        services.AddTransactionMemoryCache();
        return services.BuildServiceProvider();
    }
}
