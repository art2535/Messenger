# Этап 2 — аудит и очистка Messenger.Core

Документ отражает состояние ветки `refactor/architecture` по мере выполнения этапа 2.

## Состав Core и выявленные связи

В `Messenger.Core/Models` находятся 23 модели. В большинстве одновременно присутствуют:
- свойства предметной области;
- EF-аннотации (`Key`, `Column`, `ForeignKey`, `InverseProperty`, `Table`);
- навигационные свойства EF;
- JSON-аннотации (`JsonIgnore`).

Например, `User`, `Message`, `Chat`, `AccountSetting` и связанные типы содержат навигационные коллекции и EF mapping attributes. Поэтому простое удаление атрибутов или перемещение файлов без перевода потребителей изменило бы EF-модель либо поведение сериализации.

`GuapMessengerContext` содержит 21 DbSet и сейчас конфигурирует сущности смешанным способом: Fluent API в самом контексте плюс атрибуты на моделях. В нём также есть MassTransit Inbox/Outbox entities.

## Выполнено

### 2.2. Перенос SignalR

Перенесены `ChatHub`, `NotificationHub`, `UserStatusHub` из Core в `Messenger.API/Hubs`. Namespace и импорт Program.cs обновлены; маршруты `MapHub` сохранены.

### 2.3. Первый вынос EF mapping

Создан `Messenger.Infrastructure/Data/Configurations/LogConfiguration.cs`, реализующий `IEntityTypeConfiguration<Log>`.

Из `GuapMessengerContext.OnModelCreating` удалён inline-блок конфигурации `Log`; вместо него включено `modelBuilder.ApplyConfigurationsFromAssembly(typeof(GuapMessengerContext).Assembly)`.

Сохранены настройки таблицы `logging.Logs`, первичного ключа, uuid/default SQL, типов колонок и индексов:
- `IX_logs_timestamp`
- `IX_logs_level`
- `IX_logs_application`

Это перенос только Fluent mapping. Тип `Log` пока остаётся в Core; это промежуточный шаг, а не завершённое разделение Domain и Persistence.


### 2.4. Вынос конфигураций модерации и папок чатов

Из `GuapMessengerContext.OnModelCreating` в отдельные `IEntityTypeConfiguration<T>` классы Infrastructure вынесены конфигурации:
- `ChatFolder` — таблица, ключ, ограничение имени, индекс пользователя и связь с пользователем;
- `ChatFolderChat` — таблица, ключ, уникальный индекс пары Folder/Chat и каскадные связи;
- `MessageReport` — таблица, ключ, явные имена колонок и индекс статуса;
- `UserMute` — таблица, ключ и явные имена колонок;
- `ModerationLog` — таблица, ключ и явные имена колонок.

Содержимое Fluent-конфигураций перенесено без изменения заданных имён таблиц, ключей, индексов, колонок и правил удаления. `ApplyConfigurationsFromAssembly` уже подключён на предыдущем шаге, поэтому новые классы обнаруживаются автоматически. Data annotations в моделях оставлены: они пока остаются частью текущей EF-модели.

### 2.5. Зависимости Core

Из `Messenger.Core.csproj` удалены прямые PackageReference на JwtBearer и EntityFrameworkCore. Сами модели пока всё ещё используют EF-аннотации и требуют дальнейшей миграции.

## Что осталось

1. Провести полный аудит оставшихся EF-аннотаций и связей моделей, спроектировать границу Domain/Persistence и правила преобразования между ними.
2. Создать persistence entities в Infrastructure и постепенно перевести repositories/services на них.
3. Перевести API/Web и Application contracts с прежних EF-моделей на чистые domain/DTO типы.
4. После перевода всех потребителей удалить EF- и JSON-аннотации, а также EF-навигации из Core.
5. Перенести DTO и сервисные интерфейсы в Application, не создавая зависимости Core → Application.
6. Усилить архитектурную проверку: запретить EF Core/ASP.NET references и namespaces в Core.

## Важные ограничения

- Нельзя удалять `JsonIgnore` до замены сериализации на DTO: это может раскрыть навигационные графы и изменить API payload.
- Нельзя менять EF-аннотации до полного переноса соответствующего mapping в Fluent API.
- Нельзя генерировать новую миграцию только на основании выноса конфигураций: перенос существующего Fluent API не должен менять модель БД, но сравнение snapshot/model и проверка миграций требуют .NET SDK.

## Проверки

Проверено чтением актуальных файлов из GitHub: все оставшиеся inline-конфигурации из `GuapMessengerContext` вынесены в Infrastructure, включая девять конфигураций этапа 2.6; все ранее вынесенные конфигурации остаются в каталоге `Configurations`; `OnModelCreating` вызывает `ApplyConfigurationsFromAssembly`. Проверка эквивалентности EF model snapshot, `dotnet build` и `dotnet test` не запускались.

### 2.5. Вынос конфигураций основных сущностей

Вынесены Fluent-конфигурации `Chat`, `ChatParticipant`, `Message`, `Notification`, `Reaction` и `User` в отдельные классы Infrastructure. Сохранены исходные ключи, имена индексов, SQL defaults, типы дат, фильтр индекса закреплённых сообщений, имена FK constraints и поведение удаления. Для `User` отдельно сохранена конфигурация связи один-к-одному с `AccountSetting` и join table `User_Roles` с прежними именами ключей и колонок.

Конфигурации переносят только существующий Fluent API. EF-аннотации в Core-моделях остаются, поэтому этот шаг не завершает отделение домена от EF.

### 2.6. Вынос оставшихся Fluent-конфигураций

В отдельные `IEntityTypeConfiguration<T>` классы Infrastructure перенесены конфигурации `AccountSetting`, `Attachment`, `Login`, `Role`, `UserStatus`, `Broadcast`, `BroadcastRecipient`, `PushSubscription` и `ScheduledMessage`. В `RoleConfiguration` перенесены все шесть записей `HasData` с исходными GUID, названиями и описаниями.

Сохранены заданные в контексте ключи и их имена, `ValueGeneratedNever`, индексы, имена FK constraints, правила удаления, SQL defaults (`gen_random_uuid()`, `now()`), имена/типы колонок, ограничения длины и таблица `Scheduled_Messages`. Аннотации моделей, включая `[Table]`, `[Column]`, `[ForeignKey]`, `[InverseProperty]` и `[JsonIgnore]`, пока не удалялись: они остаются частью действующей EF-модели.

После переноса `OnModelCreating` содержит только настройки расширения PostgreSQL, MassTransit Inbox/Outbox и вызов `ApplyConfigurationsFromAssembly`, а также partial hook.
