using Revit.TransactionMemoryCache.Services;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Revit.TransactionMemoryCache.Tests;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit test naming convention: MethodName_Scenario_ExpectedResult.")]
public class CachedElementCollectorKeyBuilderTests
{
    [Fact]
    public void Build_ProducesSameKey_RegardlessOfFragmentOrder()
    {
        var keyA = CachedElementCollectorKeyBuilder.Build(1, "ToElements", ["OfClass:Wall", "WhereElementIsNotElementType"]);
        var keyB = CachedElementCollectorKeyBuilder.Build(1, "ToElements", ["WhereElementIsNotElementType", "OfClass:Wall"]);

        Assert.Equal(keyA, keyB);
    }

    [Fact]
    public void Build_ProducesDifferentKeys_ForDifferentFragments()
    {
        var keyA = CachedElementCollectorKeyBuilder.Build(1, "ToElements", ["OfClass:Wall"]);
        var keyB = CachedElementCollectorKeyBuilder.Build(1, "ToElements", ["OfClass:Floor"]);

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void Build_ProducesDifferentKeys_ForDifferentDocumentIdentity()
    {
        var keyA = CachedElementCollectorKeyBuilder.Build(1, "ToElements", ["OfClass:Wall"]);
        var keyB = CachedElementCollectorKeyBuilder.Build(2, "ToElements", ["OfClass:Wall"]);

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void Build_ProducesDifferentKeys_ForDifferentTerminal()
    {
        var keyA = CachedElementCollectorKeyBuilder.Build(1, "ToElements", ["OfClass:Wall"]);
        var keyB = CachedElementCollectorKeyBuilder.Build(1, "ToElementIds", ["OfClass:Wall"]);

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void Build_ProducesSameKey_ForEmptyFragments_WhenDocumentAndTerminalMatch()
    {
        var keyA = CachedElementCollectorKeyBuilder.Build(1, "ToElements", []);
        var keyB = CachedElementCollectorKeyBuilder.Build(1, "ToElements", []);

        Assert.Equal(keyA, keyB);
    }

    [Fact]
    public void Build_ThrowsArgumentNullException_WhenTerminalIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => CachedElementCollectorKeyBuilder.Build(1, null!, []));
    }

    [Fact]
    public void Build_ThrowsArgumentNullException_WhenFragmentsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => CachedElementCollectorKeyBuilder.Build(1, "ToElements", null!));
    }
}
