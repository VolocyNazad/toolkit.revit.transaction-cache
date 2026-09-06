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
- `CachedElementCollector` — fluent-обёртка над `FilteredElementCollector` с автоматическим кэшированием результата (см. раздел ниже).
- Два Roslyn-анализатора (`RTMC001`/`RTMC002`), подключаются автоматически вместе с пакетом.

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

## CachedElementCollector

Fluent-обёртка над `FilteredElementCollector`, но с автоматическим кэшированием результата в `IRevitTransactionMemoryCache`. Ничего не обращается к Revit API до терминального вызова (`ToElements()`/`ToElementIds()`) — до этого момента цепочка вызовов только собирает фрагменты будущего ключа кэша.

```csharp
using Revit.TransactionMemoryCache.Abstractions.Services;

public sealed class MyService(ICachedElementCollectorFactory collectorFactory)
{
    public IReadOnlyList<Wall> GetWalls(Document doc) =>
        collectorFactory.Create(doc)
            .OfClass(typeof(Wall)) // или .Of<Wall>()
            .WhereElementIsNotElementType()
            .ToElements()
            .Cast<Wall>()
            .ToList();
}
```

`ICachedElementCollectorFactory` резолвится через DI (регистрируется вместе с `AddTransactionMemoryCache()`) и бросает `InvalidOperationException`, если `IRevitTransactionMemoryCacheInitializer.Initialize()` ещё не вызывался — кэш без автоматической инвалидации молча отдавал бы устаревшие данные после изменения документа.

**Ключевые правила:**

- `OfClass`/`Of<T>`, `OfCategory`, `Excluding` можно вызвать **не более одного раза** за цепочку; `WhereElementIsElementType`/`WhereElementIsNotElementType` — взаимоисключающие. Нарушение бросает `InvalidOperationException` сразу же, не дожидаясь промаха кэша. Аналог этой проверки на этапе компиляции — анализатор `RTMC002` (Error).
- Порядок fluent-вызовов **не влияет** на ключ кэша — фрагменты канонизируются (сортируются) перед склейкой, так что `OfClass(...).Excluding(...)` и `Excluding(...).OfClass(...)` — один и тот же ключ.
- Результат `ToElements()`/`ToElementIds()` — **общий** для всех вызывающих с эквивалентной цепочкой. Не приводите его к изменяемому типу (`List<T>`, массив) и не мутируйте — это молча испортит закэшированное значение для всех остальных. Анализатор `RTMC001` (Warning) ловит такой каст на месте вызова и предлагает code fix — заменить на `.ToList()`/`.ToArray()` (создаёт копию).
- `WherePasses(ElementFilter)` **намеренно не поддерживается** — у большинства подклассов `ElementFilter` нет надёжного value equality, чтобы строить по ним детерминированный ключ кэша. Вместо этого — узкие fluent-обёртки под конкретные value-типы параметров:
  - `OfCategories(IEnumerable<BuiltInCategory>)` — аналог `OfCategory`, но для нескольких категорий сразу (`ElementMulticategoryFilter`). Как и `OfCategory`, вызывается не более одного раза за цепочку, и конфликтует с `OfCategory` (это один и тот же "слот" под категорию).
  - `WhereParameterEquals(BuiltInParameter, ...)` / `WhereParameterEquals(ElementId parameterId, ...)` — фильтр по значению параметра (`ElementParameterFilter`/`ParameterFilterRuleFactory.CreateEqualsRule`). В отличие от остальных fluent-методов, **можно вызывать сколько угодно раз** за цепочку — каждый вызов сужает результат (аналогично нескольким `WherePasses` подряд на `FilteredElementCollector`). Покрывает все 4 `Parameter.StorageType`:
    - `int`, `string` (регистронезависимо), `ElementId` — без допущений;
    - `double` — только с явным `epsilon` (`WhereParameterEquals(parameter, value, epsilon)`), т.к. точное сравнение `double` почти никогда не то, что нужно, а разумная погрешность зависит от единиц измерения параметра (длина/площадь/угол).
    - Перегрузка с `ElementId parameterId` вместо `BuiltInParameter` — для shared/project-параметров, у которых нет `BuiltInParameter` (например, `SharedParameterElement.Id`).

## Известные ограничения

