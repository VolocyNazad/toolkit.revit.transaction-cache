# Revit.Transaction-Cache

[![Revit 2011-2027](https://img.shields.io/badge/Revit-2011–2027-green.svg)](https://autodesk.com/revit)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![VolocyNazad](https://img.shields.io/badge/VolocyNazad-blue.svg)](https://github.com/VolocyNazad)

> Кэш значений в памяти, привязанный к жизненному циклу документа Revit.

Revit.TransactionMemoryCache — сервис мемоизации дорогих вычислений над Revit API с автоматическим сбросом кэша при изменении документа или переключении вида, с поддержкой DI-контейнеризации.

## Возможности

- `IRevitTransactionMemoryCache` — `GetOrCreate<TItem>(object key, Func<TItem> factory)`: возвращает закэшированное значение по ключу либо вычисляет его через `factory` и кэширует.
- `IRevitTransactionMemoryCacheInitializer` — `Initialize()`/`Deinitialize()`: подписывает/отписывает кэш от событий `DocumentChanged` и `ViewActivated`, автоматически сбрасывая все закэшированные значения при их наступлении.
- `RevitTransactionMemoryCache` — единственная реализация обоих интерфейсов поверх `IMemoryCache` и [`IRevitContext`](https://github.com/VolocyNazad/toolkit.revit.context).
- Регистрация в DI одной строкой через `AddTransactionMemoryCache()`.
- Потокобезопасный жизненный цикл (инициализация, сброс, `Dispose`).

## Установка

```
dotnet add package VolocyNazad.Revit.TransactionMemoryCache
```

## Использование

Регистрация сервисов в контейнере DI (требует также `VolocyNazad.Revit.Context`):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Revit.Context.DI;
using Revit.TransactionMemoryCache.DI;

services.AddRevitContext();
services.AddTransactionMemoryCache();
```

Инициализация в `IExternalApplication.OnStartup` (после инициализации контекста):

```csharp
using Autodesk.Revit.UI;
using Revit.Context.Abstractions.Services;
using Revit.TransactionMemoryCache.Abstractions.Services;

public Result OnStartup(UIControlledApplication application)
{
    serviceProvider.GetRequiredService<IRevitContextInitializer>().Initialize(application);
    serviceProvider.GetRequiredService<IRevitTransactionMemoryCacheInitializer>().Initialize();

    return Result.Succeeded;
}
```

Использование кэша в любом сервисе:

```csharp
using Revit.TransactionMemoryCache.Abstractions.Services;

public sealed class MyService(IRevitTransactionMemoryCache cache)
{
    public IList<Wall> GetWalls(Document doc) =>
        cache.GetOrCreate("walls", () => new FilteredElementCollector(doc)
            .OfClass(typeof(Wall))
            .Cast<Wall>()
            .ToList())!;
}
```

Кэш автоматически сбрасывается при изменении документа (`DocumentChanged`) и при переключении активного вида (`ViewActivated`), поэтому повторный вызов `GetOrCreate` с тем же ключом после этих событий заново вычислит значение.

## Поддерживаемые версии Revit

Пакет собирается под версии Revit 2011–2027 (см. конфигурации в `Revit.TransactionMemoryCache.csproj`), таргетируя `net48` для версий до 2025 и `net8.0-windows` для 2025+.

## Требования

- .NET SDK 10.0.103+ (см. `global.json`)
- Revit API (пакет `Revit_All_Main_Versions_API_x64`)
- `VolocyNazad.Revit.Context`

## Лицензия

MIT, см. [LICENSE.md](LICENSE.md).
