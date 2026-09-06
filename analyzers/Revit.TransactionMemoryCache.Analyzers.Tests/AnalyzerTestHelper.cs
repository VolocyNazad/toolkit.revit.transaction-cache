using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Revit.TransactionMemoryCache.Analyzers.Tests;

/// <summary>
/// Compiles test source against a hand-written stub of <c>CachedElementCollector</c>'s public surface (no
/// dependency on the real Revit API assembly - the analyzers only care about method names/containing types, not
/// actual Revit types) and runs a single analyzer over the resulting compilation.
/// </summary>
internal static class AnalyzerTestHelper
{
    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(DiagnosticAnalyzer analyzer, string source)
    {
        var stubTree = CSharpSyntaxTree.ParseText(TestStubs.CachedElementCollectorStub);
        var sourceTree = CSharpSyntaxTree.ParseText(source);

        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTests",
            syntaxTrees: [stubTree, sourceTree],
            references: GetSystemReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var compilationErrors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (compilationErrors.Length > 0)
        {
            throw new InvalidOperationException(
                "Test source failed to compile against the CachedElementCollector stub:\n" +
                string.Join('\n', compilationErrors.Select(d => d.ToString())));
        }

        var compilationWithAnalyzers = compilation.WithAnalyzers([analyzer]);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync().ConfigureAwait(false);
    }

    private static IEnumerable<MetadataReference> GetSystemReferences()
    {
        var trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
    }
}