- **Инвалидация — глобальная для всех открытых документов, не по документу.** `IRevitTransactionMemoryCache` — единственный singleton-инстанс на процесс аддина (регистрируется через `AddTransactionMemoryCache()`); изоляция между документами обеспечивается не отдельными экземплярами кэша, а тем, что идентификатор документа зашит в сам ключ (`RuntimeHelpers.GetHashCode(document)` внутри `CachedElementCollectorKeyBuilder`/произвольных ключей через `GetOrCreate`). Но `RevitTransactionMemoryCache.Refresh()` сбрасывает **один общий** `CancellationTokenSource`, на который подписаны записи вообще всех документов — при `DocumentChanged`/`ViewActivated` в одном документе кэш **всех** одновременно открытых документов сбрасывается целиком, а не только изменившегося.
  - Не приводит к устаревшим данным (это избыточная, а не недостаточная инвалидация), но снижает эффективность кэша при одновременной активной работе с несколькими открытыми документами.
  - Планируется на будущее: сделать инвалидацию по-документно — например, партиционировать `CancellationTokenSource` по идентификатору документа из `DocumentChangedEventArgs`/активного `Document`, а не держать один общий токен на все записи.

- **Ключ `CachedElementCollector` завязан на конкретную ссылку на `Document`, а не на "логический" документ.** Ключ кэша использует `RuntimeHelpers.GetHashCode(document)` — идентичность управляемого объекта-обёртки, а не документа как такового. Эмпирически подтверждено (RevitTests): `Element.Document` может вернуть **другой** экземпляр обёртки, чем тот, что был передан в `FilteredElementCollector`/`ICachedElementCollectorFactory.Create(document)`. Практическое следствие: если получить `Document` для одного и того же открытого документа двумя разными путями (например, один раз через `UIDocument.Document`, другой раз — через `element.Document`) и передать в `Create(...)` оба варианта, кэш не переиспользуется между ними — не порча данных, а просто пропущенное попадание в кэш. Рекомендация до исправления: всегда прокидывать в `Create(document)` **одну и ту же** ссылку на `Document` (например, полученную один раз в начале команды), а не переполучать её из разных мест API.
  - Планируется на будущее: заменить идентичность объекта на что-то стабильное на уровне логического документа (например, `Document.PathName`/`Document.Title` в сочетании с флагом "рабочий/несохранённый", если такой ключ окажется надёжнее ссылочной идентичности).

## Поддерживаемые версии Revit

Пакет собирается под версии Revit 2011–2027 (см. конфигурации в `Revit.TransactionMemoryCache.csproj`), таргетируя `net48` для версий до 2025 и `net8.0-windows` для 2025+.

## Требования

- .NET SDK 10.0.103+ (см. `global.json`)
- Revit API (пакет `Revit_All_Main_Versions_API_x64`)
- `VolocyNazad.Revit.Context`

## Бенчмарки

Сравнение производительности `FilteredElementCollector`-запросов к БД Revit с кэшированием через
`IRevitTransactionMemoryCache` и без него — см. `benchmark/`. Запускается вручную внутри живой сессии
Revit (`Nice3point.BenchmarkDotNet.Revit`), поэтому не гоняется в CI.

`Light`/`Medium`/`Complex` — три уровня сложности `FilteredElementCollector`-запроса. Каждый прогоняется с параметром `CallsPerSession` (1/5/20/100) — сколько раз один и тот же запрос запрашивается подряд в рамках одной "транзакции", прежде чем документ изменится и кэш инвалидируется. `Uncached` всегда платит полную цену `CallsPerSession` раз; `Cached` использует свежий ключ на каждый замер, поэтому каждый раз платит ровно за один реальный промах плюс `CallsPerSession − 1` попаданий — так видно, как экономия растёт вместе с числом повторных обращений, а не только предельную стоимость одного уже тёплого hit.

### Вводные

**Модель и данные**
- Один новый проектный документ (`Application.NewProjectDocument(UnitSystem.Metric)`), создаётся заново для каждого класса/комбинации параметров.
- **1000 стен** (`WallCount`), выстроенных в ряд на одном засеянном уровне, в одной транзакции (`OnGlobalSetup` в `CachingBenchmarksBase`).

**Что именно запрашивает каждый уровень сложности**

| Уровень | Запрос |
|---|---|
| Light | `OfClass(typeof(Level))` — один дешёвый фильтр по классу, единственный засеянный уровень. |
| Medium | `OfClass(typeof(Wall)).WhereElementIsNotElementType()` — все 1000 стен, только фильтрация по классу и не-типу. |
| Complex | То же плюс `BoundingBoxIntersectsFilter`, затем в управляемом коде: чтение параметра `CURVE_ELEM_LENGTH` у каждой стены и сортировка по `Id` — трогает геометрию/параметры каждого элемента, а не только строку в таблице элементов. |

