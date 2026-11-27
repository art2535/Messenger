# Baseline Messenger — этап 0

Дата фиксации: 27 ноября 2026 года.
Исходная ветка: `main`.
Зафиксированный commit: `6524a3a322c8a745fb94f800a6dd0afc205d9c33` (`Слияние веток feature/settings-overhaul и main`).

## 1. Состав решения

На baseline в `Messenger.sln` зафиксировано 5 проектов:

1. `Messenger.Core`
2. `Messenger.Infrastructure`
3. `Messenger.API`
4. `Messenger.Web`
5. `Messenger.AppHost`

Важно: в текущем дереве репозитория отдельный проект `Messenger.Tests` отсутствует, хотя README содержит ссылки на него. Это расхождение фиксируем как baseline-наблюдение и не исправляем на этапе 0.

## 2. Статистика репозитория

По Git tree зафиксированного commit:

| Метрика | Значение |
|---|---:|
| Файлов в Git tree | 380 |
| C#-файлов | 212 |
| Проектов в solution | 5 |
| Domain/model-классов в `Messenger.Core/Models` | 23 |
| REST controllers | 17 |
| REST endpoints по HTTP-атрибутам | 83 |
| SignalR hubs | 3 |
| API background services | 4 |
| Web background services | 1 |
| EF Core migrations | 22 |
| JavaScript-файлов | 29 |

### База данных

`GuapMessengerContext` содержит 21 явный `DbSet` доменных сущностей. Дополнительно EF-модель содержит таблицу связи `User_Roles` и сущности Outbox/Inbox MassTransit. Поэтому при дальнейшем рефакторинге количество физических таблиц нужно считать по актуальной миграции/схеме БД, а не только по количеству `DbSet`.

## 3. Технологический baseline

- .NET 10 / ASP.NET Core
- PostgreSQL 17 + EF Core 10 + Npgsql
- Redis
- RabbitMQ + MassTransit
- SignalR + Redis backplane
- Razor Pages + JavaScript + PWA
- IndexedDB + Background Sync
- OpenTelemetry + Prometheus + Grafana
- Serilog
- .NET Aspire
- Docker Compose + nginx
- GitLab CI

## 4. Текущие зависимости проектов

Фактические ProjectReference:

```text
Messenger.Core
    ↑
Messenger.Infrastructure

Messenger.Core + Messenger.Infrastructure
    ↑
Messenger.API

Messenger.Core
    ↑
Messenger.Web

Messenger.API + Messenger.Web
    ↑
Messenger.AppHost
```

Ключевое архитектурное наблюдение: отдельного `Messenger.Application` пока нет. Кроме того, `Messenger.Core` сейчас содержит зависимости на ASP.NET JWT Bearer и Entity Framework Core. Это не меняем в baseline, но именно это является одним из главных объектов следующего этапа.

## 5. Realtime baseline

Зарегистрировано 3 SignalR Hub:

- `ChatHub`
- `UserStatusHub`
- `NotificationHub`

В API они мапятся через `MapHub<...>` под версионированным API-prefix.

## 6. API baseline

Текущая API-структура включает 17 controllers и 83 HTTP-метода/атрибута маршрутизации, найденных статическим анализом controller-файлов.

API использует:

- `/api/v{version}/...`
- Scalar/OpenAPI
- policy-based authorization
- rate limiting
- health checks
- OpenTelemetry
- Serilog
- SignalR
- RabbitMQ/MassTransit

## 7. Frontend baseline

В `Messenger.Web/wwwroot/js` зафиксировано 29 JavaScript-файлов.

Фронтенд уже разделён по каталогам `auth`, `chats`, `offline`, `settings`, `utils`, но одновременно использует глобальное состояние через `window`. Это фиксируется как предмет последующего frontend-refactoring, а не меняется здесь.

## 8. Offline/PWA baseline

Уже присутствуют:

- IndexedDB;
- offline-кэш;
- outgoing queue;
- Background Sync;
- Service Worker;
- optimistic UI;
- LRU-кэш.

На этапе 0 существующий механизм не переписывается.

## 9. Definition of Done для baseline

Baseline считается зафиксированным, если:

- состояние `main` привязано к конкретному commit;
- создана ветка `refactor/architecture`;
- архитектурные документы baseline добавлены в `docs/refactoring`;
- существующий функционал не изменён;
- результаты проверок, которые нельзя выполнить из GitHub-интеграции, явно помечены как `not run`.

## 10. Проверки

`dotnet build`, `dotnet test`, запуск Aspire и production Compose на удалённом репозитории в рамках этой операции не выполнялись: GitHub-инструмент позволяет читать/изменять Git-объекты, но не предоставляет локальный .NET/Docker runtime.

Это намеренно фиксируется, а не заменяется предположением о прохождении проверок.

## 11. Следующий шаг

После baseline начинаем этап 1:

- создать `Messenger.Application`;
- зафиксировать dependency graph;
- добавить автоматическую архитектурную проверку;
- запретить обратные зависимости.
