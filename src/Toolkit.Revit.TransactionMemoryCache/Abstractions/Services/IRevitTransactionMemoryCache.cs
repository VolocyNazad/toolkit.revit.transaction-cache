namespace Revit.TransactionMemoryCache.Abstractions.Services;

public interface IRevitTransactionMemoryCache
{
    TItem? GetOrCreate<TItem>(object key, Func<TItem> factory);
}
