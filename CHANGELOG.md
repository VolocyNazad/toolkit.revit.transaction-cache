# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project's versions are `MajorRevitVersion.0.Patch` (see MinVer usage
in `Revit.TransactionMemoryCache.csproj`).

## [Unreleased]

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
  `WhereParameterEquals(BuiltInParameter, int/string/ElementId)` - narrower,
  value-keyable alternatives to the unsupported `WherePasses(ElementFilter)`.
  `WhereParameterEquals` may be called any number of times per chain
  (unlike the other fluent methods), each call narrowing the result further.
  RTMC002 now also flags `OfCategory`/`OfCategories` conflicts.

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

[Unreleased]: https://github.com/VolocyNazad/toolkit.revit.transaction-cache/compare/0.0.7...HEAD
