# Миграция сервисных контрактов, зависящих от моделей Core

## Текущее состояние после этапа 2.7.7

`IUserService` и `IMessageService` уже физически находятся в Application, но их сигнатуры всё ещё используют EF-модели `User`, `Role` и `Message`. В Application также перенесены независимые DTO: `UpdateUserProfileRequest`, `UserSearch`, `MessageDto`, `MessageSearchResponse`, `ChatExportResult` и `ServiceResult<T>`. Их прежние namespace сохранены для совместимости.

`AttachmentDto` оставлен в Core, поскольку его использует `ScheduledMessageDto` в Core-контракте `IScheduledMessageService`. `MessageDto` из Application может ссылаться на него по направлению Application -> Core.

## Карта оставшихся зависимостей

| Контракт | Связь с моделями / платформой | Что требуется до переноса |
|---|---|---|
| `IAttachmentService` | `Attachment` | DTO/команды создания и чтения вложений |
| `IChatService` | `Chat`, `ChatParticipant` | Модели чтения и команды чатов/участников |
| `ILoginService` | `Login` | DTO сессии и команды управления сессиями |
| `IMessageService` (Application) | `Message`, `User` через операции, `ChatMessageSent` | Убрать EF-модели из сигнатур и классифицировать событие |
| `INotificationService` | `Notification` | DTO уведомления и команды чтения |
| `IPushSubscriptionService` | `AccountSetting`, `PushSubscription` | DTO настроек и подписки |
| `IReactionService` | `Reaction` | DTO реакции и команды |
| `IScheduledMessageService` | `ScheduledMessage`, `AttachmentInfo` | DTO и команды расписания, отделённые от EF-сущности |
| `IUserService` (Application) | `User`, `Role` | DTO пользователя/ролей и независимые команды чтения/изменения |
| `IUserStatusService` | `UserStatus` | DTO статуса и команды обновления |

## Сквозные зависимости

`IUploadedFile` отделяет загрузку от ASP.NET. `FormFileUploadAdapter` находится на границе API; `UserService` открывает и освобождает поток в рамках операции. `MessageService` принимает `IUploadedFile[]`, но пока не использует его — это существующее поведение.

`MessageDeliveryStatus` находится в `Messenger.Core.Enums`, сохраняя значения Pending=0, Sent=1, Delivered=2, Read=3, Failed=4. Enum остаётся в Core, поскольку EF-сущность `Message` также использует его.

`IMessageService` публикует `ChatMessageSent`, а `IScheduledMessageService` использует `AttachmentInfo`. До переноса требуется проверить сериализацию и потребителей этих контрактов.

## Следующие безопасные шаги

1. Создать response DTO для `User`, `Role` и `Message`, не копируя EF navigation properties.
2. Перевести реализации Infrastructure и потребителей API/Web на эти DTO вертикально, по одному сценарию.
3. После удаления ссылок на EF-сущности переместить соответствующие интерфейсы и сменить namespace.
4. Удалять временные EF/ASP.NET compile-time зависимости Core только после аудита оставшихся файлов и успешной сборки.

## Проверки

Изменения этапа 2.7.7 проверены статически по сигнатурам и зависимостям. `dotnet build` и тесты не запускались; успешная компиляция не подтверждена.


## Подэтап 2.7.8 — IUserService

`IUserService` переведён с `User`/`Role` на `UserDto`/`RoleDto`; реализация Infrastructure выполняет отображение EF-сущностей внутри `UserService`. Переведены методы получения пользователя по ID и external ID, список пользователей, список ролей и регистрация внешнего пользователя. `UserDto` содержит только scalar-поля, прикладное представление аккаунта и роли; EF navigation properties в контракт не протекают. API extension `GetCurrentUserAsync` также возвращает DTO.

`IMessageService` и другие entity-based интерфейсы пока остаются отдельной задачей: их EF-зависимости будут устранены после проектирования и миграции соответствующих DTO.
