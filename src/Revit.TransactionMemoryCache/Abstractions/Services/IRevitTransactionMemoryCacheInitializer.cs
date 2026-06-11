namespace Revit.TransactionMemoryCache.Abstractions.Services;

public interface IRevitTransactionMemoryCacheInitializer
{
    void Deinitialize();
    void Initialize();
}