**Параметр сессии**
- `CallsPerSession`: **1 / 3 / 5 / 10** — сколько раз подряд запрашивается один и тот же результат в рамках одной "транзакции" до инвалидации кэша (см. предыдущий абзац). Каждое значение — отдельная строка в отчёте (BenchmarkDotNet `[Params]`).

**Поведение кэша**
- Кэш — реальный `RevitTransactionMemoryCache` из `src/`, собранный через `AddRevitContext()` + `AddTransactionMemoryCache()`, тот же путь, что и в проде.
- `SlidingExpiration = 10 минут` на запись (см. `RevitTransactionMemoryCache.GetOrCreate`) — весь прогон занимает секунды, поэтому в рамках одного замера запись никогда не истекает сама по себе; единственный промах — тот, что мы намеренно создаём свежим ключом на каждый замер.
- Инвалидация по `DocumentChanged`/`ViewActivated` в бенчмарке не участвует — `Initialize()` не вызывается, так как это требует `UIControlledApplication`, которого у хоста бенчмарка нет.

**Конфигурация замера (BenchmarkDotNet)**
- `Job.Default` — число вызовов на итерацию (`InvocationCount`), число итераций разминки/замера и т.д. не заданы вручную, движок калибрует их сам (см. ответ про Pilot-стадию выше).
- `MemoryDiagnoser.Default` — включена колонка `Allocated`/`Alloc Ratio`.
- Экспортёры: CSV (`-report.csv`), детальные замеры (`-measurements.csv`), JSON, GitHub-markdown (тот, что попадает сюда).
- Таргет — конфигурация `Release_2025.0.0` (`net8.0-windows`, платформа x64); хост-машина/ОС/версия .NET SDK/BenchmarkDotNet фиксируются автоматически в шапке каждого отчёта ниже.

<!-- benchmark-results:start -->
_Обновлено: 2026-09-05 14:21 (локальный запуск бенчмарков)._

### Light

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
Intel Core Ultra 7 155H 3.80GHz, 1 CPU, 22 logical and 16 physical cores
.NET SDK 10.0.400
  [Host]     : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3 [AttachedDebugger]
  Job-BNKTQO : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3

BuildConfiguration=Release_2025.0.0  

```
| Method   | CallsPerSession | Mean       | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |---------------- |-----------:|---------:|---------:|------:|--------:|----------:|------------:|
| **Uncached** | **1**               |   **363.2 μs** |  **5.03 μs** |  **4.70 μs** |  **1.00** |    **0.02** |     **656 B** |        **1.00** |
| Cached   | 1               |   404.2 μs |  7.84 μs |  6.95 μs |  1.11 |    0.02 |    1360 B |        2.07 |
|          |                 |            |          |          |       |         |           |             |
| **Uncached** | **3**               | **1,227.5 μs** | **23.62 μs** | **22.10 μs** |  **1.00** |    **0.02** |    **1968 B** |        **1.00** |
| Cached   | 3               |   470.2 μs |  9.20 μs | 16.12 μs |  0.38 |    0.01 |    1680 B |        0.85 |
|          |                 |            |          |          |       |         |           |             |
| **Uncached** | **5**               | **2,063.4 μs** | **40.27 μs** | **62.70 μs** |  **1.00** |    **0.04** |    **3280 B** |        **1.00** |
| Cached   | 5               |   439.0 μs |  8.46 μs | 11.30 μs |  0.21 |    0.01 |    2000 B |        0.61 |
|          |                 |            |          |          |       |         |           |             |
| **Uncached** | **10**              | **4,305.8 μs** | **75.61 μs** | **84.04 μs** |  **1.00** |    **0.03** |    **6560 B** |        **1.00** |
| Cached   | 10              |   442.0 μs |  8.47 μs |  9.42 μs |  0.10 |    0.00 |    2800 B |        0.43 |

### Medium

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
Intel Core Ultra 7 155H 3.80GHz, 1 CPU, 22 logical and 16 physical cores
.NET SDK 10.0.400
  [Host]     : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3 [AttachedDebugger]
  Job-BNKTQO : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3

BuildConfiguration=Release_2025.0.0  

```
| Method   | CallsPerSession | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Gen0    | Gen1    | Allocated | Alloc Ratio |
|--------- |---------------- |----------:|----------:|----------:|----------:|------:|--------:|--------:|--------:|----------:|------------:|
| **Uncached** | **1**               |  **1.424 ms** | **0.0484 ms** | **0.1411 ms** |  **1.410 ms** |  **1.01** |    **0.14** |  **7.8125** |  **5.8594** |    **102 KB** |        **1.00** |
| Cached   | 1               |  1.355 ms | 0.0404 ms | 0.1145 ms |  1.324 ms |  0.96 |    0.12 |  7.8125 |  5.8594 | 102.61 KB |        1.01 |
|          |                 |           |           |           |           |       |         |         |         |           |             |
| **Uncached** | **3**               |  **4.090 ms** | **0.0944 ms** | **0.2740 ms** |  **3.999 ms** |  **1.00** |    **0.09** | **23.4375** | **15.6250** |    **306 KB** |        **1.00** |
| Cached   | 3               |  1.268 ms | 0.0333 ms | 0.0945 ms |  1.225 ms |  0.31 |    0.03 |  7.8125 |  5.8594 | 102.92 KB |        0.34 |
|          |                 |           |           |           |           |       |         |         |         |           |             |
| **Uncached** | **5**               |  **7.403 ms** | **0.0840 ms** | **0.0786 ms** |  **7.416 ms** |  **1.00** |    **0.01** | **39.0625** | **31.2500** |    **510 KB** |        **1.00** |
| Cached   | 5               |  1.383 ms | 0.0753 ms | 0.2209 ms |  1.429 ms |  0.19 |    0.03 |  7.8125 |  5.8594 | 103.23 KB |        0.20 |
|          |                 |           |           |           |           |       |         |         |         |           |             |
| **Uncached** | **10**              | **11.652 ms** | **0.2294 ms** | **0.2146 ms** | **11.588 ms** |  **1.00** |    **0.03** | **78.1250** | **62.5000** |   **1020 KB** |        **1.00** |
| Cached   | 10              |  1.221 ms | 0.0350 ms | 0.1004 ms |  1.218 ms |  0.10 |    0.01 |  7.8125 |  5.8594 | 104.02 KB |        0.10 |

