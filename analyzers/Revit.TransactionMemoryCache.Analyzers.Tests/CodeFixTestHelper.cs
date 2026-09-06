using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Revit.TransactionMemoryCache.Analyzers.Tests;

/// <summary>
/// Runs a single analyzer + its code fix over test source (compiled against the same Revit-API-free
/// <see cref="TestStubs.CachedElementCollectorStub"/> used by <see cref="AnalyzerTestHelper"/>) and returns the
/// fixed document's text, without depending on the <c>Microsoft.CodeAnalysis.Testing</c> package family.
/// </summary>
internal static class CodeFixTestHelper
{
    public static async Task<string> ApplyFixAsync(DiagnosticAnalyzer analyzer, CodeFixProvider codeFixProvider, string source)
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var stubDocumentId = DocumentId.CreateNewId(projectId);
        var sourceDocumentId = DocumentId.CreateNewId(projectId);

        var solution = workspace.CurrentSolution
            .AddProject(projectId, "AnalyzerTests", "AnalyzerTests", LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(projectId, GetSystemReferences())
            .AddDocument(stubDocumentId, "Stub.cs", TestStubs.CachedElementCollectorStub)
            .AddDocument(sourceDocumentId, "Test.cs", source);

        var document = solution.GetDocument(sourceDocumentId)!;
        var documentSyntaxTree = await document.GetSyntaxTreeAsync().ConfigureAwait(false);
        var compilation = (await solution.Projects.Single().GetCompilationAsync().ConfigureAwait(false))!;

        var compilationWithAnalyzers = compilation.WithAnalyzers([analyzer]);
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync().ConfigureAwait(false);
        var diagnostic = diagnostics.Single(d => d.Location.SourceTree == documentSyntaxTree);

        var registeredActions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => registeredActions.Add(action),
            CancellationToken.None);

        await codeFixProvider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
        var codeAction = registeredActions.Single();

        var operations = await codeAction.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
        var applyChangesOperation = operations.OfType<ApplyChangesOperation>().Single();
        var changedDocument = applyChangesOperation.ChangedSolution.GetDocument(sourceDocumentId)!;
        var newRoot = await changedDocument.GetSyntaxRootAsync().ConfigureAwait(false);

        return newRoot!.ToFullString();
    }

    private static IEnumerable<MetadataReference> GetSystemReferences()
    {
        var trustedPlatformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
    }
}
