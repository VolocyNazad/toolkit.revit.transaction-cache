namespace Revit.TransactionMemoryCache.Services;

/// <summary>
/// Polyfills <c>System.ArgumentNullException.ThrowIfNull(object?, string?)</c> (.NET 6+) for the older,
/// net48-era target frameworks this library also builds for (pre-~2022 Revit versions) - PolySharp can't help
/// here, since it can't add members to an external, already-shipped BCL type it doesn't own. Behaves the same:
/// throws <c>System.ArgumentNullException</c> with the checked expression's text as the parameter name, via
/// <see cref="System.Runtime.CompilerServices.CallerArgumentExpressionAttribute"/> (a compiler-recognized
/// attribute, which PolySharp *does* polyfill on older target frameworks).
/// </summary>
internal static class ThrowHelper
{
    public static void ThrowIfNull<T>(
        T? value,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : class
    {
        if (value is null)
            throw new System.ArgumentNullException(paramName);
    }
}
