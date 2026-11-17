# Repository guide

Paths in this document are relative to the repository root.
Read and follow the [development policy](policies/development.md) alongside this guide.

## About

Source for `VolocyNazad.Revit.TransactionMemoryCache`: an in-memory cache
scoped to a Revit transaction, used to avoid repeated Revit API lookups
within one transaction. Depends on `VolocyNazad.Revit.Context` (sibling
repo `toolkit.revit.context`).

## Repository structure

```
.
├── analyzers/
│   ├── Revit.TransactionMemoryCache.Analyzers/          Roslyn analyzers (RTMC001/RTMC002) -
│   │                                                    netstandard2.0, no Workspaces
│   │                                                    reference (RS1038) and no Revit API
│   │                                                    reference (works by method
│   │                                                    name/containing-type symbol matching)
│   ├── Revit.TransactionMemoryCache.Analyzers.CodeFixes/ RTMC001's CodeFixProvider - separate
│   │                                                    assembly because it needs
│   │                                                    Microsoft.CodeAnalysis.Workspaces,
│   │                                                    which a pure DiagnosticAnalyzer
│   │                                                    assembly must not reference (RS1038);
│   │                                                    project-references the Analyzers
│   │                                                    project for the shared descriptors
│   └── Revit.TransactionMemoryCache.Analyzers.Tests/    xunit.v3, hand-rolled Roslyn
│                                                        compilation/CodeFixContext harness
│                                                        (no Microsoft.CodeAnalysis.Testing -
│                                                        avoids a version clash with xunit.v3);
│                                                        compiles against a Revit-API-free stub
│                                                        of CachedElementCollector's surface
├── benchmark/
│   └── Revit.TransactionMemoryCache.Benchmark/  BenchmarkDotNet suite, runs
│                                                inside Revit (own .slnx,
│                                                not part of the main solution)
├── src/
│   └── Revit.TransactionMemoryCache/             the real project - packages the
│                                                 Analyzers project's output into its own
│                                                 NuGet package (analyzers/dotnet/cs)
└── tests/
    ├── Revit.TransactionMemoryCache.Tests/       headless xunit.v3 - only for code with
    │                                             no Revit-typed fields/parameters at all;
    │                                             merely loading a type with a
    │                                             Document/FilteredElementCollector-typed
    │                                             field requires RevitAPI.dll, which won't
    │                                             load outside a live Revit process
    └── Revit.TransactionMemoryCache.RevitTests/  Nice3point.TUnit.Revit - runs inside a
                                                   real Revit process; covers everything
                                                   Revit-typed that the headless project can't
                                                   (CachedElementCollector's fluent chain,
                                                   real Document/FilteredElementCollector
                                                   behaviour, factory init-guard)
```

## Tech stack

- .NET through the `VolocyNazad.Revit.Sdk` MSBuild SDK; the target framework follows the Revit
  version: `net48` for 2021-2024, `net8.0-windows` for 2025-2026 and `net10.0-windows` for 2027.
  The platform is `x64`.
- Built against `Nice3point.Revit.Api.RevitAPI` / `Nice3point.Revit.Api.RevitAPIUI`; the package
  version follows the selected build configuration (for example `Release_2026.4.10` uses `2026.4.10`).
- `VolocyNazad.Revit.Context` (sibling package), AutoConstructor (source-generated DI constructors),
  `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.DependencyInjection.Abstractions`,
  MinVer (git-tag-based versioning) and PolySharp (polyfills).
- Tests, headless (`tests/Revit.TransactionMemoryCache.Tests/`): **xunit.v3** through
  Microsoft.Testing.Platform, with `coverlet.collector` and `Microsoft.Extensions.DependencyInjection`.
  Only for code with zero Revit-typed fields/parameters - anything that even declares a field of a
  Revit type forces RevitAPI.dll to load, which fails outside a live Revit process.
- Tests, in-Revit (`tests/Revit.TransactionMemoryCache.RevitTests/`): **Nice3point.TUnit.Revit**
  (`RevitApiTest` base class, `[assembly: TestExecutor<RevitThreadExecutor>]`,
  `[Before(Test)]`/`[After(Test)]` with `[HookExecutor<RevitThreadExecutor>]`) - runs inside a real
  Revit process against a real `Document`. Don't wire up the real
  `IRevitContextInitializer`/`IRevitTransactionMemoryCacheInitializer` chain there (needs a
  `UIControlledApplication`, unreachable from this harness) - substitute a hand-written fake
  `IRevitTransactionMemoryCache` instead.
