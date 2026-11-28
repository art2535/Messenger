# Этап 15 — Backend Testing

Дата реализации: 2026-11-28  
Рабочая ветка: refactor/architecture

## Что добавлено

### 15.1 Unit-тесты Application

Добавлен отдельный набор UtcDateTimeTests для сохранения UTC-значения и Kind, трактовки Unspecified как UTC без сдвига ticks, преобразования Local в UTC и nullable-перегрузки. Существующие тесты BroadcastService и FileValidationService сохранены.

### 15.2 Unit-тесты Infrastructure

Существующие наборы тестов сервисов Infrastructure сохранены. Аудит выявил покрытие критичных сценариев доступа, идемпотентности, валидации, шифрования, сессий и обработки ошибок. Интеграционные тесты на PostgreSQL/Redis не учитываются как unit-тесты.

### 15.3 PostgreSQL integration tests

Сохранены Testcontainers-тесты сервисов, проверка поведения внешних ключей и отдельная миграционная проверка полного пути MigrateAsync. Обычная PostgreSQL-фикстура по-прежнему использует EnsureCreatedAsync; миграции проверяются отдельной фикстурой, чтобы не смешивать два сценария.

### 15.4 Redis integration tests

Сохранены реальные Redis Testcontainers-сценарии для кэша, rate limiting, распределённой блокировки, presence, heartbeat, конкурентных переходов и восстановления после недоступности Redis.

### 15.5 API contract tests

Добавлен ChatFoldersControllerContractTests: проверяются версия и маршрут контроллера, обязательная авторизация, статус 404 при отсутствии папки и статус 401 при отсутствии внешней identity. Это controller-level contract coverage; полный OpenAPI snapshot/diff и end-to-end HTTP контракт всего API в данный объём не включены.

### 15.6 SignalR integration tests

Добавлен ChatHubTransportIntegrationTests. Тест поднимает реальный SignalR endpoint на TestServer, устанавливает клиентское Long Polling соединение с тестовой аутентификацией и вызывает production hub-метод TryAcquireSendMessagePermit, проверяя partition key и лимит.

### 15.7 RabbitMQ integration tests

Добавлены RabbitMQ Testcontainers fixture/collection и RabbitMqTransportIntegrationTests. Тест запускает MassTransit RabbitMQ transport, публикует сообщение и ожидает его фактического получения consumer-ом. Проверяется транспортная доставка и сериализация сообщения, а не только вызов consumer напрямую.

### 15.8 Concurrency tests

Сохранены существующие конкурентные проверки: конкурентная регистрация пользователя; идемпотентная отправка сообщений и обработка вложений; конкурентная диспетчеризация запланированных сообщений; конкурентное получение Redis distributed lock; параллельные connect/disconnect presence и гонка heartbeat/disconnect. Новые дублирующие тесты на те же инварианты не добавлялись.

### 15.9 Coverage audit

Добавлен coverlet.collector и Messenger.Tests/coverlet.runsettings для Cobertura-отчёта по Application, Infrastructure, API и Realtime. Добавлен GitHub Actions workflow .github/workflows/backend-tests.yml, который восстанавливает зависимости, собирает проект, выполняет весь тестовый проект и сохраняет TRX/Cobertura artifacts.

## Запуск локально

    dotnet restore Messenger.Tests/Messenger.Tests.csproj
    dotnet test Messenger.Tests/Messenger.Tests.csproj --configuration Release --collect:"XPlat Code Coverage" --settings Messenger.Tests/coverlet.runsettings

Для интеграционных тестов необходим доступный Docker Engine. PostgreSQL, Redis и RabbitMQ поднимаются тестовыми Testcontainers fixtures.

## Границы результата

Код и конфигурация добавлены в репозиторий, но успешный запуск .NET сборки/тестов в среде внесения изменений не подтверждён. Итоговый статус CI и фактический процент покрытия должны быть зафиксированы после первого запуска workflow или локальной команды. Не следует считать этап полностью верифицированным до зелёного прогона.
