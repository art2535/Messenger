# Миграция сервисных контрактов, зависящих от моделей Core

## Текущее состояние после этапа 2.7.3

В `Messenger.Application/Interfaces` находятся семь контрактов:

- `ICacheService`
- `IEncryptionService`
- `IAccountSettingsService`
- `IAdminService`
- `IBroadcastService`
- `IChatFolderService`
- `IModerationService`

Их сигнатуры не принимают EF-сущности и не используют ASP.NET типы. DTO для пяти последних контрактов пока физически находятся в Core; их перенос и переименование namespace должны быть отдельным согласованным изменением с обновлением всех потребителей.

В Core остаются десять сервисных интерфейсов, которые нельзя безопасно переместить одним файловым переносом.

## Карта оставшихся зависимостей

| Контракт | Связь с моделями / платформой | Что требуется до переноса |
|---|---|---|
| `IAttachmentService` | `Attachment` | DTO/команды для создания и чтения вложений |
| `IChatService` | `Chat`, `ChatParticipant` | Модели чтения и команды для чатов/участников |
| `ILoginService` | `Login` | DTO сессии и команды управления сессиями |
| `IMessageService` | `Message`, `ServiceResult<Message>`, `IFormFile`, `ChatMessageSent` | Результат операции без сущности, контракт загрузки файлов, независимый message contract |
| `INotificationService` | `Notification` | DTO уведомления и команды чтения |
| `IPushSubscriptionService` | `AccountSetting`, `PushSubscription` | DTO настроек и подписки |
| `IReactionService` | `Reaction` | DTO реакции и команды |
| `IScheduledMessageService` | `ScheduledMessage`, `AttachmentInfo` | DTO и команды расписания, отделённые от EF-сущности |
| `IUserService` | `User`, `Role`, `IFormFile` | DTO пользователя/ролей и контракт загрузки файлов |
| `IUserStatusService` | `UserStatus` | DTO статуса и команды обновления |

## Выявленные сквозные зависимости

### ASP.NET типы в Core-контрактах

`IUserService.UploadAvatarAsync` и `IMessageService.SendMessageAsync` принимают `Microsoft.AspNetCore.Http.IFormFile`. Application не должен требовать ASP.NET HTTP abstraction ради передачи файла в прикладной сценарий.

Перед миграцией этих методов следует ввести нейтральную модель входящего файла (имя, content type, длина и поток/способ чтения) в Application. Адаптацию `IFormFile` следует оставить на границе API. Нужно отдельно проверить жизненный цикл потока и асинхронное чтение, прежде чем менять сигнатуры.

### Перечисление статуса доставки

`CachedMessageItem` использует `MessageDeliveryStatus`. Перечисление вынесено из каталога EF-моделей в `Messenger.Core.Enums`, сохранив числовые значения Pending=0, Sent=1, Delivered=2, Read=3, Failed=4. Оно пока остаётся в сборке Core: EF-сущность `Message` также использует этот тип, поэтому перенос enum непосредственно в Application создал бы запрещённую обратную ссылку Core -> Application. В модели EF свойство по-прежнему отображается как integer; snapshot содержит `Property<int>("DeliveryStatus")`, поэтому отдельного изменения схемы БД не требуется.

### События и транспортные контракты

`IMessageService` публикует `ChatMessageSent`, а `IScheduledMessageService` использует `AttachmentInfo`. Эти типы расположены в `Messenger.Core.Messages`. До переноса нужно определить, являются ли они прикладными событиями/контрактами или деталями конкретного транспорта, и проверить их сериализацию и потребителей.

## Безопасный порядок дальнейшей работы

1. Выделить независимые типы: enum статуса доставки, file-upload abstraction, DTO событий и результатов. Не менять числовые значения enum и wire-format событий.
2. Перевести `IUserService` и `IMessageService` с `IFormFile` на нейтральную абстракцию файла отдельным изменением; адаптеры разместить на границе API.
3. Для каждого entity-based интерфейса создать прикладные request/response DTO и определить команды/запросы. Не копировать EF navigation properties в Application DTO.
4. Обновить реализации Infrastructure, API controllers, Web consumers, фоновые службы и DI регистрации по одному вертикальному сценарию.
5. Только после удаления ссылок на Core EF-сущности перемещать соответствующий интерфейс в Application и менять namespace.
6. Удалять временные EF/ASP.NET compile-time зависимости Core лишь после проверки всех оставшихся файлов и успешной сборки решения.

## Ограничения текущего подэтапа

На этом шаге сигнатуры десяти оставшихся интерфейсов и модели не менялись. Переносить их до подготовки независимых контрактов небезопасно: это создало бы обратные зависимости либо заставило Application ссылаться на persistence-модели.

## Проверки

Проверены исходники десяти оставшихся интерфейсов, `Message`, `User`, `MessageDeliveryStatus`, `ChatMessageSent` и кеширующих DTO. Это статический аудит исходников. `dotnet build` и тесты не запускались; успешная компиляция не подтверждена.
