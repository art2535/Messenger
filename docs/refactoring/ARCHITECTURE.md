# Архитектурный baseline и текущее состояние рефакторинга

## Исходное состояние

Baseline зафиксирован на commit `6524a3a322c8a745fb94f800a6dd0afc205d9c33`. Подробная статистика и исходные зависимости — в [BASELINE.md](BASELINE.md).

## Текущее состояние прямых ссылок

Фактические прямые ProjectReference:

| Проект | Прямые ссылки |
|---|---|
| Messenger.Core | нет |
| Messenger.Application | Messenger.Core |
| Messenger.Infrastructure | Messenger.Core, Messenger.Application |
| Messenger.Realtime | Messenger.Application |
| Messenger.API | Messenger.Application, Messenger.Infrastructure, Messenger.Realtime |
| Messenger.Web | Messenger.Application, Messenger.Realtime |
| Messenger.AppHost | нет |

API получает типы Core транзитивно через Infrastructure.

## Правила направления зависимостей

- Core не ссылается на Application, Infrastructure, API или Web.
- Application ссылается только на Core.
- Infrastructure ссылается на Application и Core.
- Realtime ссылается только на Application; ASP.NET Core подключается как shared framework, а не как проектная зависимость.
- API ссылается на Application, Infrastructure и Realtime.
- Web ссылается на Application и Realtime: Web использует `IHubContext<ChatHub>` для отправки realtime-уведомлений. Прямой ссылки на Core сейчас нет; бизнес-логика и persistence остаются вне Web.
- AppHost остаётся точкой композиции Aspire и не содержит бизнес-логики.

Автоматическая проверка прямых ссылок: `python3 scripts/check-architecture.py`. Она также запускается в GitLab CI, но не заменяет сборку и тесты.

## Прогресс этапа 2 — очистка Core

Выполнено в commit `eece268b9388f26b99531ffce2fd959b8c3c7155`:

- три SignalR Hub перенесены из `Messenger.Core/Hubs` в `Messenger.API/Hubs`;
- namespace Hub-классов изменён на `Messenger.API.Hubs`;
- регистрация Hub в `Messenger.API/Program.cs` обновлена;
- из `Messenger.Core.csproj` удалены прямые PackageReference на JwtBearer и EntityFrameworkCore.

Актуальная проверка исходников Core на текущей ветке показывает, что в проекте остались только доменные модели и enum; DTO и прикладные интерфейсы находятся в Application. Все 19 C#-файлов Core проверены на прямые EF Core, ASP.NET Core и Infrastructure references; запрещённых using/type references не обнаружено. В моделях Core также не обнаружены EF mapping или JSON serialization attributes.

Это уточняет исторический статус исходного этапа: перечисленные выше переносы/очистки больше не следует считать открытыми задачами без повторного подтверждения по актуальной ветке. Подробный исторический аудит и план дальнейшего разделения сущностей/маппинга: [CORE.md](CORE.md).

## Ограничения проверок

GitHub-изменения проверены чтением файлов и Git tree. `dotnet build`, `dotnet test`, запуск Aspire и production Compose не выполнялись: в этой среде нет локального .NET/Docker runtime.

## Аудит сервисного слоя

Полный реестр охвата, подтверждённые исправления, открытые дефекты и критерии завершения собраны в [SERVICE-AUDIT.md](SERVICE-AUDIT.md). Документ ведётся по фактическому состоянию ветки и обновляется по мере прохождения сервисов.

## Подэтап backend: композиция Messenger.API

Точка входа `Messenger.API/Program.cs` выделена в отдельный подэтап backend-рефакторинга. Цель — оставить в Program только последовательность запуска, а конфигурацию сгруппировать по назначению:

- регистрации зависимостей и инфраструктуры — `ApiServiceCollectionExtensions`;
- миграции, middleware и endpoint mapping — `ApiApplicationBuilderExtensions`.

При переносе сохраняются порядок middleware, CORS, API versioning, SignalR access token из query string, маршруты hubs, health/metrics/Scalar, JSON serialization, логирование и поведение применения миграций. Это не должно превращаться в изменение бизнес-поведения API.

