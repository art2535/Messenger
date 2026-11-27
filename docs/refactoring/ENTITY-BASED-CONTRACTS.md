# Миграция сервисных контрактов, зависящих от моделей Core

## Текущее состояние после этапа 2.7.9

`IUserService` переведён с EF-моделей `User`/`Role` на DTO. `IMessageService` также отвязан от EF-сущности `Message`: чтение и закрепление возвращают `MessageDto`, изменение текста и статуса выполняется командами по ID. В контракте остаётся событие `ChatMessageSent`, его назначение и место размещения требуют отдельного решения.

`ServiceResult<T>` в Application унифицирован с `ApiResponse<T>`: он наследует общий формат ответа и использует PascalCase-свойства `IsSuccess`, `Data`, `Error`, `ErrorCode`, `InnerError`, `Message`, `Meta`. `InnerError` добавлен в базовый `ApiResponse<T>` для сохранения прежней диагностики.

## Карта оставшихся зависимостей

| Контракт | Связь с моделями / платформой | Что требуется до переноса |
|---|---|---|
| `IAttachmentService` | `Attachment` | DTO/команды создания и чтения вложений |
| `IChatService` | `Chat`, `ChatParticipant` | Модели чтения и команды чатов/участников |
| `ILoginService` | `Login` | DTO сессии и команды управления сессиями |
| `IMessageService` (Application) | `ChatMessageSent` (интеграционное событие) | Решить, должно ли событие находиться в Application или отдельном Contracts-проекте; проверить MassTransit serialization |
| `INotificationService` | `Notification` | DTO уведомления и команды чтения |
| `IPushSubscriptionService` | `AccountSetting`, `PushSubscription` | DTO настроек и подписки |
| `IReactionService` | `Reaction` | DTO реакции и команды |
| `IScheduledMessageService` | `ScheduledMessage`, `AttachmentInfo` | DTO и команды расписания, отделённые от EF-сущности |
| `IUserService` (Application) | `UserDto`, `RoleDto` | Контракт отвязан от EF-моделей |
| `IUserStatusService` | `UserStatus` | DTO статуса и команды обновления |

## Сквозные зависимости

`IUploadedFile` отделяет загрузку от ASP.NET. `MessageDeliveryStatus` находится в `Messenger.Core.Enums` и сохраняет числовые значения Pending=0, Sent=1, Delivered=2, Read=3, Failed=4.

## Следующие шаги

1. Отделить `ChatMessageSent` от Core-моделей и определить границу интеграционных событий.
2. Перевести оставшиеся entity-based сервисные контракты на DTO/команды.
3. После аудита потребителей сменить namespace интерфейсов Application с временных `Messenger.Core.Interfaces`.
4. Удалять временные зависимости Core только после полного аудита и успешной сборки.

## Проверки

Изменения предыдущих подэтапов проверялись статически. Сборка и тесты не запускались; успешная компиляция не подтверждена.


## Результат подэтапа 2.7.9 — IMessageService

Методы чтения и закрепления сообщений переведены на `MessageDto`. `SendMessageAsync` возвращает `ServiceResult<MessageDto>`. Для записи введены операции `UpdateMessageContentAsync` и `UpdateDeliveryStatusAsync`, принимающие идентификатор и необходимые данные вместо EF-сущности `Message`. API и consumer используют эти контракты. EF-сущность и внутренние методы кэширования/пагинации остаются внутри Infrastructure.
