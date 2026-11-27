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

В Core остаются восемь сервисных интерфейсов, которые нельзя безопасно переместить одним файловым переносом. `IUserService` и `IMessageService` уже физически перенесены в Application вместе с нейтральным контрактом файла, но их сигнатуры всё ещё зависят от Core-моделей.

## Карта оставшихся зависимостей

| Контракт | Связь с моделями / платформой | Что требуется до переноса |
|---|---|---|
| `IAttachmentService` | `Attachment` | DTO/команды для создания и чтения вложений |
| `IChatService` | `Chat`, `ChatParticipant` | Модели чтения и команды для чатов/участников |
| `ILoginService` | `Login` | DTO сессии и команды управления сессиями |
| `IMessageService` (уже в Application) | `Message`, `ServiceResult<Message>`, `IUploadedFile`, `ChatMessageSent` | Результат операции без сущности, независимый message contract |
| `INotificationService` | `Notification` | DTO уведомления и команды чтения |
| `IPushSubscriptionService` | `AccountSetting`, `PushSubscription` | DTO настроек и подписки |
| `IReactionService` | `Reaction` | DTO реакции и команды |
| `IScheduledMessageService` | `ScheduledMessage`, `AttachmentInfo` | DTO и команды расписания, отделённые от EF-сущности |
| `IUserService` (уже в Application) | `User`, `Role`, `IUploadedFile` | DTO пользователя/ролей |
| `IUserStatusService` | `UserStatus` | DTO статуса и команды обновления |

## Выявленные сквозные зависимости

### ASP.NET типы в Core-контрактах

`IUserService.UploadAvatarAsync` и `IMessageService.SendMessageAsync` теперь принимают `Messenger.Application.Files.IUploadedFile`, содержащий имя, content type, длину и метод открытия потока. `FormFileUploadAdapter` в API адаптирует HTTP `IFormFile`; ASP.NET-типы больше не входят в эти Application-контракты. `UserService` открывает поток только на время копирования и освобождает его после завершения операции. `MessageService` пока не использует переданный массив файлов; это поведение существовало до миграции.

### Перечисление статуса доставки

`CachedMessageItem` использует `MessageDeliveryStatus`. Перечисление вынесено из каталога EF-моделей в `Messenger.Core.Enums`, сохранив числовые значения Pending=0, Sent=1, Delivered=2, Read=3, Failed=4. Оно пока остаётся в сборке Core: EF-сущность `Message` также использует этот тип, поэтому перенос enum непосредственно в Application создал бы запрещённую обратную ссылку Core -> Application. В модели EF свойство по-прежнему отображается как integer; snapshot содержит `Property<int>("DeliveryStatus")`, поэтому отдельного изменения схемы БД не требуется.

### События и транспортные контракты

`IMessageService` публикует `ChatMessageSent`, а `IScheduledMessageService` использует `AttachmentInfo`. Эти типы расположены в `Messenger.Core.Messages`. До переноса нужно определить, являются ли они прикладными событиями/контрактами или деталями конкретного транспорта, и проверить их сериализацию и потребителей.

## Безопасный порядок дальнейшей работы

1. Выделить независимые типы: enum статуса доставки, file-upload abstraction, DTO событий и результатов. Не менять числовые значения enum и wire-format событий.
2. (Выполнено в подэтапе 2.7.6) Перевести `IUserService` и `IMessageService` с `IFormFile` на нейтральную абстракцию файла; адаптеры оставить на границе API.
3. Для каждого entity-based интерфейса создать прикладные request/response DTO и определить команды/запросы. Не копировать EF navigation properties в Application DTO.
4. Обновить реализации Infrastructure, API controllers, Web consumers, фоновые службы и DI регистрации по одному вертикальному сценарию.
5. Только после удаления ссылок на Core EF-сущности перемещать соответствующий интерфейс в Application и менять namespace.
6. Удалять временные EF/ASP.NET compile-time зависимости Core лишь после проверки всех оставшихся файлов и успешной сборки решения.

## Ограничения текущего подэтапа

На этом шаге изменены только два контракта, связанные с загрузкой файлов. Их зависимости от EF-моделей Core сохраняются, поэтому остальные восемь entity-based интерфейсов пока остаются в Core.

## Проверки

Проверены исходники десяти оставшихся интерфейсов, `Message`, `User`, `MessageDeliveryStatus`, `ChatMessageSent` и кеширующих DTO. Это статический аудит исходников. `dotnet build` и тесты не запускались; успешная компиляция не подтверждена.
