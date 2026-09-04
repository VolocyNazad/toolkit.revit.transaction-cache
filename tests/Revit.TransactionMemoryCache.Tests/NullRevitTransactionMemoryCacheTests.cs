using Revit.TransactionMemoryCache.Services;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Revit.TransactionMemoryCache.Tests;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit test naming convention: MethodName_Scenario_ExpectedResult.")]
public class NullRevitTransactionMemoryCacheTests
{
    [Fact]
    public void Instance_ReturnsSameInstance_OnRepeatedAccess()
    {
        var first = NullRevitTransactionMemoryCache.Instance;
        var second = NullRevitTransactionMemoryCache.Instance;

        Assert.Same(first, second);
    }

    [Fact]
    public void GetOrCreate_ReturnsFactoryResult_AndDoesNotCache()
    {
        var cache = NullRevitTransactionMemoryCache.Instance;
        var callCount = 0;

        var first = cache.GetOrCreate("key", () => { callCount++; return 42; });
        var second = cache.GetOrCreate("key", () => { callCount++; return 99; });

        Assert.Equal(42, first);
        Assert.Equal(99, second);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public void GetOrCreate_ThrowsArgumentNullException_WhenKeyIsNull()
    {
        var cache = NullRevitTransactionMemoryCache.Instance;

        Assert.Throws<ArgumentNullException>(() => cache.GetOrCreate<int>(null!, () => 1));
    }

    [Fact]
    public void GetOrCreate_ThrowsArgumentNullException_WhenFactoryIsNull()
    {
        var cache = NullRevitTransactionMemoryCache.Instance;

        Assert.Throws<ArgumentNullException>(() => cache.GetOrCreate<int>("key", null!));
    }

    [Fact]
    public void Initialize_DoesNotThrow()
    {
        NullRevitTransactionMemoryCache.Instance.Initialize();
    }

    [Fact]
    public void Deinitialize_DoesNotThrow()
    {
        NullRevitTransactionMemoryCache.Instance.Deinitialize();
    }
}
