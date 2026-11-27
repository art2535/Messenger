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
| Messenger.API | Messenger.Application, Messenger.Infrastructure |
| Messenger.Web | Messenger.Core, Messenger.Application |
| Messenger.AppHost | нет |

API получает типы Core транзитивно через Infrastructure.

## Правила направления зависимостей

- Core не ссылается на Application, Infrastructure, API или Web.
- Application ссылается только на Core.
- Infrastructure ссылается на Application и Core.
- API ссылается на Application и Infrastructure.
- Web может ссылаться на Application и Core; бизнес-логика и persistence остаются вне Web.
- AppHost остаётся точкой композиции Aspire и не содержит бизнес-логики.

Автоматическая проверка прямых ссылок: `python3 scripts/check-architecture.py`. Она также запускается в GitLab CI, но не заменяет сборку и тесты.

## Прогресс этапа 2 — очистка Core

Выполнено в commit `eece268b9388f26b99531ffce2fd959b8c3c7155`:

- три SignalR Hub перенесены из `Messenger.Core/Hubs` в `Messenger.API/Hubs`;
- namespace Hub-классов изменён на `Messenger.API.Hubs`;
- регистрация Hub в `Messenger.API/Program.cs` обновлена;
- из `Messenger.Core.csproj` удалены прямые PackageReference на JwtBearer и EntityFrameworkCore.

Что ещё не выполнено:

- часть DTO ещё остаётся в Core; независимые DTO поэтапно переносятся в Application;
- часть сервисных интерфейсов всё ещё находится в Core; entity-based контракты требуют предварительной замены EF-моделей на DTO;
- модели всё ещё совмещают доменные данные, EF mapping attributes и JSON serialization attributes;
- DataAnnotations/EF navigation attributes и JSON attributes ещё не очищены;
- перенос EF entities и конфигураций в Infrastructure ещё впереди.

Подробный аудит и последовательность следующих подэтапов: [CORE.md](CORE.md).

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
