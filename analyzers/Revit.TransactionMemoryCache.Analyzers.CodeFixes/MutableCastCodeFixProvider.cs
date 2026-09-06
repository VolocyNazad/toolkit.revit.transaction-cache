using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Revit.TransactionMemoryCache.Analyzers;

/// <summary>
/// Code fix for RTMC001: replaces an unsafe downcast of a <c>CachedElementCollector.ToElements()</c>/
/// <c>ToElementIds()</c> result (e.g. <c>(List&lt;Element&gt;)collector.ToElements()</c>) with a safe copy -
/// <c>.ToArray()</c> when the target was an array type, <c>.ToList()</c> otherwise. A copy is always safe to
/// mutate, unlike the shared cached instance the cast was reaching into.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MutableCastCodeFixProvider))]
[Shared]
public sealed class MutableCastCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.MutableCastOfCachedResult.Id);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        var (sourceExpression, isArrayTarget) = node switch
        {
            CastExpressionSyntax cast => (cast.Expression, cast.Type is ArrayTypeSyntax),
            BinaryExpressionSyntax asExpression when asExpression.IsKind(SyntaxKind.AsExpression) =>
                (asExpression.Left, asExpression.Right is ArrayTypeSyntax),
            _ => (null, false),
        };

        if (sourceExpression is null)
            return;

        var methodName = isArrayTarget ? "ToArray" : "ToList";
        var title = $"Replace cast with '.{methodName}()' (creates a safe copy)";

        context.RegisterCodeFix(
            CodeAction.Create(
                title,
                cancellationToken => ReplaceWithCopyAsync(context.Document, node, sourceExpression, methodName, cancellationToken),
                equivalenceKey: title),
            diagnostic);
    }

    private static async Task<Document> ReplaceWithCopyAsync(
        Document document,
        SyntaxNode nodeToReplace,
        ExpressionSyntax sourceExpression,
        string methodName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var replacement = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    sourceExpression.WithoutTrivia(),
                    SyntaxFactory.IdentifierName(methodName)))
            .WithTriviaFrom(nodeToReplace);

        var newRoot = root.ReplaceNode(nodeToReplace, replacement);

        // .ToList()/.ToArray() are System.Linq extension methods on IEnumerable<T> - make sure the file can see them.
        if (newRoot is CompilationUnitSyntax compilationUnit && !HasSystemLinqUsing(compilationUnit))
        {
            newRoot = compilationUnit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Linq")));
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private static bool HasSystemLinqUsing(CompilationUnitSyntax compilationUnit) =>
        compilationUnit.Usings.Any(usingDirective => usingDirective.Name?.ToString() == "System.Linq");
}
