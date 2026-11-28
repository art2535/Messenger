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
- `UsersController` использует DB-backed `IAdminService.IsSystemAdminAsync`: назначение ролей и полный список пользователей доступны только администраторам; просмотр пользователя по `externalId` разрешён самому пользователю или администратору.
- `BroadcastsController` требует системную роль администратора для создания массовой рассылки; проверка статуса также использует DB-backed проверку, а не только роль из JWT.
- `NotificationHub` больше не предоставляет клиенту метод отправки уведомлений произвольному `userId`; внутренний пользователь определяется по `sub` через `IUserService`.
- `UserStatusHub` использует `sub` -> внутренний `UserId` и общий `IUserPresenceTracker`, чтобы не переводить пользователя offline при закрытии только одного из нескольких соединений; метрики соединений учитываются только после успешного добавления в SignalR group.

### Diagnostics и инфраструктурные ограничения

- /health, /health/live и /health/ready публикуются без авторизации; payload очищен от exception messages и внутренних data, оставляя только общий статус, статус каждой проверки и длительность.
- /metrics также опубликован без endpoint authorization. В production доступ следует ограничить сетью мониторинга или отдельной схемой аутентификации.
- HTTP и SignalR rate limiters используют Redis-backed distributed partitions. Если Redis не настроен, используется локальный fallback; если Redis настроен, но недоступен, лимитер работает fail-closed. Redis outage/recovery покрыт integration tests.
- CORS whitelist сейчас зашит в код и допускает только localhost и *.dev.localhost; production cross-origin Web deployment требует конфигурационного списка origins.
- Миграции БД применяются при старте каждого API процесса. Для горизонтального масштабирования предпочтителен отдельный deployment migration step/lock.

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

В рамках этапа File Storage сохранены основные upload/download сценарии. Из контракта update-profile удалён query-параметр avatarUrl: установка произвольного URL аватара больше не допускается, а frontend обновлён и использует отдельный upload-avatar endpoint.


## Загрузка и выдача файлов

- Вложения сообщений и запланированных сообщений сохраняются через IFileStorage; загрузка ограничена 20 файлами, 10 МБ на файл и 50 МБ суммарно.
- Аватар ограничен 2 МБ; endpoint также ограничивает размер multipart-запроса.
- Допустимые расширения проверяются вместе с сигнатурой содержимого. Content-Type, переданный клиентом, не используется как доверенный MIME.
- uploads не раздаётся статически. Скачивание выполняется через AttachmentsController с проверкой доступа к сообщению и членства в чате; безопасные растровые изображения могут отображаться inline, остальные файлы скачиваются как attachment.
- Файлы удалённых сообщений очищаются фоновой задачей после grace period, чтобы не удалить файлы, на которые ещё ссылаются другие сообщения, отложенные сообщения или transactional outbox.
- Аватары остаются публичными статическими изображениями. Произвольный URL аватара через update-profile больше не принимается: изменение выполняется только через upload-avatar.

