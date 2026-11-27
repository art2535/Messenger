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
