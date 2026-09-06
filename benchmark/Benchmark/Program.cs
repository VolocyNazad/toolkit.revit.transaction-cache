using System.Globalization;
using System.Text;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using Nice3point.BenchmarkDotNet.Revit;
using BenchmarkDotNet.Running;
using Benchmark.Benchmarks;

var benchmarkDirectory = FindBenchmarkDirectory();
var artifactsPath = Path.Combine(benchmarkDirectory, "BenchmarkDotNet.Artifacts");

var configuration = ManualConfig.Create(DefaultConfig.Instance)
    .WithArtifactsPath(artifactsPath)
    .AddJob(Job.Default.WithCurrentConfiguration())
    .AddDiagnoser(MemoryDiagnoser.Default)
    .AddExporter(CsvExporter.Default)
    .AddExporter(CsvMeasurementsExporter.Default)
    .AddExporter(JsonExporter.Default)
    .AddExporter(MarkdownExporter.GitHub);

BenchmarkRunner.Run<LightQueryCachingBenchmarks>(configuration);
BenchmarkRunner.Run<MediumQueryCachingBenchmarks>(configuration);
BenchmarkRunner.Run<ComplexQueryCachingBenchmarks>(configuration);

// Opt-in: pass --update-readme to splice the freshly generated GitHub-flavoured markdown reports
// into README.md between the <!-- benchmark-results:start/end --> markers, so ad-hoc/debug runs
// don't dirty the README on every invocation.
if (args.Contains("--update-readme", StringComparer.OrdinalIgnoreCase))
{
    UpdateReadme(benchmarkDirectory, artifactsPath);
}

static string FindBenchmarkDirectory()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Benchmark.csproj")))
            return directory.Parent?.FullName
                   ?? throw new DirectoryNotFoundException("benchmark directory was not found.");

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Benchmark.csproj was not found.");
}

static void UpdateReadme(string benchmarkDirectory, string artifactsPath)
{
    var repositoryRoot = Directory.GetParent(benchmarkDirectory)?.FullName;
    if (repositoryRoot is null)
    {
        Console.WriteLine("README update skipped: repository root was not found.");
        return;
    }

    var readmePath = Path.Combine(repositoryRoot, "README.md");
    if (!File.Exists(readmePath))
    {
        Console.WriteLine($"README update skipped: {readmePath} was not found.");
        return;
    }

    var resultsDirectory = Path.Combine(artifactsPath, "results");
    if (!Directory.Exists(resultsDirectory))
    {
        Console.WriteLine($"README update skipped: {resultsDirectory} was not found.");
        return;
    }

    (string Title, string SearchPattern)[] sections =
    [
        ("Light", "*LightQueryCachingBenchmarks-report-github.md"),
        ("Medium", "*MediumQueryCachingBenchmarks-report-github.md"),
        ("Complex", "*ComplexQueryCachingBenchmarks-report-github.md")
    ];

    var block = new StringBuilder();
    block.Append(CultureInfo.InvariantCulture, $"_Обновлено: {DateTime.Now:yyyy-MM-dd HH:mm} (локальный запуск бенчмарков)._");
    block.AppendLine();
    block.AppendLine();

    foreach (var (title, searchPattern) in sections)
    {
        var reportFile = Directory.GetFiles(resultsDirectory, searchPattern)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (reportFile is null)
            continue;

        block.Append(CultureInfo.InvariantCulture, $"### {title}");
        block.AppendLine();
        block.AppendLine();
        block.AppendLine(File.ReadAllText(reportFile).Trim());
        block.AppendLine();
    }

    const string startMarker = "<!-- benchmark-results:start -->";
    const string endMarker = "<!-- benchmark-results:end -->";

    var readme = File.ReadAllText(readmePath);
    var startIndex = readme.IndexOf(startMarker, StringComparison.Ordinal);
    var endIndex = readme.IndexOf(endMarker, StringComparison.Ordinal);

    if (startIndex < 0 || endIndex < 0 || endIndex < startIndex)
    {
        Console.WriteLine("README update skipped: markers not found in README.md.");
        return;
    }

    var updatedReadme = string.Concat(
        readme.AsSpan(0, startIndex + startMarker.Length),
        Environment.NewLine,
        block.ToString(),
        readme.AsSpan(endIndex));

    File.WriteAllText(readmePath, updatedReadme);
    Console.WriteLine($"README.md updated with the latest benchmark results ({readmePath}).");
}
