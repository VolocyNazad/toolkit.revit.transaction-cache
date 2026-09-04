# AGENTS.md

## Policy

The stack documented below is the default and takes priority over
whatever an agent might otherwise reach for. Prefer what's already in use
over introducing an alternative. If a deviation seems necessary, say so
explicitly to the user and get confirmation before adding it.

If a change affects the folder structure or the tech stack (a new/removed project, a new dependency, a version bump worth recording, a new convention), update this file accordingly as part of the same change - don't leave it to a later pass.

Before changing existing tests or writing new ones, ask the user first – confirm what should be covered and how (or that the change is trivial enough not to need it) rather than deciding unilaterally.

Commit messages follow Conventional Commits (`<type>(<scope>): <description>`, e.g. `feat(manifest): ...`, `fix(...): ...`, `docs(agents): ...`, `test(...): ...`, `chore(...): ...`, `refactor(...): ...`) - scope optional but preferred when it clarifies what changed.

## About

Source for `VolocyNazad.Revit.TransactionMemoryCache`: an in-memory cache
scoped to a Revit transaction, used to avoid repeated Revit API lookups
within one transaction. Depends on `VolocyNazad.Revit.Context` (sibling
repo `toolkit.revit.context`).

## Repository structure

```
.
├── src/
│   ├── Revit.TransactionMemoryCache/            the real project
│   └── Toolkit.Revit.TransactionMemoryCache/    empty (bin/obj only,
│                                                 no .csproj) - stale
│                                                 leftover, not a project
└── tests/
    └── Revit.TransactionMemoryCache.Tests/
```

## Tech stack

- .NET, built against `Revit_All_Main_Versions_API_x64` (Nice3point's
  multi-version Revit API reference package)
- VolocyNazad.Revit.Context (sibling package)
- AutoConstructor (source-generated DI constructors)
- Microsoft.Extensions.Caching.Memory,
  Microsoft.Extensions.DependencyInjection.Abstractions
- MinVer (git-tag-based versioning), PolySharp (polyfills)
- Tests: **xunit.v3** (matches `toolkit.revit.async` in this same repo family) + coverlet.collector +
  Microsoft.Extensions.DependencyInjection
- No central package management (`Directory.Packages.props` is empty) -
  package versions are set per-`<PackageReference>`
