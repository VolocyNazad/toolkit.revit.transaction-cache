using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Revit.TransactionMemoryCache.Analyzers.Tests;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "xUnit test naming convention: MethodName_Scenario_ExpectedResult.")]
public class ConflictingFluentCallAnalyzerTests
{
    [Fact]
    public async Task DuplicateOfClass_ReportsRtmc002()
    {
        const string source = """
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.OfClass(typeof(object)).OfClass(typeof(string));
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == "RTMC002");
    }

    [Fact]
    public async Task OfClassThenGenericOf_ReportsRtmc002()
    {
        const string source = """
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.OfClass(typeof(object)).Of<string>();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == "RTMC002");
    }

    [Fact]
    public async Task WhereElementIsElementType_ThenWhereElementIsNotElementType_ReportsRtmc002()
    {
        const string source = """
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.WhereElementIsElementType().WhereElementIsNotElementType();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == "RTMC002");
    }

    [Fact]
    public async Task DuplicateExcluding_ReportsRtmc002()
    {
        const string source = """
            using System.Collections.Generic;
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.Excluding(new List<int>()).Excluding(new List<int>());
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == "RTMC002");
    }

    [Fact]
    public async Task DifferentFilters_DoesNotReport()
    {
        const string source = """
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.OfClass(typeof(object)).WhereElementIsNotElementType();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "RTMC002");
    }

    [Fact]
    public async Task SingleOfClass_DoesNotReport()
    {
        const string source = """
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.OfClass(typeof(object));
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "RTMC002");
    }

    /// <summary>Same method names on an unrelated type must not be flagged - only real CachedElementCollector calls count.</summary>
    [Fact]
    public async Task OfClassOnUnrelatedType_DoesNotReport()
    {
        const string source = """
            class Unrelated
            {
                public Unrelated OfClass(System.Type t) => this;
            }

            class C
            {
                void M(Unrelated collector)
                {
                    var chain = collector.OfClass(typeof(object)).OfClass(typeof(string));
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "RTMC002");
    }
}
