using Microsoft.CodeAnalysis;

namespace Revit.TransactionMemoryCache.Analyzers;

/// <summary>Diagnostic descriptors shared by every analyzer in this project.</summary>
public static class DiagnosticDescriptors
{
    private const string Category = "Usage";

    /// <summary>
    /// RTMC001 (Warning): the result of <c>CachedElementCollector.ToElements()</c>/<c>ToElementIds()</c> was cast
    /// to a mutable collection type. That result is shared across every caller with an equivalent fluent chain -
    /// mutating it silently corrupts the cache for everyone else.
    /// </summary>
    public static readonly DiagnosticDescriptor MutableCastOfCachedResult = new(
        id: "RTMC001",
        title: "Cached collector result cast to a mutable collection type",
        messageFormat: "The result of '{0}' is shared across every caller with an equivalent chain - casting it to '{1}' and mutating it would corrupt the cached value for everyone else",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "CachedElementCollector.ToElements()/ToElementIds() return a shared, cached list. Casting the " +
                      "result to a mutable collection type (e.g. List<T>) and modifying it silently corrupts the " +
                      "cache for every other caller with the same chain.");

    /// <summary>
    /// RTMC002 (Error): a <c>CachedElementCollector</c> fluent call conflicts with (duplicates, or is mutually
    /// exclusive with) an earlier call in the same chain. <c>CachedElementCollector</c> throws
    /// <see cref="InvalidOperationException"/> immediately when this happens at runtime, so the analyzer
    /// surfaces it at compile time instead.
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingFluentCall = new(
        id: "RTMC002",
        title: "Conflicting or duplicate CachedElementCollector fluent call",
        messageFormat: "'{0}' has already been called (or is mutually exclusive with a call already present) on " +
                       "this CachedElementCollector chain - this throws InvalidOperationException at runtime",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "OfClass/Of<T>, OfCategory and Excluding may each be called at most once per chain, and " +
                      "WhereElementIsElementType/WhereElementIsNotElementType are mutually exclusive - " +
                      "CachedElementCollector throws InvalidOperationException immediately when violated.");
}
