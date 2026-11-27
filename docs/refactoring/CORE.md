# Этап 2 — аудит и очистка Messenger.Core

Документ отражает состояние ветки `refactor/architecture` по мере выполнения этапа 2.

## Состав Core и выявленные связи

В `Messenger.Core/Models` находятся 23 модели. В большинстве одновременно присутствуют:
- свойства предметной области;
- EF-аннотации (`Key`, `Column`, `ForeignKey`, `InverseProperty`, `Table`);
- навигационные свойства EF;
- JSON-аннотации (`JsonIgnore`).

Например, `User`, `Message`, `Chat`, `AccountSetting` и связанные типы содержат навигационные коллекции и EF mapping attributes. Поэтому простое удаление атрибутов или перемещение файлов без перевода потребителей изменило бы EF-модель либо поведение сериализации.

`GuapMessengerContext` содержит 21 DbSet и сейчас конфигурирует сущности через отдельные классы Infrastructure. В нём также есть MassTransit Inbox/Outbox entities.

## Выполнено

### 2.2. Перенос SignalR

Перенесены `ChatHub`, `NotificationHub`, `UserStatusHub` из Core в `Messenger.API/Hubs`. Namespace и импорты обновлены; маршруты `MapHub` сохранены.

### 2.3–2.6. Вынос EF mapping

Fluent-конфигурации всех сущностей вынесены в отдельные `IEntityTypeConfiguration<T>` классы Infrastructure. Сохранены существующие имена таблиц, колонок, ключей, индексов и FK constraints, SQL defaults, типы данных, правила удаления и seed-данные. В `OnModelCreating` остались настройки PostgreSQL extension, MassTransit Inbox/Outbox, вызов `ApplyConfigurationsFromAssembly` и partial hook.

Это перенос только Fluent mapping. Типы сущностей, EF-аннотации и навигационные свойства пока остаются в Core; разделение Domain и Persistence не завершено.

### 2.5. Первичное удаление зависимостей

Из `Messenger.Core.csproj` ранее были удалены прямые ссылки на JwtBearer и EntityFrameworkCore. Последующий аудит показал, что это было преждевременно: Core всё ещё содержит EF-аннотации в моделях. Зависимость `IFormFile` была устранена при переносе `IUserService` и `IMessageService` в Application на подэтапе 2.7.6.

### 2.7. Аудит границы Domain / Persistence

Проведён аудит моделей Core, DbContext, EF Model Snapshot, сервисных интерфейсов и ключевых реализаций Infrastructure/API. Выявлено, что сервисные контракты возвращают Core EF-модели, Infrastructure использует их напрямую через DbSet и навигации, а API/Web читают навигационные свойства и сериализуют результаты. Также Core содержит EF-аннотации и ASP.NET типы в интерфейсах.

Подробная матрица типов, найденные зависимости, целевая схема, порядок миграции и критерии приёмки описаны в [PERSISTENCE-BOUNDARY.md](PERSISTENCE-BOUNDARY.md). На этом подэтапе кодовую модель DbContext не дублировали: до перевода контрактов это создало бы две конкурирующие модели и не дало бы реального разделения.

### 2.7.1. Временный мост зависимостей Core

В `Messenger.Core.csproj` временно возвращены только те compile-time зависимости, которые фактически ещё используются исходниками:
- `Microsoft.EntityFrameworkCore` — для оставшихся EF Core-аннотаций моделей;
- `Microsoft.AspNetCore.App` — для `IFormFile` в существующих сервисных интерфейсах.

Это не целевая архитектура и не означает, что Core очищен. Мост предотвращает ситуацию, когда удаление PackageReference делает проект несобираемым раньше, чем будут перенесены контракты и модели. Удалить обе ссылки можно только после перевода соответствующих типов и аннотаций в Application/API/Infrastructure.

## Что осталось

1. Перенести оставшиеся сервисные контракты из Core в Application после замены EF-сущностей на прикладные DTO; загрузка файлов уже отвязана от ASP.NET через `IUploadedFile`.
2. Создать persistence entities в Infrastructure и постепенно перевести DbContext и реализации сервисов на них.
3. Продолжить перевод API/Web и SignalR с EF-моделей на DTO/Application contracts; `IUserService` уже возвращает `UserDto`/`RoleDto`.
4. После перевода всех потребителей удалить EF- и JSON-аннотации, а также EF-навигации из Core.
5. Удалить временные EF Core и ASP.NET framework references из Core.
6. Усилить архитектурную проверку: запретить EF Core/ASP.NET references и namespaces в Core.

