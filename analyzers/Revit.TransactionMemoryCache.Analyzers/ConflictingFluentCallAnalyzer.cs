using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Revit.TransactionMemoryCache.Analyzers;

/// <summary>
/// RTMC002: flags a <c>CachedElementCollector</c> fluent call that conflicts with an earlier call already present
/// in the same chain - <c>OfClass</c>/<c>Of&lt;T&gt;</c>, <c>OfCategory</c> and <c>Excluding</c> may each appear at
/// most once, and <c>WhereElementIsElementType</c>/<c>WhereElementIsNotElementType</c> are mutually exclusive.
/// <c>CachedElementCollector</c> throws <see cref="InvalidOperationException"/> immediately when this
/// happens at runtime, so this analyzer surfaces it at compile time instead.
///
/// v1 scope: only a single fluent expression chain is tracked (walking back through <c>.Expression</c> on each
/// member access) - a chain broken up across intermediate variables is not tracked (that would require dataflow
/// analysis across statements).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConflictingFluentCallAnalyzer : DiagnosticAnalyzer
{
    private const string CachedElementCollectorFullName = "Revit.TransactionMemoryCache.Services.CachedElementCollector";

    /// <summary>Maps each restricted fluent method name to its conflict group - two calls conflict if they share a group.</summary>
    private static readonly Dictionary<string, string> ConflictGroups = new()
    {
        ["OfClass"] = "OfClass",
        ["Of"] = "OfClass", // generic Of<TElement>() is sugar for OfClass(typeof(TElement))
        ["OfCategory"] = "OfCategory",
        ["Excluding"] = "Excluding",
        ["WhereElementIsElementType"] = "ElementTypeFilter",
        ["WhereElementIsNotElementType"] = "ElementTypeFilter",
    };

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.ConflictingFluentCall);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol)
            return;

        if (!ConflictGroups.TryGetValue(methodSymbol.Name, out var group))
            return;

        if (methodSymbol.ContainingType?.ToDisplayString() != CachedElementCollectorFullName)
            return;

        var receiver = memberAccess.Expression;
        while (receiver is InvocationExpressionSyntax precedingInvocation)
        {
            if (context.SemanticModel.GetSymbolInfo(precedingInvocation, context.CancellationToken).Symbol is not IMethodSymbol precedingSymbol)
                break;

            if (precedingSymbol.ContainingType?.ToDisplayString() != CachedElementCollectorFullName)
                break;

            if (ConflictGroups.TryGetValue(precedingSymbol.Name, out var precedingGroup) && precedingGroup == group)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ConflictingFluentCall,
                    memberAccess.Name.GetLocation(),
                    methodSymbol.Name));
                return;
            }

            if (precedingInvocation.Expression is not MemberAccessExpressionSyntax precedingMemberAccess)
                break;

            receiver = precedingMemberAccess.Expression;
        }
    }
}
