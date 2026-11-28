# Аудит Messenger.Infrastructure

## Проверенная ревизия

Аудит обновлён по ветке `refactor/architecture`; актуальные изменения DbContext и design-time factory зафиксированы отдельным коммитом.

Проверены:
- `Messenger.Infrastructure.csproj` и прямые ссылки на Core/Application;
- `GuapMessengerContext`, design-time factory и DbSet;
- все текущие Fluent API configurations;
- три persistence-only entity: `Log`, `Login`, `PushSubscription`;
- миграции, связанные с модерацией, scheduled messages, ExternalId и notification deduplication;
- ключевые сервисы доступа к данным, кэша, Redis, JWT, авторизации сессий и рассылок.

Это статический аудит исходников. Он не подтверждает корректность runtime-поведения, SQL-плана или миграций на реальной базе.

## Архитектурная оценка

Границы проектов сейчас выдержаны:
- Infrastructure ссылается только на Core и Application.
- EF Core, Npgsql, Redis и MassTransit EF outbox находятся в Infrastructure.
- Fluent API configurations, DbContext, миграции и persistence-only entities находятся в Infrastructure.
- Core-модели не зависят от EF Core; persistence-only сущности `Log`, `Login`, `PushSubscription` не протекают в Core.
- Application-контракты возвращают DTO/значения, а не EF IQueryable или DbContext.

Большинство сервисов Infrastructure реализуют Application-интерфейсы и работают с `GuapMessengerContext` напрямую. Отдельный EF repository есть для рассылок (`EfBroadcastRepository` / `IBroadcastRepository`). Само по себе отсутствие generic repository не является дефектом: добавлять универсальный CRUD-слой поверх EF Core не рекомендуется. Выделять persistence contracts имеет смысл там, где это изолирует сложные запросы, транзакционные границы или позволяет тестировать use case без EF.

## Найденные проблемы и решения

### Исправлено: Broadcast Sender — nullable FK с SetNull

До исправления модель была противоречивой:
- `Broadcast.SenderId` и навигация `Sender` были обязательными;
- `BroadcastConfiguration` задавала `OnDelete(DeleteBehavior.SetNull)` вместе с `.IsRequired()`;
- исходная миграция `20260617210434_Init` создавала `Sender_ID NOT NULL`, хотя FK имел `ON DELETE SET NULL`;
- snapshot также описывал required-связь с SetNull.

При удалении пользователя PostgreSQL не мог записать NULL в NOT NULL колонку.

**Решение: сохранять рассылку без автора.** Проверка `UserService.DeleteAccountAsync` показала, что аккаунт удаляется напрямую; отдельного правила, запрещающего удаление автора рассылок, нет. Рассылка и её получатели являются самостоятельной исторической записью, поэтому удаление пользователя не должно удалять рассылку или блокировать удаление аккаунта.

Выполнено:
- `Broadcast.SenderId` и навигация `Sender` стали nullable;
- Fluent API сохраняет `DeleteBehavior.SetNull`, но больше не помечает связь обязательной;
- `BroadcastSummaryDto.SenderId` допускает отсутствие автора; существующая проверка доступа оставляет просмотр такой рассылки только администраторам;
- новая миграция `20261127200000_MakeBroadcastSenderOptional` меняет `Broadcasts.Sender_ID` на nullable; исходные миграции не изменялись. Откат возможен только до появления строк с NULL в `Sender_ID` либо после ручного восстановления авторов;
- snapshot и migration target model синхронизированы с nullable FK.

Проверка consumers: создание рассылки по-прежнему требует существующий `senderId`; идентификатор используется в проверке владельца сводки, а UI не требует навигацию Sender. Получатели и их статусы чтения не зависят от наличия автора.

### Исправлено: design-time конфигурация DbContext

`GuapMessengerContextFactory` ищет каталог API вверх от текущего рабочего каталога и `AppContext.BaseDirectory`. JSON-конфигурация API необязательна: если проект или appsettings отсутствуют, factory всё равно может создать контекст при наличии `ConnectionStrings__DefaultConnection`. Переменные окружения имеют наивысший приоритет. Окружение берётся из `DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT`, по умолчанию — Development. Отсутствующая строка подключения приводит к понятной ошибке без вывода секрета.

В текущем `.gitlab-ci.yml` миграции не запускаются: pipeline выполняет architecture guard, а deploy использует Docker Compose. Design-time factory и команды `dotnet ef` в CI пока не проверены запуском.

### Исправлено: минимальная поверхность DbContext