## Важные ограничения

- Нельзя удалять `JsonIgnore` до замены сериализации на DTO: это может раскрыть навигационные графы и изменить API payload.
- Нельзя менять EF-аннотации до полного переноса соответствующего mapping в Fluent API.
- Нельзя генерировать новую миграцию только на основании выноса конфигураций: перенос существующего Fluent API не должен менять модель БД, но сравнение snapshot/model и проверка миграций требуют .NET SDK.
- Временный мост зависимостей нельзя считать завершением этапа 2: это защитная мера до фактического переноса контрактов и сущностей.

## Проверки

Исходники проверены по GitHub. В этой среде не удалось выполнить `dotnet build`, `dotnet test` или сравнение EF model snapshot; поэтому компиляция и эквивалентность EF-модели пока не подтверждены.


### 2.7.8. DTO-контракт IUserService

`IUserService` и `UserService` больше не возвращают `User` и `Role`. Для чтения пользователей и ролей используются `UserDto` и `RoleDto`, расположенные в Application; EF-сущности преобразуются внутри Infrastructure. API extension `GetCurrentUserAsync` также возвращает `UserDto`. В DTO нет EF-навигаций; `Account` и `Roles` доступны прикладному коду, но исключены из JSON, чтобы сохранить прежнее поведение сериализации.


### 2.7.9. DTO-контракт IMessageService

Методы `IMessageService` больше не принимают и не возвращают EF-сущность `Message`. Отправка возвращает `ServiceResult<MessageDto>`; чтение, поиск и закреплённые сообщения возвращают `MessageDto`. Изменение содержимого и статуса доставки выполняется командами `UpdateMessageContentAsync` и `UpdateDeliveryStatusAsync` по идентификатору. Преобразование EF-сущностей и навигаций в DTO находится в Infrastructure. API-контроллер и MassTransit consumer переведены на DTO/команды. В контракте остаётся `ChatMessageSent`, который ещё требует отдельного архитектурного решения.


### 2.7.10. Интеграционное событие ChatMessageSent

`ChatMessageSent` физически перенесено в `Messenger.Application/Messages`. Namespace `Messenger.Core.Messages` сохранён намеренно, чтобы не менять MassTransit message URN и совместимость уже опубликованных сообщений. Событие по-прежнему использует `AttachmentInfo` из Core; его перенос отложен до миграции `IScheduledMessageService`, который также зависит от этого типа. Публикация через EF Core Bus Outbox, consumer, endpoint и retry policy не изменялись.


### 2.7.11. DTO-контракт IScheduledMessageService

`IScheduledMessageService` физически перенесён в Application и отвязан от EF-сущности `ScheduledMessage`. Для HTTP-чтения используется `ScheduledMessageDto`, для фоновой отправки — `ScheduledMessageDispatchDto` с зашифрованным текстом и исходными данными вложений. EF-сущности и преобразование JSON вложений остаются внутри Infrastructure. `AttachmentInfo` и DTO расписания также физически перенесены в Application; прежние namespace сохранены для совместимости.


### 2.7.12. DTO-контракт IAttachmentService

`IAttachmentService` физически перенесён в Application. Его методы используют `CreateAttachmentDto` и `AttachmentDto`, не раскрывая EF-сущность `Attachment`. API-контроллер и consumer передают нейтральные DTO; EF mapping остаётся в Infrastructure. Namespace интерфейса и DTO сохранены для минимизации изменений потребителей.


### Пространства имён после переноса контрактов

Перенесённые в Application интерфейсы, DTO и сообщения используют namespaces `Messenger.Application.*`; обратные совместимые namespaces `Messenger.Core.*` для них больше не сохраняются. Сами файлы находятся только в `Messenger.Application`. В Core остаются лишь типы, которые ещё не переносились. Для события `ChatMessageSent` исходный MassTransit URN сохранён атрибутом `MessageUrn`.
