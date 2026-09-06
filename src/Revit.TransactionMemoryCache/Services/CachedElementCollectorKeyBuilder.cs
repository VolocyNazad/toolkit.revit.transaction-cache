namespace Revit.TransactionMemoryCache.Services;

/// <summary>
/// Builds the cache key for <see cref="CachedElementCollector"/>: combines a document identity, the set of
/// fluent-configuration fragments recorded so far, and the terminal operation name (<c>ToElements</c>/
/// <c>ToElementIds</c>) into a single deterministic string. <c>fragments</c> is sorted (ordinal)
/// before joining, so the same logical query built through fluent calls in a different order produces the
/// same key. Pure and independent of any live Revit <c>Document</c>/API, so it can be unit tested without a
/// Revit process.
/// </summary>
public static class CachedElementCollectorKeyBuilder
{
    private const string Prefix = "CachedElementCollector";

    /// <summary>
    /// Builds the cache key.
    /// </summary>
    /// <param name="documentIdentity">A value stable for the lifetime of one open document - see
    /// <see cref="System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(object)"/>, which is what
    /// <see cref="CachedElementCollector"/> passes here.</param>
    /// <param name="terminal">The terminal operation name (e.g. <c>"ToElements"</c>).</param>
    /// <param name="fragments">Key fragments recorded by the fluent configuration calls, in any order.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="terminal"/> or <paramref name="fragments"/> is <see langword="null"/>.</exception>
    public static string Build(int documentIdentity, string terminal, IEnumerable<string> fragments)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(fragments);

        var sortedFragments = fragments.OrderBy(fragment => fragment, StringComparer.Ordinal);
        return $"{Prefix}|{documentIdentity}|{string.Join('|', sortedFragments)}|{terminal}";
    }
}
