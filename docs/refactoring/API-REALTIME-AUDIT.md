# Аудит Messenger.API и Messenger.Realtime

## Область проверки

Статически проверены:
- Program.cs и этапы composition root;
- регистрация DI, middleware, authentication, CORS и rate limiting;
- основные API controller groups: auth, users, chats, messages, attachments, broadcasts, notifications, moderation, admin, scheduled messages, push, reactions, account settings, login и user status;
- SignalR hubs, IUserIdProvider, connection lifecycle и client-callable hub methods;
- health checks, static assets и diagnostics endpoints.

Проверка основана на исходниках ветки refactor/architecture. Это не runtime security test и не полный построчный review каждого endpoint.

## Исправленные проблемы

### 1. Доверие к телу внешнего callback

POST /auth/external/callback принимал ExternalId и Roles из JSON, а endpoint не требовал JWT. UserService.RegisterExternalUserAsync преобразует ROLE_ADMIN в системную роль администратора. Это позволяло подменять идентичность/роли в запросе.

Исправлено:
- endpoint требует [Authorize];
- ExternalId из тела должен совпадать с sub валидированного токена;
- локальный ExternalId берётся из claims токена;
- роли извлекаются из role / ClaimTypes.Role / realm_access.roles проверенного principal; request.Roles игнорируется;
- email и имя также читаются из claims, а не из JSON.

Web IndexModel уже отправляет OIDC access token через ApiHelper, поэтому добавление [Authorize] не меняет штатный поток входа.

### 2. Повышение привилегий через UsersController

POST /users/assign-role/{roleId} был доступен любому authenticated user, а UserService.AssignRoleAsync не выполнял проверку полномочий. Полный список пользователей и lookup по чужому externalId также были доступны всем авторизованным.

Исправлено через IAdminService.IsSystemAdminAsync:
- назначение роли — только системный администратор;
- полный список пользователей — только системный администратор;
- lookup по externalId — сам пользователь или системный администратор.

Проверка использует роль в БД, включая системный RoleId/имя «Администратор», а не только JWT role string.

### 3. Создание массовых рассылок

POST /broadcasts/create требовал только аутентификацию, хотя создаёт массовую рассылку для произвольного набора пользователей. Теперь создание доступно только системному администратору. Проверка статуса рассылки также использует DB-backed IAdminService, а не User.IsInRole("Admin").

### 4. SignalR client-callable methods и доступ к чат-группам

ChatHub содержал публичные Notify* методы, которые SignalR публиковал как методы, вызываемые клиентом. Сервер уже отправляет эти события через IHubContext<ChatHub>, поэтому методы удалены из публичного Hub API.

Дополнительно:
- JoinChat проверяет членство через actor-aware IChatService.GetChatParticipantsAsync(chatId, actorId, ...);
- StartTyping и StopTyping выполняют ту же проверку и нормализуют chat GUID;
- RequestAndBroadcastUserStatus ограничен самим пользователем и участниками общих чатов.

NotificationHub.SendNotification(userId, text) удалён: любой authenticated client мог отправлять уведомление на произвольный userId. Идентификатор пользователя в NotificationHub и UserStatusHub теперь резолвится из sub через IUserService, а не через предположение, что NameIdentifier — GUID.

UserStatusHub использует общий IUserPresenceTracker, чтобы отключение одного SignalR соединения не переводило пользователя offline, пока остаются другие соединения. Учёт connection metrics привязан к фактически зарегистрированному соединению.

### 5. Диагностические ответы

Health checks больше не возвращают exception messages и внутренние data в публичном JSON payload.

## Архитектурные наблюдения

- Program.cs уже является компактным composition root. Регистрация, middleware и endpoint mapping разнесены по extension methods; дальнейшее дробление без новой ответственности не даст пользы.
- Контроллеры в основном зависят от Application interfaces, но часть из них содержит повторяющиеся try/catch и transport mapping. Следующий шаг — единый exception-to-HTTP mapping/ProblemDetails и выделение use cases для сложных сценариев.
- В ApiServiceCollectionExtensions CORS whitelist зафиксирован в коде: localhost и *.dev.localhost.
- HTTP rate limiter и SignalR rate limiter используют локальные in-memory partitions; при нескольких репликах лимиты не общие.
- /metrics опубликован без endpoint authorization. Ограничение доступа должно выполняться на уровне сети или отдельной схемой аутентификации.
- Миграции БД запускаются на старте API. Для нескольких реплик нужен отдельный миграционный шаг либо координация/lock.
- UseStaticAssets публикует только /avatars; вложения /uploads остаются за авторизованным endpoint — это соответствует принятой модели private attachments.

## Оставшиеся риски и задачи

1. **SignalR stale group membership — исправлено на уровне приложения:** при удалении участника активные connection ID извлекаются из Redis presence tracker и удаляются из chat group; `JoinChat` повторно проверяет членство после вступления, закрывая гонку. Операция не атомарна с транзакцией БД, поэтому уже отправленные события не отзываются; необходимо runtime-тестирование с несколькими соединениями и API-репликами.
2. Ограничить /metrics и решить, какие health endpoints доступны публично.
3. Перенести CORS origins в конфигурацию с безопасной валидацией.
4. Решить вопрос распределённого rate limiting для нескольких API instances.
5. Перенести применение EF migrations из каждого API startup в deployment workflow.
6. Добавить отдельные API/Realtime unit и integration tests; в репозитории на момент аудита отдельного test project не обнаружено.

## Проверки

- Проверены статические call sites SignalR: Web вызывает JoinChat, LeaveChat, StartTyping, StopTyping, RequestAndBroadcastUserStatus; удалённые Notify* / SendNotification не являются штатными клиентскими invocations.
- Проверен Web login flow: ApiHelper.PostRawAsync добавляет Bearer access token к auth/external/callback.
- Сборка, тесты, интеграционные проверки JWT и SignalR не запускались: в рабочем окружении отсутствует .NET SDK.
