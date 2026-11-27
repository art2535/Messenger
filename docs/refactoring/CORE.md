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

1. Продолжить вынос Fluent-конфигураций для остальных сущностей, затем спроектировать persistence entities в Infrastructure.
2. Перевести Infrastructure repositories/services на persistence entities.
3. Перевести API/Web и Application contracts с прежних EF-моделей на чистые domain/DTO типы.
4. После перевода потребителей удалить EF и JSON атрибуты из Core.
5. Перенести DTO и сервисные интерфейсы в Application, не создавая зависимости Core → Application.
6. Добавить архитектурную проверку, запрещающую EF Core/ASP.NET references и namespaces в Core.

## Важные ограничения

- Нельзя удалять `JsonIgnore` до замены сериализации на DTO: это может раскрыть навигационные графы и изменить API payload.
- Нельзя менять EF-аннотации до полного переноса соответствующего mapping в Fluent API.
- Нельзя генерировать новую миграцию только на основании этого изменения: текущий перенос Log mapping должен сохранять модель БД, но сравнение snapshot/model и проверка миграций требуют .NET SDK.

## Проверки

Проверено чтением актуальных файлов из GitHub: конфигурации Log, ChatFolder, ChatFolderChat, MessageReport, UserMute и ModerationLog вынесены в Infrastructure; соответствующие inline-блоки удалены из контекста; `ApplyConfigurationsFromAssembly` присутствует. Проверка эквивалентности EF model snapshot, `dotnet build` и `dotnet test` не запускались.
