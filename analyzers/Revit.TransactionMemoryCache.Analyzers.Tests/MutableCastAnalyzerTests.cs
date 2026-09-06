using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Revit.TransactionMemoryCache.Analyzers.Tests;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit test naming convention: MethodName_Scenario_ExpectedResult.")]
public class MutableCastAnalyzerTests
{
    [Fact]
    public async Task DirectCastOfToElementsToList_ReportsRtmc001()
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

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new MutableCastAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == "RTMC001");
    }

    [Fact]
    public async Task AsCastOfToElementIdsToList_ReportsRtmc001()
    {
        const string source = """
            using System.Collections.Generic;
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var list = collector.ToElementIds() as List<int>;
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new MutableCastAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == "RTMC001");
    }

    [Fact]
    public async Task CastToReadOnlyInterface_DoesNotReport()
    {
        const string source = """
            using System.Collections.Generic;
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var list = (IReadOnlyList<object>)collector.ToElements();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new MutableCastAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "RTMC001");
    }

    [Fact]
    public async Task CastOfUnrelatedMethodResult_DoesNotReport()
    {
        const string source = """
            using System.Collections.Generic;

            class C
            {
                object GetElements() => null!;

                void M()
                {
                    var list = (List<object>)GetElements();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new MutableCastAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "RTMC001");
    }

    /// <summary>Documents a known v1 limitation: a cast applied via an intermediate variable is not tracked.</summary>
    [Fact]
    public async Task CastViaIntermediateVariable_IsNotDetected()
    {
        const string source = """
            using System.Collections.Generic;
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var elements = collector.ToElements();
                    var list = (List<object>)elements;
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new MutableCastAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "RTMC001");
    }
}
