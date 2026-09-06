using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Revit.TransactionMemoryCache.Analyzers.Tests;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit test naming convention: MethodName_Scenario_ExpectedResult.")]
public class MutableCastCodeFixProviderTests
{
    [Fact]
    public async Task DirectCastToList_IsReplacedWithToList_AndAddsSystemLinqUsing()
    {
        const string source = """
            using System.Collections.Generic;
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var list = (List<object>)collector.ToElements();
                }
            }
            """;

        var fixedSource = await CodeFixTestHelper.ApplyFixAsync(new MutableCastAnalyzer(), new MutableCastCodeFixProvider(), source);

        Assert.Contains("var list = collector.ToElements().ToList();", fixedSource, StringComparison.Ordinal);
        Assert.Contains("using System.Linq;", fixedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("(List<object>)", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsCastToArray_IsReplacedWithToArray()
    {
        const string source = """
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var ids = collector.ToElementIds() as int[];
                }
            }
            """;

        var fixedSource = await CodeFixTestHelper.ApplyFixAsync(new MutableCastAnalyzer(), new MutableCastCodeFixProvider(), source);

        Assert.Contains("var ids = collector.ToElementIds().ToArray();", fixedSource, StringComparison.Ordinal);
        Assert.Contains("using System.Linq;", fixedSource, StringComparison.Ordinal);
        Assert.DoesNotContain(" as int[]", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingSystemLinqUsing_IsNotDuplicated()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var list = (List<object>)collector.ToElements();
                }
            }
            """;

        var fixedSource = await CodeFixTestHelper.ApplyFixAsync(new MutableCastAnalyzer(), new MutableCastCodeFixProvider(), source);

        var usingCount = fixedSource.Split("using System.Linq;").Length - 1;
        Assert.Equal(1, usingCount);
    }
}
