# Аудит интеграционного тестирования

Дата аудита: 2026-11-28  
Ветка: `refactor/architecture`

## Текущее покрытие

В проекте уже есть отдельные наборы unit- и integration-тестов. Каждый тестовый класс находится в отдельном файле, namespaces соответствуют тематическим каталогам:

- **PostgreSQL:** миграции на чистой БД, соответствие EF snapshot, создание схемы; интеграционные проверки сервисов пользователей, чатов, сообщений, уведомлений, Push-подписок и других инфраструктурных компонентов.
- **Redis:** общий rate limit между экземплярами сервиса, изоляция partition, кэш и TTL, распределённые блокировки, Presence между двумя tracker-инстансами, параллельные connect/disconnect и гонка heartbeat/disconnect.
- **Background services:** очистка сессий, heartbeat Presence, обработка запланированных сообщений и очистка файлов; для orphan cleanup добавлен PostgreSQL integration test с реальным локальным хранилищем.
- **Web:** обновление токенов и хранение пользовательских токенов.

Redis-тесты используют Testcontainers.Redis, PostgreSQL-тесты — Testcontainers.PostgreSql. Это реальные интеграционные тесты с контейнерами, а не только моки.

## Обнаруженные пробелы

### 1. HTTP API

- Есть компонентные HTTP integration tests на TestServer для middleware rate limiting (429 и bypass для non-controller endpoint).
- Полный API host через WebApplicationFactory не поднимается; authentication/authorization, маршрутизация и error contract не проверяются сквозным набором по всем endpoint-ам.
- IDOR и права доступа проверяются в сервисных и отдельных HTTP/Hub тестах, но полного набора сквозных HTTP-проверок пока нет.

### 2. SignalR

- Есть `ChatHubAuthorizationTests`: пользователь, не состоящий в чате, не может присоединиться к группе.
- Полный SignalR transport/test host, reconnect и lifecycle соединения ещё не покрыты.
- Проверки StartTyping/StopTyping, повторной проверки членства, rate limiting и доступа к статусу покрывают не все комбинации; Presence-хранилище протестировано отдельно.

### 3. Отказоустойчивость Redis

- Проверены локальный fallback при отсутствии Redis и распределённое поведение при доступном Redis.
- Добавлены outage/recovery tests: rate limiter fail-closed при настроенном, но недоступном Redis и восстанавливает работу после возврата Redis; Presence heartbeat обновляет локальные lease после восстановления.
- Остаются дополнительные гонки connect/disconnect/heartbeat при длительной недоступности Redis и межпроцессная конкуренция.

### 4. Push и доставка событий

- Проверены права владения Push-подписками, конкурирующая регистрация endpoint и безопасный no-op при отсутствии сообщения.
- Не хватает проверки полного сценария доставки Push для сохранённого сообщения, идемпотентности повторной обработки и обработки ошибок внешнего Push-провайдера.
- Следует отдельно проверить consumer-путь RabbitMQ → сохранённое сообщение → SignalR/Push, не подменяя проверку consumer тестом одного сервиса.

### 5. CI

`.gitlab-ci.yml` содержит test-dotnet job, который запускает полный solution test suite на .NET 10 SDK container. Job требует runner с Docker socket для Testcontainers; фактическое выполнение зависит от доступности такого runner.

## Реализовано в рамках этапа 13.5

- Добавлены HTTP integration tests на TestServer для HTTP rate limiting: отказ 429 на controller endpoint и обход middleware для non-controller endpoint.
- Добавлен тест ChatHub: пользователь, не состоящий в чате, не может присоединить SignalR connection к группе.
- Добавлены тесты Redis outage/recovery: rate limiter отклоняет запрос при потере Redis и снова пропускает после восстановления; Presence heartbeat восстанавливает lease локальных подключений после возврата Redis.
- Добавлен тест consumer: ошибка Push после сохранения сообщения переводит доставку в Failed и повторно выбрасывает исключение для retry брокера.
- GitLab CI дополнен отдельным test job, запускающим весь solution через .NET 10 SDK container; Docker socket передаётся внутрь для Testcontainers.

## Остаточные проверки

- Нужен успешный запуск pipeline на runner с Docker socket и доступом к Docker Hub/NuGet. Без результата pipeline этап нельзя считать подтверждённым по сборке.
- Consumer проверяется напрямую через MassTransit ConsumeContext mock; реальный RabbitMQ transport и broker redelivery не поднимаются. Проверка транспортного retry остаётся инфраструктурной ответственностью MassTransit.
- Тесты требуют Docker на GitLab runner; если runner не предоставляет Docker socket, job должен быть настроен на соответствующий runner.

## Итог этапа 13.5

Покрытие расширено интеграционными и компонентными тестами HTTP middleware, SignalR Hub, Redis outage/recovery и MassTransit consumer. Существующие PostgreSQL/Redis integration tests сохранены и продолжают проверять работу с реальными контейнерами. GitLab CI теперь включает обязательный запуск полного solution test suite до deployment.

## Правила тестирования

- Не дублировать уже существующие тесты Redis/PostgreSQL.
- Для критических сценариев проверять наблюдаемый результат и отсутствие побочных эффектов, а не только вызов метода.
- Не считать тесты пройденными без фактического запуска.
- CI test job выполняется на runner с Docker Engine и доступным `/var/run/docker.sock`; при смене runner эти требования необходимо сохранить.


## Структура тестового проекта

Тесты сгруппированы по слоям, предметным областям и конкретным компонентам:

- Api/ — BackgroundServices (SessionCleanup, ScheduledMessages, LogRetention, Presence), Files (Cleanup, Upload), Messaging, Metrics, RateLimiting.
- Application/ — Broadcasts, Files/Validation.
- Infrastructure/ — Accounts (AccountSettings, UserStatus), Administration, Attachments, Authentication, Caching, Chats (ChatService, MessageService, Folders, Reactions), Database/Migrations, Files/Local, Moderation, Notifications, Push, Redis, Scheduling, Security, Users (UserService, Broadcasts).
- Realtime/ — Authorization, RateLimiting.
- Web/ — Authentication.
- Shared/Fixtures — PostgreSQL/Redis fixtures и xUnit collections.
- Shared/TestDoubles — FakeUploadedFile, StubHandler, NoOpCacheService и вспомогательные типы.

Namespaces повторяют структуру каталогов. Смешанные наборы тестов разделены по тестируемым компонентам; каждый тестовый класс, fixture и test double находится в отдельном файле. При переносе тестовые сценарии и атрибуты коллекций сохранены.

## Дополнительные тесты файлового хранилища

- Application/Files/Validation/FileValidationServiceTests проверяет MIME, сигнатуры, допустимые расширения, ограничения, пустые файлы и некорректные имена.
- Infrastructure/Files/Local/LocalFileStorageTests проверяет запись, чтение, удаление, изоляцию областей, path traversal, лимиты, отмену операций и защиту существующего файла при конфликте имени.
- Api/Files/Cleanup/UploadOrphanCleanupServiceTests проверяет удаление старых orphan-файлов и сохранение ссылок из вложений, аватаров и pending scheduled messages; UploadOrphanCleanupServiceUnitTests проверяет fail-safe сценарии и разбор ссылок.
- Api/Files/Upload/StoredFileNameTests проверяет генерацию серверного имени с owner prefix.

