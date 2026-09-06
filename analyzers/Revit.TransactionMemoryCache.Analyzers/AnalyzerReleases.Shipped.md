; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.0.8

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RTMC001 | Usage    | Warning  | Cached collector result cast to a mutable collection type
RTMC002 | Usage    | Error    | Conflicting or duplicate CachedElementCollector fluent call
