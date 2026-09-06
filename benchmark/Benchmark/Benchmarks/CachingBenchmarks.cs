using Autodesk.Revit.DB;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Nice3point.BenchmarkDotNet.Revit;
using Revit.Context.DI;
using Revit.TransactionMemoryCache.Abstractions.Services;
using Revit.TransactionMemoryCache.DI;

namespace Benchmark.Benchmarks;

/// <summary>
/// Shared setup and benchmark logic for the light/medium/complex query comparisons below: seeds a document
/// with <see cref="WallCount"/> walls and builds <see cref="Cache"/> exactly as production code would
/// (<c>AddRevitContext()</c> + <c>AddTransactionMemoryCache()</c>), resolved as the public
/// <see cref="IRevitTransactionMemoryCache"/> interface. Only
/// <see cref="IRevitTransactionMemoryCache.GetOrCreate{TItem}"/> is exercised - lifecycle
/// (<c>Initialize</c>/document-change invalidation) is out of scope, since it needs a
/// <c>UIControlledApplication</c> this benchmark host doesn't provide.
///
/// <see cref="Uncached"/>/<see cref="Cached"/> simulate <see cref="CallsPerSession"/> consecutive calls
/// within one "transaction" - the way production code would call <c>GetOrCreate</c> several times before
/// the document changes and the cache is invalidated. "Uncached" always pays the full cost
/// <see cref="CallsPerSession"/> times. "Cached" uses a fresh cache key per measured invocation
/// (<see cref="_sessionId"/>), so it always pays for exactly one real miss plus
/// <see cref="CallsPerSession"/> - 1 hits - never an already-warm cache from a previous measurement - which
/// lets the report show how the savings scale with how many times a query is actually reused before
/// invalidation, rather than an unbounded steady-state hit-only ratio.
///
/// BenchmarkDotNet allows only one <c>[Benchmark(Baseline = true)]</c> per class (BDN1107), so each
/// light/medium/complex comparison is its own concrete class overriding <see cref="Query"/>, sharing this
/// base's <see cref="Uncached"/>/<see cref="Cached"/>/<see cref="CallsPerSession"/>.
/// </summary>
public abstract class CachingBenchmarksBase : RevitApiBenchmark
{
    protected const int WallCount = 1000;

    /// <summary>How many times the same query is requested within one simulated transaction.</summary>
    [Params(1, 3, 5, 10)]
    public int CallsPerSession { get; set; }

    protected Document Document { get; private set; } = null!;

    private ServiceProvider _provider = null!;
    protected IRevitTransactionMemoryCache Cache { get; private set; } = null!;

    private int _sessionId;

    protected sealed override void OnGlobalSetup()
    {
        Document = Application.NewProjectDocument(UnitSystem.Metric);

        using var transaction = new Transaction(Document, "Seed model");
        transaction.Start();

        var level = Level.Create(Document, 0);
        for (var i = 0; i < WallCount; i++)
        {
            Wall.Create(Document, Line.CreateBound(new XYZ(i, 0, 0), new XYZ(i + 1, 0, 0)), level.Id, false);
        }

        transaction.Commit();

        var services = new ServiceCollection();
        services.AddRevitContext();
        services.AddTransactionMemoryCache();

        _provider = services.BuildServiceProvider();
        Cache = _provider.GetRequiredService<IRevitTransactionMemoryCache>();
    }

    protected sealed override void OnGlobalCleanup()
    {
        _provider.Dispose();
        Document.Close(false);
    }

    [Benchmark(Baseline = true)]
    public IReadOnlyList<Element> Uncached()
    {
        IReadOnlyList<Element> result = Array.Empty<Element>();
        for (var i = 0; i < CallsPerSession; i++)
        {
            result = Query();
        }

        return result;
    }

    [Benchmark]
    public IReadOnlyList<Element> Cached()
    {
        var key = $"{GetType().Name}-{_sessionId++}";

        IReadOnlyList<Element> result = Array.Empty<Element>();
        for (var i = 0; i < CallsPerSession; i++)
        {
            result = Cache.GetOrCreate(key, Query)!;
        }

        return result;
    }

    /// <summary>The actual Revit DB query being compared cached vs. uncached. Runs once per call in the loop
    /// above - <see cref="CallsPerSession"/> times for <see cref="Uncached"/>, but for <see cref="Cached"/>
    /// only on the first call per measured invocation (the rest are served from <see cref="Cache"/>).</summary>
    protected abstract IReadOnlyList<Element> Query();
}

/// <summary>
/// Light query: a single, cheap <c>OfClass</c> filter against a handful of elements (the one seeded Level).
/// See <see cref="CachingBenchmarksBase"/> for what "Uncached"/"Cached"/<see cref="CachingBenchmarksBase.CallsPerSession"/> compare.
/// </summary>
public class LightQueryCachingBenchmarks : CachingBenchmarksBase
{
    protected override IReadOnlyList<Element> Query() =>
        new FilteredElementCollector(Document)
            .OfClass(typeof(Level))
            .ToElements()
            .ToList();
}

/// <summary>
/// Medium query: every wall in the model, class + non-type filtering only. See
/// <see cref="CachingBenchmarksBase"/> for what "Uncached"/"Cached"/<see cref="CachingBenchmarksBase.CallsPerSession"/> compare.
/// </summary>
public class MediumQueryCachingBenchmarks : CachingBenchmarksBase
{
    protected override IReadOnlyList<Element> Query() =>
        new FilteredElementCollector(Document)
            .OfClass(typeof(Wall))
            .WhereElementIsNotElementType()
            .ToElements()
            .ToList();
}

/// <summary>
/// Complex query: class + non-type + a bounding-box filter, then managed-side parameter access and
/// ordering - forces every wall's geometry/parameters to be touched, not just its element table row.
/// See <see cref="CachingBenchmarksBase"/> for what "Uncached"/"Cached"/<see cref="CachingBenchmarksBase.CallsPerSession"/> compare.
/// </summary>
public class ComplexQueryCachingBenchmarks : CachingBenchmarksBase
{
    protected override IReadOnlyList<Element> Query()
    {
        var outline = new Outline(new XYZ(-10, -10, -10), new XYZ(WallCount + 10, 10, 10));
        var boundingBoxFilter = new BoundingBoxIntersectsFilter(outline);

        return new FilteredElementCollector(Document)
            .OfClass(typeof(Wall))
            .WhereElementIsNotElementType()
            .WherePasses(boundingBoxFilter)
            .Cast<Wall>()
            .Where(wall => wall.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() > 0)
            .OrderBy(wall => wall.Id.Value)
            .ToList();
    }
}
