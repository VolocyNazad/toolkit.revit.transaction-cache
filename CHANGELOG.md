# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project's versions are `MajorRevitVersion.0.Patch` (see MinVer usage
in `Directory.Packages.props`).

## [Unreleased]

### Added

- Branching policy: `main` holds the released state, `dev` integrates the next release, and changes arrive through feature, fix, release and hotfix branches.

### Changed

- Moved package versions to central package management (`Directory.Packages.props` and `tests/Directory.Packages.props`); Revit API, sibling package, and benchmark tooling versions follow the selected build configuration.
- Benchmarks now use the supported `Release_2025.4.60` configuration and the centrally managed `Microsoft.Extensions.DependencyInjection` version.

## [0.0.10] - 2026-10-15

### Added

- Support Revit 2021-2027: 2021.1.100, 2022.1.80, 2023.1.90, 2024.3.60, 2025.4.60, 2026.4.10, 2027.3.0 (SDK 1.1.0; 2021-2024 net48, 2025-2026 net8.0-windows, 2027 net10.0-windows).

### Changed

- Reference the Revit API through `Nice3point.Revit.Api.RevitAPI` / `Nice3point.Revit.Api.RevitAPIUI` (floating `$(MajorRevitVersion).*`) instead of `Revit_All_Main_Versions_API_x64`, so each major resolves to its latest stable API patch.
- Require `VolocyNazad.Revit.Context` `$(MajorRevitVersion).0.10` (was `$(MajorRevitVersion).0.9`).

### Fixed

- Build string-parameter filters with the 2-arg `CreateEqualsRule` overload on Revit 2026+, where Autodesk removed the `caseSensitive` overload.

## [0.0.9] - 2026-09-21

### Added

- Validate required repository files, navigation links, and Solution Items in CI.

### Fixed

- Correct the repository guide to reflect that central package management is not used.

- Include the build configuration in CI artifact names to prevent collisions between Revit matrix jobs.

### Changed

- Standardize GitHub Actions workflow filenames and display names by responsibility.

- Clarify package version management and remove redundant central package configuration.

- Set the Roslyn compatibility baseline to Microsoft.CodeAnalysis 4.14 for Visual Studio 2022 version 17.14.

- Limit the declared Revit support matrix to 2021.1.9, 2023.0.0, and 2025.0.0.

- Configure xUnit v3 test execution through Microsoft.Testing.Platform and fail test runs when no tests are discovered.

- Establish a shared EditorConfig baseline and use the repository-policy validator as the single structural CI check.

- Complete solution items for repository documents, configuration, workflows and maintenance scripts; document the shared layout.

- Standardize local and CI SDK selection on stable .NET 10.0 through global.json, restrict roll-forward to that major/minor line, and configure setup-dotnet to read the file.

- Show documentation in Visual Studio Solution Explorer under a `docs` solution folder with matching subfolders.

- Move development policies and repository guidance from `AGENTS.md` to `docs/policies/development.md` and `docs/repository.md`; keep required reading links in `AGENTS.md` and add README navigation.

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

- Standardize GitHub Actions workflow filenames and display names by responsibility.

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
