# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project's versions are `MajorRevitVersion.0.Patch` (see MinVer usage
in `Revit.TransactionMemoryCache.csproj`).

## [Unreleased]

## [0.0.8] - 2026-09-06

### Added

- `CachedElementCollector` - a fluent, cached alternative to
  `FilteredElementCollector` (`OfClass`/`Of<T>()`, `OfCategory`,
  `WhereElementIsElementType`/`WhereElementIsNotElementType`, `Excluding`,
  `ToElements`/`ToElementIds`), backed by `IRevitTransactionMemoryCache` and
  built via the new `ICachedElementCollectorFactory`.
- `IRevitTransactionMemoryCacheInitializer.IsInitialized`, so callers (such
  as the collector factory) can guard against caching before automatic
  invalidation has been wired up.
- RTMC001 analyzer (Warning) + code fix: flags casting a cached collector's
  `ToElements()`/`ToElementIds()` result to a mutable collection type, and
  offers to replace the cast with `.ToArray()`/`.ToList()`.
- RTMC002 analyzer (Error): flags a `CachedElementCollector` fluent call
  that conflicts with (duplicates, or is mutually exclusive with) an
  earlier call in the same chain - the same violation that throws
  `InvalidOperationException` at runtime, now caught at compile time.
- `Revit.TransactionMemoryCache.RevitTests` - integration tests for
  `CachedElementCollector` running inside a real Revit process.
- A BenchmarkDotNet suite (`benchmark/`) comparing cached vs. uncached
  queries.
- `CachedElementCollector.OfCategories(IEnumerable<BuiltInCategory>)` and
  `WhereParameterEquals(...)` - narrower, value-keyable alternatives to the
  unsupported `WherePasses(ElementFilter)`. `WhereParameterEquals` may be
  called any number of times per chain (unlike the other fluent methods),
  each call narrowing the result further. RTMC002 now also flags
  `OfCategory`/`OfCategories` conflicts.
- `WhereParameterEquals` overloads for `double` (with an explicit required
  `epsilon`) and for shared/project parameters (`ElementId parameterId`
  instead of `BuiltInParameter`) - covers all four `Parameter.StorageType`
  values and both built-in and non-built-in parameters.
- Filter inversion: `NotOfCategory`/`NotOfCategories` (once-only, a
  separate slot from `OfCategory`/`OfCategories` - composable with them)
  and `WhereParameterNotEquals` (mirrors every `WhereParameterEquals`
  overload, same unrestricted call-any-number-of-times behaviour).
  RTMC002 covers the new `NotOfCategory`/`NotOfCategories` conflict group.
- `NotOfClass`/`NotOf<T>` - same inversion pattern for the class filter
  (`ElementClassFilter` with `inverted: true`), once-only in its own
  slot, composable with `OfClass`/`Of<T>`. RTMC002 covers this conflict
  group too.
- `WhereIsRoom()`/`WhereIsSpace()` - wrap the parameter-less
  `RoomFilter`/`SpaceFilter` quick filters, so they need no equality
  workaround. Once-only each, in independent slots (composable with each
  other, though the combination always matches nothing). RTMC002 covers
  both.
- `WhereBoundingBoxIntersects(XYZ min, XYZ max, double epsilon)` - the
  first supported geometric filter (`BoundingBoxIntersectsFilter`/
  `Outline`). The real Revit-side filter uses the exact coordinates; the
  cache key rounds each coordinate to an explicit required `epsilon`
  first, since `XYZ` has no stable equality to key a cache on - same
  explicit-tolerance approach as the `double` `WhereParameterEquals`
  overloads. Unrestricted, like `WhereParameterEquals`.

### Changed

- Repository reorganized into `src/`, `tests/` and `analyzers/` folders.
- Analyzer package split into `Revit.TransactionMemoryCache.Analyzers` and
  `Revit.TransactionMemoryCache.Analyzers.CodeFixes` (RS1038: a
  `DiagnosticAnalyzer` assembly must not reference
  `Microsoft.CodeAnalysis.Workspaces`).

### Known limitations

- Cache invalidation is global across all open documents, not per-document
  (`IRevitTransactionMemoryCache` is a process-wide singleton).
- The cache key is tied to a specific `Document` reference
  (`RuntimeHelpers.GetHashCode`), not a stable logical document identity -
  `Element.Document` is not always reference-stable against the `Document`
  used to query it. See README for details.

[Unreleased]: https://github.com/VolocyNazad/toolkit.revit.transaction-cache/compare/0.0.8...HEAD
[0.0.8]: https://github.com/VolocyNazad/toolkit.revit.transaction-cache/compare/0.0.7...0.0.8
