# Revit.Transaction-Cache

[![Revit 2021.1.9, 2023, 2025](https://img.shields.io/badge/Revit-2021.1.9%20%7C%202023%20%7C%202025-green.svg)](https://autodesk.com/revit)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![VolocyNazad](https://img.shields.io/badge/VolocyNazad-blue.svg)](https://github.com/VolocyNazad)

> An in-memory value cache bound to the lifecycle of a Revit document.

Revit.TransactionMemoryCache — a memoization service for expensive Revit API computations, with automatic cache invalidation on document changes or view switches, and support for DI containerization.

## Features

- `IRevitTransactionMemoryCache` — `GetOrCreate<TItem>(object key, Func<TItem> factory)`: returns the cached value for a key, or computes it via `factory` and caches it.
- `IRevitTransactionMemoryCacheInitializer` — `Initialize()`/`Deinitialize()`: subscribes/unsubscribes the cache to the `DocumentChanged` and `ViewActivated` events, automatically clearing all cached values when they fire.
- `RevitTransactionMemoryCache` — the single implementation of both interfaces on top of `IMemoryCache` and [`IRevitContext`](https://github.com/VolocyNazad/toolkit.revit.context).
- One-line DI registration via `AddTransactionMemoryCache()`.
- Thread-safe lifecycle (initialization, reset, `Dispose`).
- `CachedElementCollector` — a fluent wrapper over `FilteredElementCollector` with automatic result caching (see the section below).
- Two Roslyn analyzers (`RTMC001`/`RTMC002`), wired in automatically with the package.

## Installation

```
dotnet add package VolocyNazad.Revit.TransactionMemoryCache
```

## Usage

Registering services in the DI container (also requires `VolocyNazad.Revit.Context`):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Revit.Context.DI;
using Revit.TransactionMemoryCache.DI;

services.AddRevitContext();
services.AddTransactionMemoryCache();
```

Initialization in `IExternalApplication.OnStartup` (after the context has been initialized):

```csharp
using Autodesk.Revit.UI;
using Revit.Context.Abstractions.Services;
using Revit.TransactionMemoryCache.Abstractions.Services;

public Result OnStartup(UIControlledApplication application)
{
    serviceProvider.GetRequiredService<IRevitContextInitializer>().Initialize(application);
    serviceProvider.GetRequiredService<IRevitTransactionMemoryCacheInitializer>().Initialize();

    return Result.Succeeded;
}
```

Using the cache in any service:

```csharp
using Revit.TransactionMemoryCache.Abstractions.Services;

public sealed class MyService(IRevitTransactionMemoryCache cache)
{
    public IList<Wall> GetWalls(Document doc) =>
        cache.GetOrCreate("walls", () => new FilteredElementCollector(doc)
            .OfClass(typeof(Wall))
            .Cast<Wall>()
            .ToList())!;
}
```

The cache is automatically reset when the document changes (`DocumentChanged`) and when the active view is switched (`ViewActivated`), so calling `GetOrCreate` again with the same key after these events recomputes the value.

## CachedElementCollector

A fluent wrapper over `FilteredElementCollector`, but with automatic result caching in `IRevitTransactionMemoryCache`. Nothing touches the Revit API until the terminal call (`ToElements()`/`ToElementIds()`) — up to that point, the call chain only accumulates the fragments of the future cache key.

```csharp
using Revit.TransactionMemoryCache.Abstractions.Services;

public sealed class MyService(ICachedElementCollectorFactory collectorFactory)
{
    public IReadOnlyList<Wall> GetWalls(Document doc) =>
        collectorFactory.Create(doc)
            .OfClass(typeof(Wall)) // or .Of<Wall>()
            .WhereElementIsNotElementType()
            .ToElements()
            .Cast<Wall>()
            .ToList();
}
```

`ICachedElementCollectorFactory` is resolved via DI (registered together with `AddTransactionMemoryCache()`) and throws `InvalidOperationException` if `IRevitTransactionMemoryCacheInitializer.Initialize()` has not been called yet — a cache without automatic invalidation would silently serve stale data after a document change.

**Key rules:**

- `OfClass`/`Of<T>`, `OfCategory`, `Excluding` may each be called **at most once** per chain; `WhereElementIsElementType`/`WhereElementIsNotElementType` are mutually exclusive. Violating this throws `InvalidOperationException` immediately, without waiting for a cache miss. The compile-time counterpart of this check is the `RTMC002` (Error) analyzer.
- `NotOfClass`/`NotOf<T>` — the inverted version of `OfClass`/`Of<T>` (`ElementClassFilter` with `inverted: true`): elements that **do not** belong to the class. Once-only among themselves (their own "slot", separate from `OfClass`/`Of<T>`), but **can be combined** with `OfClass`/`Of<T>` in the same chain — independent quick filters, just like with categories.
- The order of fluent calls **does not affect** the cache key — fragments are canonicalized (sorted) before being combined, so `OfClass(...).Excluding(...)` and `Excluding(...).OfClass(...)` produce the same key.
- The result of `ToElements()`/`ToElementIds()` is **shared** across all callers with an equivalent chain. Do not cast it to a mutable type (`List<T>`, an array) or mutate it — that would silently corrupt the cached value for everyone else. The `RTMC001` (Warning) analyzer catches such a cast at the call site and offers a code fix — replace it with `.ToList()`/`.ToArray()` (which creates a copy).
- `WherePasses(ElementFilter)` is **deliberately unsupported** — most `ElementFilter` subclasses lack reliable value equality, which is needed to build a deterministic cache key from them. Instead, there are narrow fluent wrappers for specific value-typed parameters:
  - `OfCategories(IEnumerable<BuiltInCategory>)` — the counterpart to `OfCategory`, but for multiple categories at once (`ElementMulticategoryFilter`). Like `OfCategory`, it can be called at most once per chain, and conflicts with `OfCategory` (they share the same category "slot").
  - `NotOfCategory(BuiltInCategory)` / `NotOfCategories(IEnumerable<BuiltInCategory>)` — the inverted versions (`ElementCategoryFilter`/`ElementMulticategoryFilter` with `inverted: true`): elements that **do not** belong to the category/categories. Once-only among themselves (their own "slot", separate from `OfCategory`/`OfCategories`), but **can be combined** with `OfCategory`/`OfCategories` in the same chain — these are independent quick filters, not alternative ways of expressing the same thing.
  - `WhereParameterEquals(BuiltInParameter, ...)` / `WhereParameterEquals(ElementId parameterId, ...)` — a filter on a parameter value (`ElementParameterFilter`/`ParameterFilterRuleFactory.CreateEqualsRule`). Unlike the other fluent methods, this one **can be called any number of times** per chain — each call narrows the result (similar to chaining several `WherePasses` calls on `FilteredElementCollector`). It covers all 4 `Parameter.StorageType` values:
    - `int`, `string` (case-insensitive), `ElementId` — no assumptions needed;
    - `double` — only with an explicit `epsilon` (`WhereParameterEquals(parameter, value, epsilon)`), since exact `double` comparison is almost never what you want, and a sensible tolerance depends on the parameter's unit of measure (length/area/angle).
    - The overload taking `ElementId parameterId` instead of `BuiltInParameter` is for shared/project parameters that have no `BuiltInParameter` (e.g. `SharedParameterElement.Id`).
  - `WhereParameterNotEquals(...)` — the same overloads as `WhereParameterEquals`, but inverted (`ElementParameterFilter` with `inverted: true`). Also not limited in the number of calls.
  - `WhereIsRoom()` / `WhereIsSpace()` — quick filters `RoomFilter`/`SpaceFilter` (no parameters, so no value-equality concerns either). Once-only each, but independent "slots" — both can be combined in the same chain (the result will always be empty, but no exception is thrown).
  - `WhereBoundingBoxIntersects(XYZ min, XYZ max, double epsilon)` — the only supported geometric filter (`BoundingBoxIntersectsFilter`/`Outline`). The filter itself is built on the Revit side from exact coordinates, but each coordinate is rounded to `epsilon` for the cache key — otherwise `XYZ` provides no stable equality for a key. `epsilon` is required explicitly (for the same reason as the `double` overloads of `WhereParameterEquals`). Not limited in the number of calls.

## Known limitations

- **Invalidation is global across all open documents, not per document.** `IRevitTransactionMemoryCache` is the single singleton instance per add-in process (registered via `AddTransactionMemoryCache()`); isolation between documents is achieved not through separate cache instances, but because the document identifier is baked into the key itself (`RuntimeHelpers.GetHashCode(document)` inside `CachedElementCollectorKeyBuilder`/arbitrary keys via `GetOrCreate`). But `RevitTransactionMemoryCache.Refresh()` resets a **single shared** `CancellationTokenSource` that entries for *all* documents are subscribed to — on `DocumentChanged`/`ViewActivated` in one document, the cache for **all** currently open documents is cleared entirely, not just the one that changed.
  - This does not cause stale data (it's over-invalidation, not under-invalidation), but it reduces cache effectiveness when working with several open documents at once.
  - Planned for the future: make invalidation per document — for example, partitioning the `CancellationTokenSource` by the document identifier from `DocumentChangedEventArgs`/the active `Document`, instead of holding one shared token for all entries.

- **The `CachedElementCollector` key is tied to a specific `Document` reference, not to the "logical" document.** The cache key uses `RuntimeHelpers.GetHashCode(document)` — the identity of the managed wrapper object, not of the document as such. Empirically confirmed (RevitTests): `Element.Document` can return a **different** wrapper instance than the one passed into `FilteredElementCollector`/`ICachedElementCollectorFactory.Create(document)`. Practical consequence: if you obtain the `Document` for the same open document through two different paths (e.g. once via `UIDocument.Document`, once via `element.Document`) and pass both into `Create(...)`, the cache is not reused between them — not data corruption, just a missed cache hit. Recommendation until this is fixed: always pass the **same** `Document` reference into `Create(document)` (e.g. one obtained once at the start of the command), rather than re-fetching it from different places in the API.
  - Planned for the future: replace object identity with something stable at the logical-document level (e.g. `Document.PathName`/`Document.Title` combined with a "workshared/unsaved" flag, if such a key turns out to be more reliable than reference identity).

## Supported Revit versions

The package is built and tested for Revit 2021.1.9, 2023.0.0, and 2025.0.0. Revit 2021 and 2023 target net48; Revit 2025 targets net8.0-windows.

## Requirements

- .NET SDK 10.0.103+ (see `global.json`)
- Revit API (the `Revit_All_Main_Versions_API_x64` package)
- `VolocyNazad.Revit.Context`

## Benchmarks

A performance comparison of `FilteredElementCollector` queries against the Revit database with and without caching via
`IRevitTransactionMemoryCache` — see `benchmark/`. Run manually inside a live
Revit session (`Nice3point.BenchmarkDotNet.Revit`), so it does not run in CI.

`Light`/`Medium`/`Complex` are three complexity levels of a `FilteredElementCollector` query. Each is run with the `CallsPerSession` parameter (1/5/20/100) — how many times the same query is requested in a row within a single "transaction", before the document changes and the cache is invalidated. `Uncached` always pays the full price `CallsPerSession` times; `Cached` uses a fresh key for each measurement, so each time it pays for exactly one real miss plus `CallsPerSession − 1` hits — this shows how the savings grow with the number of repeated accesses, not just the marginal cost of a single already-warm hit.

### Setup

**Model and data**
- One new project document (`Application.NewProjectDocument(UnitSystem.Metric)`), created fresh for each class/parameter combination.
- **1000 walls** (`WallCount`), lined up on a single seeded level, in a single transaction (`OnGlobalSetup` in `CachingBenchmarksBase`).

**What each complexity level actually queries**

| Level | Query |
|---|---|
| Light | `OfClass(typeof(Level))` — a single cheap class filter, the one seeded level. |
| Medium | `OfClass(typeof(Wall)).WhereElementIsNotElementType()` — all 1000 walls, class and non-type filtering only. |
| Complex | The same, plus `BoundingBoxIntersectsFilter`, then in managed code: reading the `CURVE_ELEM_LENGTH` parameter of each wall and sorting by `Id` — touches the geometry/parameters of each element, not just its row in the element table. |

**Session parameter**
- `CallsPerSession`: **1 / 3 / 5 / 10** — how many times the same result is requested in a row within one "transaction" before the cache is invalidated (see the previous paragraph). Each value is a separate row in the report (BenchmarkDotNet `[Params]`).

**Cache behavior**
- The cache is the real `RevitTransactionMemoryCache` from `src/`, wired up via `AddRevitContext()` + `AddTransactionMemoryCache()` — the same path as in production.
- `SlidingExpiration = 10 minutes` per entry (see `RevitTransactionMemoryCache.GetOrCreate`) — the whole run takes seconds, so within a single measurement an entry never expires on its own; the only miss is the one we deliberately create with a fresh key for each measurement.
- Invalidation via `DocumentChanged`/`ViewActivated` does not participate in the benchmark — `Initialize()` is not called, since it requires a `UIControlledApplication`, which the benchmark host does not have.

**Measurement configuration (BenchmarkDotNet)**
- `Job.Default` — the number of invocations per iteration (`InvocationCount`), the number of warm-up/measurement iterations, etc. are not set manually; the engine calibrates them itself (see the note about the Pilot stage above).
- `MemoryDiagnoser.Default` — enables the `Allocated`/`Alloc Ratio` column.
- Exporters: CSV (`-report.csv`), detailed measurements (`-measurements.csv`), JSON, GitHub markdown (the one that ends up here).
- Target — the `Release_2025.0.0` configuration (`net8.0-windows`, x64 platform); the host machine/OS/.NET SDK version/BenchmarkDotNet version are recorded automatically in the header of each report below.

<!-- benchmark-results:start -->
_Updated: 2026-09-05 14:21 (local benchmark run)._

### Light

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
Intel Core Ultra 7 155H 3.80GHz, 1 CPU, 22 logical and 16 physical cores
.NET SDK 10.0.400
  [Host]     : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3 [AttachedDebugger]
  Job-BNKTQO : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3

BuildConfiguration=Release_2025.0.0  

```
| Method   | CallsPerSession | Mean       | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |---------------- |-----------:|---------:|---------:|------:|--------:|----------:|------------:|
| **Uncached** | **1**               |   **363.2 μs** |  **5.03 μs** |  **4.70 μs** |  **1.00** |    **0.02** |     **656 B** |        **1.00** |
| Cached   | 1               |   404.2 μs |  7.84 μs |  6.95 μs |  1.11 |    0.02 |    1360 B |        2.07 |
|          |                 |            |          |          |       |         |           |             |
| **Uncached** | **3**               | **1,227.5 μs** | **23.62 μs** | **22.10 μs** |  **1.00** |    **0.02** |    **1968 B** |        **1.00** |
| Cached   | 3               |   470.2 μs |  9.20 μs | 16.12 μs |  0.38 |    0.01 |    1680 B |        0.85 |
|          |                 |            |          |          |       |         |           |             |
| **Uncached** | **5**               | **2,063.4 μs** | **40.27 μs** | **62.70 μs** |  **1.00** |    **0.04** |    **3280 B** |        **1.00** |
| Cached   | 5               |   439.0 μs |  8.46 μs | 11.30 μs |  0.21 |    0.01 |    2000 B |        0.61 |
|          |                 |            |          |          |       |         |           |             |
| **Uncached** | **10**              | **4,305.8 μs** | **75.61 μs** | **84.04 μs** |  **1.00** |    **0.03** |    **6560 B** |        **1.00** |
| Cached   | 10              |   442.0 μs |  8.47 μs |  9.42 μs |  0.10 |    0.00 |    2800 B |        0.43 |

### Medium

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
Intel Core Ultra 7 155H 3.80GHz, 1 CPU, 22 logical and 16 physical cores
.NET SDK 10.0.400
  [Host]     : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3 [AttachedDebugger]
  Job-BNKTQO : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3

BuildConfiguration=Release_2025.0.0  

```
| Method   | CallsPerSession | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Gen0    | Gen1    | Allocated | Alloc Ratio |
|--------- |---------------- |----------:|----------:|----------:|----------:|------:|--------:|--------:|--------:|----------:|------------:|
| **Uncached** | **1**               |  **1.424 ms** | **0.0484 ms** | **0.1411 ms** |  **1.410 ms** |  **1.01** |    **0.14** |  **7.8125** |  **5.8594** |    **102 KB** |        **1.00** |
| Cached   | 1               |  1.355 ms | 0.0404 ms | 0.1145 ms |  1.324 ms |  0.96 |    0.12 |  7.8125 |  5.8594 | 102.61 KB |        1.01 |
|          |                 |           |           |           |           |       |         |         |         |           |             |
| **Uncached** | **3**               |  **4.090 ms** | **0.0944 ms** | **0.2740 ms** |  **3.999 ms** |  **1.00** |    **0.09** | **23.4375** | **15.6250** |    **306 KB** |        **1.00** |
| Cached   | 3               |  1.268 ms | 0.0333 ms | 0.0945 ms |  1.225 ms |  0.31 |    0.03 |  7.8125 |  5.8594 | 102.92 KB |        0.34 |
|          |                 |           |           |           |           |       |         |         |         |           |             |
| **Uncached** | **5**               |  **7.403 ms** | **0.0840 ms** | **0.0786 ms** |  **7.416 ms** |  **1.00** |    **0.01** | **39.0625** | **31.2500** |    **510 KB** |        **1.00** |
| Cached   | 5               |  1.383 ms | 0.0753 ms | 0.2209 ms |  1.429 ms |  0.19 |    0.03 |  7.8125 |  5.8594 | 103.23 KB |        0.20 |
|          |                 |           |           |           |           |       |         |         |         |           |             |
| **Uncached** | **10**              | **11.652 ms** | **0.2294 ms** | **0.2146 ms** | **11.588 ms** |  **1.00** |    **0.03** | **78.1250** | **62.5000** |   **1020 KB** |        **1.00** |
| Cached   | 10              |  1.221 ms | 0.0350 ms | 0.1004 ms |  1.218 ms |  0.10 |    0.01 |  7.8125 |  5.8594 | 104.02 KB |        0.10 |

### Complex

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
Intel Core Ultra 7 155H 3.80GHz, 1 CPU, 22 logical and 16 physical cores
.NET SDK 10.0.400
  [Host]     : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3 [AttachedDebugger]
  Job-BNKTQO : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3

BuildConfiguration=Release_2025.0.0  

```
| Method   | CallsPerSession | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Gen0     | Gen1     | Allocated  | Alloc Ratio |
|--------- |---------------- |----------:|----------:|----------:|----------:|------:|--------:|---------:|---------:|-----------:|------------:|
| **Uncached** | **1**               |  **3.751 ms** | **0.0743 ms** | **0.0991 ms** |  **3.742 ms** |  **1.00** |    **0.04** |  **19.5313** |  **15.6250** |  **255.73 KB** |        **1.00** |
| Cached   | 1               |  4.533 ms | 0.1995 ms | 0.5725 ms |  4.319 ms |  1.21 |    0.16 |  19.5313 |  15.6250 |  256.42 KB |        1.00 |
|          |                 |           |           |           |           |       |         |          |          |            |             |
| **Uncached** | **3**               | **10.152 ms** | **0.1761 ms** | **0.2028 ms** | **10.177 ms** |  **1.00** |    **0.03** |  **62.5000** |  **46.8750** |   **767.2 KB** |        **1.00** |
| Cached   | 3               |  3.662 ms | 0.0716 ms | 0.1327 ms |  3.629 ms |  0.36 |    0.01 |  19.5313 |  15.6250 |  256.73 KB |        0.33 |
|          |                 |           |           |           |           |       |         |          |          |            |             |
| **Uncached** | **5**               | **16.508 ms** | **0.2091 ms** | **0.1854 ms** | **16.477 ms** |  **1.00** |    **0.02** |  **93.7500** |  **62.5000** | **1278.67 KB** |        **1.00** |
| Cached   | 5               |  3.570 ms | 0.0709 ms | 0.0897 ms |  3.557 ms |  0.22 |    0.01 |  19.5313 |  15.6250 |  257.05 KB |        0.20 |
|          |                 |           |           |           |           |       |         |          |          |            |             |
| **Uncached** | **10**              | **33.797 ms** | **0.4736 ms** | **0.4198 ms** | **33.817 ms** |  **1.00** |    **0.02** | **200.0000** | **133.3333** | **2557.34 KB** |        **1.00** |
| Cached   | 10              |  3.601 ms | 0.0677 ms | 0.0725 ms |  3.613 ms |  0.11 |    0.00 |  19.5313 |  15.6250 |  257.83 KB |        0.10 |

<!-- benchmark-results:end -->

## License

MIT, see [LICENSE.md](LICENSE.md).

## Development documentation

- [Development policy](docs/policies/development.md)
- [Repository guide and technology stack](docs/repository.md)

## Contributing
 [CONTRIBUTING.md](CONTRIBUTING.md) before submitting changes.