- Удалён публичный parameterless constructor: все штатные runtime-регистрации используют `AddDbContext` с `DbContextOptions`, а EF design-time использует `GuapMessengerContextFactory`, которая также передаёт options.
- Удалён пустой `OnConfiguring`, не добавлявший конфигурации.
- `GuapMessengerContext` теперь создаётся только с явно заданными `DbContextOptions`, что не позволяет случайно получить контекст без провайдера.

### Удалены неиспользуемые Identity NuGet dependencies

Проверены все 22 исходных файла сервисов Infrastructure, Fluent API configurations, DbContext, persistence entities и миграционный слой. Использований `IdentityDbContext`, `UserManager`, `SignInManager`, `PasswordHasher`, Identity user/role types или соответствующих extension methods не обнаружено. Аутентификация реализована собственными JWT-компонентами, хеширование паролей — BCrypt.

Из `Messenger.Infrastructure.csproj` удалены:
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`;
- `Microsoft.Extensions.Identity.Core`.

Пакеты `Microsoft.IdentityModel.Tokens` и `System.IdentityModel.Tokens.Jwt` оставлены: они относятся к используемой JWT-реализации. `MassTransit.EntityFrameworkCore` также оставлен для Inbox/Outbox entities.

Ограничение: удаление пакетов требует подтверждения сборкой и restore в среде с .NET SDK; статический usage audit не заменяет проверку MSBuild targets и транзитивных package assets.

### Исправлено: владельцем push-подписки и уведомления определяется текущий пользователь

- `IPushSubscriptionService.RemoveByEndpointAsync` теперь принимает `userId`, а SQL delete фильтрует одновременно по владельцу и endpoint. Регистрация и отписка из `PushController` передают ID, извлечённый из аутентифицированного principal.
- `INotificationService.MarkAsReadAsync` теперь принимает `userId`; условный UPDATE фильтрует по паре `NotificationId + UserId`. `PushController` больше не может отметить чужое уведомление как прочитанное, даже если его GUID известен.
- Проверка владельца находится в самом Infrastructure-запросе, а не только в контроллере. Для чужого/несуществующего ID операция остаётся безопасным no-op.

Проверка: статически сверены все вызовы обоих изменённых контрактов и фильтры запросов. Требуются integration tests для двух разных пользователей.

### Дополнительно исправлено: конкурентность и владение push-подписками

- Регистрация одного browser endpoint сериализуется PostgreSQL advisory transaction lock; повторная регистрация переносит endpoint текущему владельцу и не оставляет копии у предыдущего аккаунта.
- Удаление и обновление подписки по ID фильтруются по UserId. Обновление не удаляет endpoint другой учётной записи; очистка устаревшей подписки ограничена конкретными Id + UserId.
- Создание настроек push обрабатывает конкурентную вставку по уникальному AccountId. При конфликте сохранения настройки перечитываются и повторно применяются; отключение push и удаление подписок остаются в транзакции.
- Перед push-рассылкой используется actor-aware получение чата и участников; отправитель должен состоять в чате, а его имя берётся из профиля участника, а не из клиентского параметра.

### Дополнительно исправлено: идемпотентность уведомлений и ограничения данных

- Повторная отметка уведомления прочитанным — no-op и не перезаписывает первоначальный ReadAt.
- Получение уведомления по ID теперь требует userId; повторное использование deduplication key другим пользователем отклоняется вместо возврата ID чужого уведомления.
- Добавлена проверка длин полей жалобы, mute и заметки модератора до обращения к ограничениям БД.
- JwtService использует process-level JWT_SECRET_KEY (совместимо с Linux/container deployments) с fallback на конфигурацию; Base64-ключ проверяется на минимальную длину 32 байта до подписи.

### Исправлено: атомарное рассмотрение жалоб модерации

`ReviewReportAsync` ранее повторно объявлял локальную переменную `report` в одной области видимости, что блокировало компиляцию. Простое удаление лишнего чтения устранило бы синтаксическую ошибку, но оставило бы race condition: два администратора могли одновременно прочитать статус `pending`, оба изменить жалобу и создать две записи аудита.

Теперь переход выполняется одним условным `ExecuteUpdateAsync` с предикатом `Status == "pending"`. Только запрос, изменивший одну строку, записывает audit log; изменение статуса и запись аудита находятся в одной транзакции. Повторный или конкурентный review, который не изменил строку, завершается без повторного audit event.

Ограничение: поведение проверено статически; конкурентный сценарий требует PostgreSQL integration test.

## Что не следует делать

- Не переносить Core entities в Infrastructure: они остаются доменными моделями, а EF mapping уже отделён Fluent API.
- Не создавать generic repository для каждого DbSet без конкретной причины.
- Не переносить `Log`, `Login`, `PushSubscription` в Core.
- Не переписывать транзакции на общий Unit of Work поверх одного DbContext без доказанной необходимости.
- Не менять миграции задним числом; исправления схемы оформлять новыми миграциями.

## Переход к тестированию

Создан проект Messenger.Tests и добавлен в solution. Он использует xUnit и Testcontainers с PostgreSQL 17; тесты работают с настоящим Npgsql/EF Core, поскольку конкурентные SQL-сценарии и ExecuteUpdateAsync нельзя надёжно проверить EF InMemory provider.

Добавлены integration tests:
- NotificationServiceTests: owner isolation, стабильность первого ReadAt, конкурентная дедупликация и запрет cross-user deduplication key.
- PushSubscriptionServiceTests: конкурентная регистрация одного endpoint, owner-scoped удаление и запрет присвоения endpoint другой учётной записи.
- ModerationServiceTests: два конкурентных review должны создать ровно одну audit-запись.
- AttachmentServiceTests: повторная доставка одного attachment event идемпотентна; повторное использование ID с другим payload отклоняется.

## Оставшиеся задачи Infrastructure / границы этапа

1. Запустить dotnet test Messenger.Tests/Messenger.Tests.csproj в среде с .NET 10 SDK и Docker; исправить compile/runtime issues, которые выявятся.
2. Добавить интеграционные сценарии для scheduled-message Outbox + SKIP LOCKED, ChatFolder/Reaction concurrency, FK delete behavior и миграций с реальной БД.
3. Проверить dotnet ef migrations script и design-time factory из solution root и каталога Infrastructure; выполнить миграции на чистой тестовой PostgreSQL и на копии текущей схемы.
4. Отдельно согласовать продуктовую семантику: удаление сообщения сейчас каскадно удаляет жалобы; User_Mutes не имеет FK к пользователю/чату. Не менять это автоматически, пока не решено, должна ли сохраняться история модерации.
5. API-уровень, не включённый в текущие Infrastructure-коммиты: POST /notifications позволяет аутентифицированному клиенту передать произвольный UserId; требуется отдельная авторизация/перенос endpoint во внутренний flow. POST /push/send также требует отдельной проверки, что payload соответствует реально созданному сообщению, а не только членству отправителя в чате.
6. RedisDistributedLock при отсутствии Redis использует no-op lock; это допустимо только для single-instance режима. Для multi-instance deployment Redis должен быть обязательным или нужен другой distributed lock provider.

### Добавлено: тесты UserService и EfBroadcastRepository

Добавлены PostgreSQL integration tests:
- конкурентная регистрация одного ExternalId из двух независимых DbContext должна вернуть один UserId и создать одну учётную запись с настройками и статусом;
- нормализация ExternalId, назначение стандартной роли и проверка ограничений входных данных;
- повторное назначение роли не создаёт дубликат связи;
- создание рассылки и получателей, запрет отметки прочтения не-получателем, идемпотентность ReadAt, unread inbox и агрегированная сводка.

### Добавлено: тесты ChatService и MessageService

Добавлены PostgreSQL integration tests для ключевых security-sensitive операций:
- actor-aware чтение чата и проверка членства;
- добавление участника только администратором и запрет выдачи административной роли через обычный метод;
- удаление чата только фактическим владельцем с каскадной очисткой участников;
- отметка прочтения только входящих сообщений и запрет операции неучастнику;
- поиск и закрепление сообщения недоступны постороннему пользователю.

### Добавлено: тесты CacheMetrics

Добавлен `CacheMetricsTests`: `MeterListener` проверяет публикацию hit/miss/error counters и извлечение prefix из cache key, включая fallback `unknown`.

### Добавлено: тесты Admin и Redis fallback

Добавлены тесты для `AdminService`, `RedisUserPresenceTracker`, `RedisCacheService` и `RedisDistributedLock`:
- проверка системной роли по данным PostgreSQL;
- локальный presence fallback: несколько подключений, переходы online/offline, защита от подмены владельца connection ID и cancellation;
- отключённый Redis cache: безопасный miss/no-op и корректная отмена операций;
- no-op lease распределённой блокировки в single-instance fallback и cancellation.

### Добавлено: тесты аккаунтов, сессий, статусов и security utilities

Добавлены тесты для `AccountSettingsService`, `UserStatusService`, `LoginService`, `AesGcmEncryptionService` и `ValidationService`:
- нормализация темы и сохранение пользовательских privacy-настроек;
- конкурентный UPSERT статуса, пакетное обновление только выбранных пользователей;
- хеширование session key, истечение сессии, owner-scoped revoke и учёт `IdleLogout`;
- AES-GCM round-trip, случайный nonce, повреждение ciphertext и некорректные ключи;
- правила пароля, email, даты рождения и проверка BCrypt-хеша.

### Добавлено: интеграционные тесты ChatFolder и Reaction concurrency

Создан `ChatFolderAndReactionTests` на PostgreSQL Testcontainers:
- два независимых DbContext одновременно добавляют один чат в одну папку; проверяется успешная идемпотентная обработка и наличие ровно одной связи `ChatFolderChat`;
- два независимых DbContext одновременно создают разные реакции одного пользователя на одно сообщение; проверяется уникальность пары `MessageId + UserId` и наличие одной итоговой реакции.

Сценарии покрывают гонку между предварительной проверкой существования и записью, где окончательную целостность обеспечивают уникальные индексы PostgreSQL.

### Добавлено: интеграционные тесты ScheduledMessage dispatcher

Создан `ScheduledMessageServiceTests` на PostgreSQL Testcontainers:
- два параллельных dispatcher используют разные DbContext; первый удерживает блокировку строки внутри callback, второй должен пропустить занятую запись через `FOR UPDATE SKIP LOCKED` и обработать другую;
- проверяется, что обе записи в итоге имеют статус `Sent` и ResultMessageId;
- при исключении из publish callback транзакция откатывается, запись остаётся `Pending`, а ResultMessageId не устанавливается.

Тесты проверяют конкурентный захват и транзакционную границу ScheduledMessage. Они не подменяют отдельную проверку фактической доставки MassTransit Bus Outbox: для неё потребуется запуск зарегистрированного outbox dispatcher и проверка OutboxMessages в PostgreSQL.

### Добавлено: тесты Application и Realtime services

Добавлены unit tests для `BroadcastService`: проверка полного набора получателей перед созданием рассылки, owner/admin authorization для сводки, запрет чтения чужой рассылки и передача фильтра unread inbox.

Добавлены rate-limiter tests для `TypingRateLimiterService` и `SendMessageRateLimiterService`: лимиты на пользователя, независимые partition и отмена ожидающего запроса при заполненной очереди. `Messenger.Tests` теперь ссылается на `Messenger.Realtime`.

### Добавлено: тесты FK delete behavior

Добавлен `ForeignKeyDeleteBehaviorTests` на PostgreSQL:
- удаление сообщения каскадно удаляет вложения, реакции и жалобы;
- удаление чата удаляет сообщения, участников и связи с папками, но сохраняет саму пользовательскую папку;
- удаление автора рассылки устанавливает `SenderId = NULL`, сохраняя рассылку и её получателей.

### Добавлено: тесты API background services

Добавлены тесты для `SessionCleanupBackgroundService` и `UploadOrphanCleanupService`:
- неактивный по LastActivity пользователь с живым SignalR presence не переводится Offline и не теряет login-сессию;
- повторная проверка presence обнаруживает reconnect во время очистки, восстанавливает Online и не отзывает сессии;
- orphan cleanup безопасно пропускает работу при некорректном URL API до открытия scope/БД.

`Messenger.Tests` теперь ссылается на `Messenger.API` для тестирования фоновых служб.

### Добавлено: тесты Web token refresh

Дополнительно добавлен `UserTokenStoreTests`: конкурентный `GetOrAdd`, compare-and-remove для устаревшей записи, отзыв всех сессий с общим refresh token и tombstones. Проверен пустой цикл `TokenRefreshBackgroundService` без обращения к API.

Добавлены тесты `TokenRefresher`:
- успешная ротация access/refresh token и обновление срока действия;
- окончательный отказ SSO отзывает сессию и создаёт tombstone;
- уже отозванный tombstone блокирует сетевой запрос;
- конкурентные refresh-запросы одной сессии сериализуются и выполняют ровно один HTTP вызов.

`Messenger.Tests` теперь ссылается на `Messenger.Web`.

## Ограничения проверки

- В рабочей среде нет .NET SDK (dotnet: command not found), поэтому dotnet build и dotnet test не запускались. Наличие Docker runtime для Testcontainers также не подтверждено.
- Миграции и SQL не запускались на PostgreSQL. Статические проверки подтверждают изменения исходников, но не заменяют restore, compilation, integration tests и migration rehearsal.
- Аудит покрывает все основные Infrastructure-сервисы, EF mappings и persistence paths, но не является доказательством отсутствия любых дефектов. Перечисленные выше продуктовые и API-level вопросы оставлены явно, а не скрыты под отметкой «всё исправлено».
