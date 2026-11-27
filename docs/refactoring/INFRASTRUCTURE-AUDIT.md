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

## Следующие подэтапы

1. Провести полный usage audit NuGet dependencies, удалить только доказанно неиспользуемые пакеты.
2. Проверить design-time factory командами `dotnet ef` из solution root и каталога Infrastructure, включая запуск только с `ConnectionStrings__DefaultConnection`; добавить проверку в CI при наличии SDK и безопасной тестовой БД.
3. Разобрать крупные EF-сервисы по use cases и запросам; выделять специализированные persistence contracts только там, где это снижает связанность.
4. Добавить integration tests для EF mappings, FK delete behavior, транзакций и критичных конкурентных сценариев.

## Ограничения

- В среде нет .NET SDK; `dotnet build`, `dotnet test`, `dotnet ef migrations script` и проверки миграций на PostgreSQL не запускались.
- Миграция nullable FK добавлена вручную по текущему snapshot, но не сгенерирована через `dotnet ef` и не применялась к PostgreSQL: в среде отсутствует .NET SDK/runtime.
- Просмотр сервисов был направлен на архитектурные границы и наиболее критичные persistence paths; это не заявление о полном построчном аудите каждого метода Infrastructure.
