# AGENTS.md

## Policy

The stack documented below is the default and takes priority over
whatever an agent might otherwise reach for. Prefer what's already in use
over introducing an alternative. If a deviation seems necessary, say so
explicitly to the user and get confirmation before adding it.

If a change affects the folder structure or the tech stack (a new/removed project, a new dependency, a version bump worth recording, a new convention), update this file accordingly as part of the same change - don't leave it to a later pass.

Before changing existing tests or writing new ones, ask the user first – confirm what should be covered and how (or that the change is trivial enough not to need it) rather than deciding unilaterally.

Commit messages follow Conventional Commits (`<type>(<scope>): <description>`, e.g. `feat(manifest): ...`, `fix(...): ...`, `docs(agents): ...`, `test(...): ...`, `chore(...): ...`, `refactor(...): ...`) - scope optional but preferred when it clarifies what changed.

Don't `git push` - commit locally and leave pushing to the user, unless they explicitly ask for a push.

Tests that touch live Revit API objects must use `Nice3point.TUnit.Revit` (see this repo's Tech stack/testing notes for when that's needed vs plain xunit) - it runs inside a real Revit process, so it can't be used for tests that exercise `RevitAPIUI`.

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
│   │                                                     netstandard2.0, no Workspaces
│   │                                                     reference (RS1038) and no Revit API
│   │                                                     reference (works by method
│   │                                                     name/containing-type symbol matching)
│   ├── Revit.TransactionMemoryCache.Analyzers.CodeFixes/ RTMC001's CodeFixProvider - separate
│   │                                                     assembly because it needs
│   │                                                     Microsoft.CodeAnalysis.Workspaces,
│   │                                                     which a pure DiagnosticAnalyzer
│   │                                                     assembly must not reference (RS1038);
│   │                                                     project-references the Analyzers
│   │                                                     project for the shared descriptors
│   └── Revit.TransactionMemoryCache.Analyzers.Tests/    xunit.v3, hand-rolled Roslyn
│                                                          compilation/CodeFixContext harness
│                                                          (no Microsoft.CodeAnalysis.Testing -
│                                                          avoids a version clash with xunit.v3);
│                                                          compiles against a Revit-API-free stub
│                                                          of CachedElementCollector's surface
├── benchmark/
│   └── Revit.TransactionMemoryCache.Benchmark/  BenchmarkDotNet suite, runs
│                                                 inside Revit (own .slnx,
│                                                 not part of the main solution)
├── src/
│   └── Revit.TransactionMemoryCache/             the real project - packages the
│                                                  Analyzers project's output into its own
│                                                  NuGet package (analyzers/dotnet/cs)
└── tests/
    ├── Revit.TransactionMemoryCache.Tests/       headless xunit.v3 - only for code with
    │                                              no Revit-typed fields/parameters at all;
    │                                              merely loading a type with a
    │                                              Document/FilteredElementCollector-typed
    │                                              field requires RevitAPI.dll, which won't
    │                                              load outside a live Revit process
    └── Revit.TransactionMemoryCache.RevitTests/  Nice3point.TUnit.Revit - runs inside a
                                                   real Revit process; covers everything
                                                   Revit-typed that the headless project can't
                                                   (CachedElementCollector's fluent chain,
                                                   real Document/FilteredElementCollector
                                                   behaviour, factory init-guard)
```

## Tech stack

- .NET, built against `Revit_All_Main_Versions_API_x64` (Nice3point's
  multi-version Revit API reference package)
- VolocyNazad.Revit.Context (sibling package)
- AutoConstructor (source-generated DI constructors)
- Microsoft.Extensions.Caching.Memory,
  Microsoft.Extensions.DependencyInjection.Abstractions
- MinVer (git-tag-based versioning), PolySharp (polyfills)
- Tests, headless (`tests/Revit.TransactionMemoryCache.Tests/`): **xunit.v3**
  (matches `toolkit.revit.async` in this same repo family) + coverlet.collector +
  Microsoft.Extensions.DependencyInjection. Only for code with zero
  Revit-typed fields/parameters - anything that even declares a field of
  a Revit type forces RevitAPI.dll to load, which fails outside a live
  Revit process.
- Tests, in-Revit (`tests/Revit.TransactionMemoryCache.RevitTests/`):
  **Nice3point.TUnit.Revit** (`RevitApiTest` base class,
  `[assembly: TestExecutor<RevitThreadExecutor>]`, `[Before(Test)]`/
  `[After(Test)]` with `[HookExecutor<RevitThreadExecutor>]`) - runs
  inside a real Revit process against a real `Document`. Same pattern as
  `revit.linter`'s `*.RevitTests` projects: don't wire up the real
  `IRevitContextInitializer`/`IRevitTransactionMemoryCacheInitializer`
  chain there (needs a `UIControlledApplication`, unreachable from this
  harness) - substitute a hand-written fake `IRevitTransactionMemoryCache`
  instead, same as `revit.linter` does in its own RevitTests.
- Analyzers, split into two assemblies (RS1038: a `DiagnosticAnalyzer`
  assembly must not reference `Microsoft.CodeAnalysis.Workspaces`):
  - `analyzers/Revit.TransactionMemoryCache.Analyzers/`: the RTMC001/RTMC002
    `DiagnosticAnalyzer`s + shared `DiagnosticDescriptors` (`public`, so the
    CodeFixes project can reference them). Microsoft.CodeAnalysis.CSharp +
    Microsoft.CodeAnalysis.Analyzers only, `PrivateAssets="all"`.
  - `analyzers/Revit.TransactionMemoryCache.Analyzers.CodeFixes/`: RTMC001's
    `CodeFixProvider`, which needs `Microsoft.CodeAnalysis.CSharp.Workspaces`
    (`Document`/`Solution`/`CodeAction`/`ApplyChangesOperation`) -
    project-references the Analyzers project.
  Both projects: `netstandard2.0`, `IncludeBuildOutput=false`,
  `DevelopmentDependency=true`, `EnforceExtendedAnalyzerRules=true`.
  Referenced from the main project via two
  `<ProjectReference OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`
  entries so both ride along inside the main package (`analyzers/dotnet/cs`)
  rather than being packaged/shipped on their own. No Revit API reference -
  both analyzers work purely off method names + containing-type symbol
  matching, so `CachedElementCollector`'s public surface is duplicated as
  a plain stand-in class in the analyzer tests instead of referencing the
  real (Revit-API-dependent) assembly.
- No central package management (`Directory.Packages.props` is empty) -
  package versions are set per-`<PackageReference>`
- Benchmarks (`benchmark/`): Nice3point.BenchmarkDotNet.Revit (runs BenchmarkDotNet in-process inside a real Revit session, since `FilteredElementCollector` needs a live `Document`) - separate `.slnx`, single `Release_2025.0.0`/net8.0-windows configuration, same pattern as `revit.linter`'s `benchmark/`
