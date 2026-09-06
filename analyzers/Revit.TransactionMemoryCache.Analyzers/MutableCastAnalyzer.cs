using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Revit.TransactionMemoryCache.Analyzers;

/// <summary>
/// RTMC001: flags an explicit cast (or <c>as</c> expression) of a <c>CachedElementCollector.ToElements()</c>/
/// <c>ToElementIds()</c> call result to a mutable collection type (an array, or any type implementing
/// <see cref="ICollection{T}"/>, e.g. <c>List&lt;T&gt;</c>).
///
/// v1 scope: only a cast applied directly to the terminal call expression is detected
/// (<c>(List&lt;Element&gt;)collector.ToElements()</c>) - a cast applied later, via an intermediate variable, is
/// not tracked (that would require dataflow analysis across statements).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MutableCastAnalyzer : DiagnosticAnalyzer
{
    private const string CachedElementCollectorFullName = "Revit.TransactionMemoryCache.Services.CachedElementCollector";
    private static readonly string[] TerminalMethodNames = ["ToElements", "ToElementIds"];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.MutableCastOfCachedResult);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeCast, SyntaxKind.CastExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAs, SyntaxKind.AsExpression);
    }

    private static void AnalyzeCast(SyntaxNodeAnalysisContext context)
    {
        var cast = (CastExpressionSyntax)context.Node;
        Analyze(context, cast.Type, cast.Expression, cast.GetLocation());
    }

    private static void AnalyzeAs(SyntaxNodeAnalysisContext context)
    {
        var asExpression = (BinaryExpressionSyntax)context.Node;
        if (asExpression.Right is not TypeSyntax targetType)
            return;

        Analyze(context, targetType, asExpression.Left, asExpression.GetLocation());
    }

    private static void Analyze(
        SyntaxNodeAnalysisContext context,
        TypeSyntax targetTypeSyntax,
        ExpressionSyntax sourceExpression,
        Location location)
    {
        var targetType = context.SemanticModel.GetTypeInfo(targetTypeSyntax, context.CancellationToken).Type;
        if (targetType is null || !IsMutableCollectionType(targetType))
            return;

        var innerExpression = Unwrap(sourceExpression);
        if (context.SemanticModel.GetSymbolInfo(innerExpression, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol)
            return;

        if (!TerminalMethodNames.Contains(methodSymbol.Name))
            return;

        if (methodSymbol.ContainingType?.ToDisplayString() != CachedElementCollectorFullName)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.MutableCastOfCachedResult,
            location,
            methodSymbol.Name,
            targetType.ToDisplayString()));
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        return expression;
    }

    private static bool IsMutableCollectionType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
            return true;

        const string collectionOfT = "System.Collections.Generic.ICollection<T>";

        if (type.OriginalDefinition.ToDisplayString() == collectionOfT)
            return true;

        return type.AllInterfaces.Any(i => i.OriginalDefinition.ToDisplayString() == collectionOfT);
    }
}