### Complex

```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9278/25H2/2025Update/HudsonValley2)
Intel Core Ultra 7 155H 3.80GHz, 1 CPU, 22 logical and 16 physical cores
.NET SDK 10.0.400
  [Host]     : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3 [AttachedDebugger]
  Job-BNKTQO : .NET 8.0.30 (8.0.30, 8.0.3026.36720), X64 RyuJIT x86-64-v3

BuildConfiguration=Release_2025.0.0  

```
| Method   | CallsPerSession | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Gen0     | Gen1     | Allocated  | Alloc Ratio |
|--------- |---------------- |----------:|----------:|----------:|----------:|------:|--------:|---------:|---------:|-----------:|------------:|
| **Uncached** | **1**               |  **3.751 ms** | **0.0743 ms** | **0.0991 ms** |  **3.742 ms** |  **1.00** |    **0.04** |  **19.5313** |  **15.6250** |  **255.73 KB** |        **1.00** |
| Cached   | 1               |  4.533 ms | 0.1995 ms | 0.5725 ms |  4.319 ms |  1.21 |    0.16 |  19.5313 |  15.6250 |  256.42 KB |        1.00 |
|          |                 |           |           |           |           |       |         |          |          |            |             |
| **Uncached** | **3**               | **10.152 ms** | **0.1761 ms** | **0.2028 ms** | **10.177 ms** |  **1.00** |    **0.03** |  **62.5000** |  **46.8750** |   **767.2 KB** |        **1.00** |
| Cached   | 3               |  3.662 ms | 0.0716 ms | 0.1327 ms |  3.629 ms |  0.36 |    0.01 |  19.5313 |  15.6250 |  256.73 KB |        0.33 |
|          |                 |           |           |           |           |       |         |          |          |            |             |
| **Uncached** | **5**               | **16.508 ms** | **0.2091 ms** | **0.1854 ms** | **16.477 ms** |  **1.00** |    **0.02** |  **93.7500** |  **62.5000** | **1278.67 KB** |        **1.00** |
| Cached   | 5               |  3.570 ms | 0.0709 ms | 0.0897 ms |  3.557 ms |  0.22 |    0.01 |  19.5313 |  15.6250 |  257.05 KB |        0.20 |
|          |                 |           |           |           |           |       |         |          |          |            |             |
| **Uncached** | **10**              | **33.797 ms** | **0.4736 ms** | **0.4198 ms** | **33.817 ms** |  **1.00** |    **0.02** | **200.0000** | **133.3333** | **2557.34 KB** |        **1.00** |
| Cached   | 10              |  3.601 ms | 0.0677 ms | 0.0725 ms |  3.613 ms |  0.11 |    0.00 |  19.5313 |  15.6250 |  257.83 KB |        0.10 |

<!-- benchmark-results:end -->

## Лицензия

MIT, см. [LICENSE.md](LICENSE.md).
