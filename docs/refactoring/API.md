# API baseline

## Transport

- ASP.NET Core Web API
- API versioning: `/api/v{version}/...`
- Scalar/OpenAPI в Development
- 17 controllers
- 83 HTTP endpoint-атрибута по статическому подсчёту controller-файлов

## Основные группы endpoint-ов

- Authorization
- Users
- Chats
- Chat folders
- Messages
- Attachments
- Reactions
- Notifications
- Push
- User statuses
- Scheduled messages
- Broadcasts
- Moderation
- Admin
- Account settings
- Logins
- OAuth proxy

## Аудит API и Realtime

- API composition root разделён на расширения регистрации, middleware и endpoint mapping; дальнейшее дробление `Program.cs` не требуется без появления новой ответственности.
- Контроллеры в основном используют Application interfaces, но остаются крупными и содержат повторяющуюся обработку ошибок; следующий шаг — единый HTTP error contract и отдельные use cases для сложных сценариев.
- Проверка SignalR выявила возможность входа в произвольную группу чата и вызова клиентом серверных методов уведомления. В `ChatHub` теперь проверяется членство для `JoinChat`, `StartTyping` и `StopTyping`; серверные методы `Notify*` удалены из публичного Hub API, поскольку сервер уже отправляет события через `IHubContext`.
- `RequestAndBroadcastUserStatus` теперь разрешает запрос только для себя или пользователя из общего чата.
- `NotificationHub` больше не предоставляет клиенту метод отправки уведомлений произвольному `userId`; внутренний пользователь определяется по `sub` через `IUserService`.
- `UserStatusHub` использует `sub` -> внутренний `UserId` и общий `IUserPresenceTracker`, чтобы не переводить пользователя offline при закрытии только одного из нескольких соединений; метрики соединений учитываются только после успешного добавления в SignalR group.

## Наблюдение для рефакторинга

Контроллеры в текущем состоянии напрямую координируют сервисы и transport-level concerns. Следующий этап должен привести API к модели:

```text
Controller
    ↓
Application command/query
    ↓
Handler / use case
    ↓
Domain + ports
    ↓
Infrastructure
```

Baseline намеренно не меняет endpoint contracts.