- Analyzers, split into two assemblies (RS1038: a `DiagnosticAnalyzer` assembly must not reference
  `Microsoft.CodeAnalysis.Workspaces`):
  - `analyzers/Revit.TransactionMemoryCache.Analyzers/`: the RTMC001/RTMC002 `DiagnosticAnalyzer`s
    and the shared `DiagnosticDescriptors` (`public`, so the CodeFixes project can reference them).
    Microsoft.CodeAnalysis.CSharp and Microsoft.CodeAnalysis.Analyzers only, `PrivateAssets="all"`.
  - `analyzers/Revit.TransactionMemoryCache.Analyzers.CodeFixes/`: RTMC001's `CodeFixProvider`,
    which needs `Microsoft.CodeAnalysis.CSharp.Workspaces` and project-references the Analyzers
    project. Both projects target `netstandard2.0`, use `IncludeBuildOutput=false`,
    `DevelopmentDependency=true` and `EnforceExtendedAnalyzerRules=true`.
  - The library references both through
    `<ProjectReference OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`, so they ride
    inside the main package (`analyzers/dotnet/cs`) instead of being packaged on their own.
  - Neither analyzer references the Revit API; both work purely off method names and containing-type
    symbol matching, so `CachedElementCollector`'s public surface is duplicated as a plain stand-in
    class in the analyzer tests. The Roslyn baseline is in the
    [Roslyn compatibility policy](policies/build.md#roslyn-compatibility).
- Package versions are managed centrally in `Directory.Packages.props`. Headless test packages are
  also declared there because the analyzer tests live under `analyzers/`;
  `tests/Directory.Packages.props` adds the Revit-major-specific `Nice3point.TUnit.Revit` version.
- Benchmarks (`benchmark/`): Nice3point.BenchmarkDotNet.Revit runs BenchmarkDotNet in-process inside
  a real Revit session, since `FilteredElementCollector` needs a live `Document`. A separate `.slnx`
  with a single `Release_2025.4.60`/`net8.0-windows` configuration; the
  [benchmark policy](policies/benchmarks.md) owns when it runs.

## Documentation layout

- `AGENTS.md` links to the required repository guidance and routes a change to the document that owns it.
- `docs/policies/` contains the repository policies - the rules a change must follow. The
  [policy index](policies/README.md) says what each policy covers.
- `docs/repository.md` describes the project, repository structure, and technology stack.
- `changelog.d/` holds the pending changelog fragments: a consumer-visible change adds one, and the
  release preparation composes them into `CHANGELOG.md`; the rules are in the
  [changelog policy](policies/changelog.md). Git does not track empty directories, so the folder
  exists only while a fragment is pending.
- `scripts/` holds the repository validator, the changelog tool and the release verification.

The root solution exposes the documentation files under a `docs` solution folder in Visual Studio, preserving their subfolder structure. When adding documentation files, also add them as solution items; solution folders do not automatically include new files.

CI artifact names include the build configuration so each Revit matrix job uploads a distinct artifact. NuGet publishing builds its own packages and does not download these CI artifacts.

The root `global.json` selects stable .NET SDK 10.0 (minimum `10.0.103`, `rollForward: latestFeature`). CI and publishing install the SDK from this file. Additional SDK installations may provide older test runtimes. See the [SDK selection policy](policies/build.md#sdk-selection).

## Solution items

The root solution exposes repository-level documents and configuration under `solutionItems`, GitHub files and maintenance scripts in matching subfolders, and documentation under `docs/`. The list is explicit, not a filesystem glob; keep links up to date when files change. See the [solution items policy](policies/repository-maintenance.md#solution-items).

## Repository validation

`scripts/Validate-Repository.ps1` enforces the required repository documents,
their navigation links, the complete and valid Solution Items, and the workflow action pins. The
`.github/workflows/repository-policy.yml` workflow runs it, the pending fragment check
(`Update-Changelog.ps1 -Verify`) and the release structure check (`Verify-Release.ps1`) for pushes
and pull requests. See the [repository maintenance policy](policies/repository-maintenance.md#repository-validation).

## Formatting

The root `.editorconfig` defines the portable formatting baseline. Existing repositories may add stricter C# or analyzer-specific settings. See the [repository maintenance policy](policies/repository-maintenance.md#formatting-baseline).

## Continuous integration

`build-and-test` builds every Revit configuration in the matrix with `-p:Platform=x64`, warnings as
errors and documentation warnings promoted to errors, runs the headless test projects of each
configuration and uploads one artifact per configuration. `repository-policy` runs the validator, the
pending fragment check and the release structure check. `publish-nuget` is manual and refuses a commit
without a single stable tag and a matching changelog section.

## Testing

The headless suite covers the cache, the registrator and the collector key builder;
`analyzers/Revit.TransactionMemoryCache.Analyzers.Tests` covers RTMC001/RTMC002 and the code fix.
`tests/Revit.TransactionMemoryCache.RevitTests` covers the Revit-typed behaviour inside a live Revit
process and is excluded from CI by the [testing policy](policies/testing.md).

## Versioning and release tags

The library uses MinVer 8 with stable tags in the `0.0.PATCH` format and no `v` prefix. A tagged
commit is built separately for Revit 2021.1.100, 2022.1.80, 2023.1.90, 2024.3.60, 2025.4.60,
2026.4.10, and 2027.3.0; tag `0.0.10`, for example, produces package versions `2021.0.10`,
`2022.0.10`, `2023.0.10`, `2024.0.10`, `2025.0.10`, `2026.0.10`, and `2027.0.10`.
`scripts/Verify-Release.ps1 -FromHeadTag` accepts exactly one matching tag at `HEAD`, and the publish
workflow verifies the package ID and the Revit-specific version before pushing to NuGet.

The tagged commit must already contain the release section in `CHANGELOG.md` with an empty
`Unreleased` section above it and no pending fragment; the publish workflow stops otherwise. See the
[release policy](policies/releases.md) and the [changelog policy](policies/changelog.md#release).
