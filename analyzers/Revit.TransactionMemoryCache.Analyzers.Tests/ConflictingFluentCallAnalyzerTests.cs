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
    public async Task DuplicateNotOfClass_ReportsRtmc002()
    {
        const string source = """
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.NotOfClass(typeof(object)).NotOfClass(typeof(string));
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == "RTMC002");
    }

    [Fact]
    public async Task NotOfClass_ThenGenericNotOf_ReportsRtmc002()
    {
        const string source = """
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.NotOfClass(typeof(object)).NotOf<string>();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == "RTMC002");
    }

    /// <summary>OfClass and NotOfClass are independent, composable filters - not the same slot.</summary>
    [Fact]
    public async Task OfClass_ThenNotOfClass_DoesNotReport()
    {
        const string source = """
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.OfClass(typeof(object)).NotOfClass(typeof(string));
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "RTMC002");
    }

    [Fact]
    public async Task OfCategory_ThenOfCategories_ReportsRtmc002()
    {
        const string source = """
            using System.Collections.Generic;
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.OfCategory(1).OfCategories(new List<int> { 1, 2 });
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == "RTMC002");
    }

    [Fact]
    public async Task DuplicateOfCategories_ReportsRtmc002()
    {
        const string source = """
            using System.Collections.Generic;
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.OfCategories(new List<int> { 1 }).OfCategories(new List<int> { 2 });
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == "RTMC002");
    }

    /// <summary>Unlike OfClass/OfCategory/Excluding, WhereParameterEquals is intentionally unrestricted.</summary>
    [Fact]
    public async Task MultipleWhereParameterEquals_DoesNotReport()
    {
        const string source = """
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.WhereParameterEquals(1, 1).WhereParameterEquals(2, 2);
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "RTMC002");
    }

    [Fact]
    public async Task DuplicateNotOfCategory_ReportsRtmc002()
    {
        const string source = """
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.NotOfCategory(1).NotOfCategory(2);
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == "RTMC002");
    }

    [Fact]
    public async Task NotOfCategory_ThenNotOfCategories_ReportsRtmc002()
    {
        const string source = """
            using System.Collections.Generic;
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.NotOfCategory(1).NotOfCategories(new List<int> { 2 });
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.Contains(diagnostics, d => d.Id == "RTMC002");
    }

    /// <summary>OfCategory and NotOfCategory are independent, composable filters - not the same slot.</summary>
    [Fact]
    public async Task OfCategory_ThenNotOfCategory_DoesNotReport()
    {
        const string source = """
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.OfCategory(1).NotOfCategory(2);
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "RTMC002");
    }

    /// <summary>Unlike OfClass/OfCategory/Excluding, WhereParameterNotEquals is intentionally unrestricted.</summary>
    [Fact]
    public async Task MultipleWhereParameterNotEquals_DoesNotReport()
    {
        const string source = """
            using Revit.TransactionMemoryCache.Services;

            class C
            {
                void M(CachedElementCollector collector)
                {
                    var chain = collector.WhereParameterNotEquals(1, 1).WhereParameterNotEquals(2, 2);
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(new ConflictingFluentCallAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "RTMC002");
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