Критерии завершения подэтапа:
1. `Program.cs` остаётся компактной точкой композиции.
2. Extension-методы имеют одну понятную ответственность и документированы.
3. Порядок регистрации сервисов и middleware подтверждён сравнением с исходной конфигурацией.
4. Выполнены `dotnet build`, тесты и smoke-проверка запуска API в Development и Production.
5. Конфигурация проверена на отсутствие дублирующихся/неиспользуемых регистраций.


## Подэтап backend: аудит DI и проверка границ проектов

Проверена конфигурация регистраций Messenger.API после выделения composition root:

- прикладные сервисы и инфраструктурные реализации регистрируются через AddServices и профильные extension-методы;
- Redis-зависимые реализации кеша и distributed lock выбираются с учётом наличия Redis connection string;
- Redis metrics hosted service добавляется только при зарегистрированном IConnectionMultiplexer;
- health checks включают Redis check только при наличии Redis multiplexer;
- hosted services scheduled messages, upload cleanup, session cleanup, presence heartbeat и log retention подключаются через отдельные методы регистрации;
- отдельной повторной регистрации перечисленных hosted services в ApiServiceCollectionExtensions не обнаружено.

В PostgreSQLExtension.EnsurePoolSettings удалён пустой условный блок, который не выполнял действий и не влиял на итоговые параметры пула.

Автоматический архитектурный guard расширен проектами Messenger.Realtime и Messenger.AppHost. Проверяются все семь проектов, включая API → Realtime, Web → Realtime и отсутствие проектных ссылок у AppHost. Прямая ссылка Web → Realtime подтверждена использованием `IHubContext<ChatHub>` в сценариях входа и выхода; это не случайная транзитивная зависимость. Проверка контролирует только прямые ProjectReference и не анализирует namespace/type-level зависимости или NuGet package references.

Ограничения этапа:
- DI-проверка основана на статическом чтении регистраций; runtime validation контейнера и integration tests не запускались.
- dotnet build/dotnet test не выполнялись в среде без .NET SDK.
- Проверка границ по .csproj не заменяет отдельный аудит EF Core/ASP.NET Core типов в исходниках Core и Application.


## Подэтап backend: аудит границ Core и Application

На ревизии `69b8a2d7b4c4b990b590f1495874a983e0a2f432` выполнен статический аудит исходников и project files двух внутренних слоёв.

- `Messenger.Core`: проверены все 19 C#-файлов и `.csproj`. Прямых ссылок на EF Core, ASP.NET Core и Infrastructure не обнаружено; проект не имеет ProjectReference или PackageReference.
- `Messenger.Application`: проверены DTO, interfaces, messages, files и единственная текущая service implementation (`BroadcastService`), а также `.csproj`. Прямых ссылок на EF Core, ASP.NET Core и Infrastructure не обнаружено. Единственная внешняя package dependency — `MassTransit.Abstractions`, используемая интеграционным контрактом `ChatMessageSent` для стабильного MessageUrn.
- Application ссылается только на Core; Core не ссылается на другие проекты.
- Контракты Application используют собственные DTO и нейтральный `IUploadedFile`, а не `IFormFile` или EF entities.

Архитектурный guard `scripts/check-architecture.py` теперь проверяет не только прямые ProjectReference всех семи проектов, но и запрещённые EF Core / ASP.NET Core / Infrastructure package/source references в Core и Application. Проверка исходников — текстовая, не Roslyn-анализ; она не заменяет проверку compile-time символов, сборку и тесты.

Ограничения:
- Внешняя зависимость MassTransit.Abstractions в Application сохранена намеренно на этом шаге: атрибут `MessageUrn` является частью текущего интеграционного контракта. Её возможное отделение требует отдельного решения по стабильности сериализации/URN и совместимости producer/consumer.
- `dotnet build`, `dotnet test` и запуск guard локально не выполнялись: в рабочей среде отсутствует .NET SDK; Python guard не запускался в этой сессии.
- Следующий этап — аудит EF entities, конфигураций и persistence contracts в Infrastructure, с проверкой реальных consumers перед любым переносом типов.
