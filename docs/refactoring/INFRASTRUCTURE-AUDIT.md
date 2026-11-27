# Аудит Messenger.Infrastructure

## Проверенная ревизия

Аудит выполнен по ветке `refactor/architecture`, HEAD `92e5052babf3dd69f8aecead0f41823c74b2bb34`.

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

### Высокий приоритет: Broadcast Sender — required FK с SetNull

Подтверждено одновременно в:
- `Broadcast.SenderId`: тип `Guid`, не nullable; `Sender` не nullable;
- `BroadcastConfiguration`: `OnDelete(DeleteBehavior.SetNull)` вместе с `.IsRequired()`;
- исходной миграции `20260617210434_Init`: колонка `Sender_ID` создаётся `NOT NULL`, а FK имеет `ON DELETE SET NULL`;
- `GuapMessengerContextModelSnapshot`: связь также помечена как required и SetNull.

Это противоречивая модель данных: при удалении пользователя PostgreSQL попытается записать NULL в NOT NULL колонку, и удаление завершится ошибкой. Конфигурация и миграция не согласованы с выбранной семантикой сохранения рассылки после удаления автора.

**Требуется продуктовое решение до изменения схемы:**
1. Сохранять рассылку без автора — сделать `SenderId` nullable, навигацию nullable и добавить миграцию изменения nullable; либо
2. Запрещать удаление пользователя, пока существуют его рассылки — заменить SetNull на Restrict/NoAction и оформить миграцию FK.

Не менять это автоматически: выбор влияет на историю рассылок, DTO и UI. Нужна проверка всех consumers `SenderId` и политики удаления пользователей.

### Средний приоритет: хрупкая design-time конфигурация DbContext

`GuapMessengerContextFactory` строит путь к конфигурации как `../Messenger.API` относительно текущего рабочего каталога и безусловно загружает `appsettings.Development.json`. Это связывает миграционный tooling с конкретной структурой папок и cwd, не учитывает обычный конфигурационный pipeline приложения и может не найти файл при запуске `dotnet ef` из solution root/CI/контейнера.

Рекомендуемое направление:
- определить базовый каталог от расположения проекта/переданного аргумента, а не от случайного cwd;
- поддержать переменные окружения и стандартную конфигурацию;
- не требовать Development-файл для production/CI design-time сценариев;
- не выводить connection string в логи и сообщения ошибок.

Перед правкой проверить команды миграций в CI и локальные инструкции.

### Низкий приоритет: лишняя поверхность DbContext

У `GuapMessengerContext` есть публичный parameterless constructor и пустой `OnConfiguring`. Контекст штатно получает `DbContextOptions` через DI, а для design-time существует отдельная factory. Проверить, нужен ли parameterless constructor каким-либо tooling/тестам; если нет — удалить его в отдельном безопасном изменении. Пустой `OnConfiguring` можно удалить без изменения конфигурации, но это следует объединить с проверкой design-time сценариев.

### Требует отдельной проверки: NuGet dependencies

В `Messenger.Infrastructure.csproj` есть `Microsoft.AspNetCore.Identity.EntityFrameworkCore` и `Microsoft.Extensions.Identity.Core`. В просмотренных сервисах аутентификация/хеширование выполняются через собственные JWT/BCrypt-компоненты; явного использования Identity DbContext/PasswordHasher в проверенных файлах не обнаружено. Не удалять пакеты до полного поиска по всем исходникам, generated files и build targets. `MassTransit.EntityFrameworkCore` используется для регистрации Inbox/Outbox entities в DbContext и не является очевидно лишним.

## Что не следует делать

- Не переносить Core entities в Infrastructure: они остаются доменными моделями, а EF mapping уже отделён Fluent API.
- Не создавать generic repository для каждого DbSet без конкретной причины.
- Не переносить `Log`, `Login`, `PushSubscription` в Core.
- Не переписывать транзакции на общий Unit of Work поверх одного DbContext без доказанной необходимости.
- Не менять миграции задним числом; исправления схемы оформлять новыми миграциями.

## Следующие подэтапы

1. Принять решение по удалению пользователя и сохранению Broadcast, исправить FK/entity/config/snapshot новой миграцией.
2. Укрепить design-time DbContext factory и проверить миграционные команды в CI.
3. Провести полный usage audit NuGet dependencies, удалить только доказанно неиспользуемые пакеты.
4. Разобрать крупные EF-сервисы по use cases и запросам; выделять специализированные persistence contracts только там, где это снижает связанность.
5. Добавить integration tests для EF mappings, FK delete behavior, транзакций и критичных конкурентных сценариев.

## Ограничения

- В среде нет .NET SDK; `dotnet build`, `dotnet test`, `dotnet ef migrations script` и проверки миграций на PostgreSQL не запускались.
- Изменения схемы в рамках этого аудита намеренно не вносились: требуется выбрать семантику удаления автора Broadcast.
- Просмотр сервисов был направлен на архитектурные границы и наиболее критичные persistence paths; это не заявление о полном построчном аудите каждого метода Infrastructure.
