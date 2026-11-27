# Миграция сервисных контрактов, зависящих от моделей Core

## Текущее состояние после этапа 2.7.9

`IUserService` переведён с EF-моделей `User`/`Role` на DTO. `IMessageService` также отвязан от EF-сущности `Message`: чтение и закрепление возвращают `MessageDto`, изменение текста и статуса выполняется командами по ID. `ChatMessageSent` выделено в Application как интеграционный контракт; физическое расположение изменено без изменения CLR namespace и MassTransit message URN.

`ServiceResult<T>` в Application унифицирован с `ApiResponse<T>`: он наследует общий формат ответа и использует PascalCase-свойства `IsSuccess`, `Data`, `Error`, `ErrorCode`, `InnerError`, `Message`, `Meta`. `InnerError` добавлен в базовый `ApiResponse<T>` для сохранения прежней диагностики.

## Карта оставшихся зависимостей

| Контракт | Связь с моделями / платформой | Что требуется до переноса |
|---|---|---|
| `IAttachmentService` | `Attachment` | DTO/команды создания и чтения вложений |
| `IChatService` | DTO чатов и участников | Переведён на прикладные DTO; EF-модели остаются внутри Infrastructure |
| `ILoginService` | `Login` | DTO сессии и команды управления сессиями |
| `IMessageService` (Application) | `ChatMessageSent` (интеграционное событие) | Решить, должно ли событие находиться в Application или отдельном Contracts-проекте; проверить MassTransit serialization |
| `INotificationService` | `Notification` | DTO уведомления и команды чтения |
| `IPushSubscriptionService` | `AccountSetting`, `PushSubscription` | DTO настроек и подписки |
| `IReactionService` | `Reaction` | DTO реакции и команды |
| `IScheduledMessageService` (Application) | `ScheduledMessageDto`, `ScheduledMessageDispatchDto`, `AttachmentInfo` | Контракт отвязан от EF-сущности; EF mapping остаётся в Infrastructure |
| `IUserService` (Application) | `UserDto`, `RoleDto` | Контракт отвязан от EF-моделей |
| `IUserStatusService` | `UserStatus` | DTO статуса и команды обновления |

## Сквозные зависимости

`IUploadedFile` отделяет загрузку от ASP.NET. `MessageDeliveryStatus` находится в `Messenger.Core.Enums` и сохраняет числовые значения Pending=0, Sent=1, Delivered=2, Read=3, Failed=4.

## Следующие шаги

1. Перевести оставшиеся entity-based сервисные контракты на DTO/команды, включая `IScheduledMessageService` и его `AttachmentInfo` dependency.
2. Определить дальнейшее разделение интеграционных событий и прикладных команд.
3. После аудита потребителей сменить namespace интерфейсов Application с временных `Messenger.Core.Interfaces`.
4. Удалять временные зависимости Core только после полного аудита и успешной сборки.

## Проверки

Изменения предыдущих подэтапов проверялись статически. Сборка и тесты не запускались; успешная компиляция не подтверждена.


## Результат подэтапа 2.7.9 — IMessageService

Методы чтения и закрепления сообщений переведены на `MessageDto`. `SendMessageAsync` возвращает `ServiceResult<MessageDto>`. Для записи введены операции `UpdateMessageContentAsync` и `UpdateDeliveryStatusAsync`, принимающие идентификатор и необходимые данные вместо EF-сущности `Message`. API и consumer используют эти контракты. EF-сущность и внутренние методы кэширования/пагинации остаются внутри Infrastructure.


## Результат подэтапа 2.7.10 — ChatMessageSent

`ChatMessageSent` перенесено физически в `Messenger.Application/Messages`, при этом namespace `Messenger.Core.Messages` сохранён для совместимости MassTransit. Событие используется API-контроллером, фоновым диспетчером запланированных сообщений, `MessageService` и `ChatMessageSentConsumer`. `AttachmentInfo` оставлен в Core: он также входит в контракт `IScheduledMessageService`, который ещё не отвязан от EF-модели `ScheduledMessage`; Application не может быть ссылкой для Core.


## Результат подэтапа 2.7.11 — IScheduledMessageService

`IScheduledMessageService` физически перенесён в Application и отвязан от EF-сущности `ScheduledMessage`. Пользовательские методы чтения возвращают `ScheduledMessageDto`; фоновый диспетчер получает отдельный `ScheduledMessageDispatchDto` с зашифрованным текстом и исходными данными вложений. `CreateAsync` и `UpdateAsync` возвращают DTO, публичный `ToDto(ScheduledMessage)` удалён. API больше не читает EF-свойства и не расшифровывает данные самостоятельно. `AttachmentInfo` и DTO расписания физически перенесены в Application с сохранением прежних namespace.


## Результат подэтапа 2.7.12 — IAttachmentService

`IAttachmentService` перенесён в Application и больше не принимает/возвращает EF-сущность `Attachment`. Добавление использует `CreateAttachmentDto` и `messageId`, чтение возвращает `AttachmentDto`. Создание EF-модели и отображение полей выполняются в `AttachmentService` внутри Infrastructure. `AttachmentsController` и `ChatMessageSentConsumer` используют DTO-контракт.


## Нормализация пространств имён перенесённых файлов

После физического переноса контракты получили namespaces нового проекта (`Messenger.Application.Interfaces`, `Messenger.Application.DTOs.*`, `Messenger.Application.Messages`). Потребители в API, Infrastructure и Web обновлены на новые namespaces. Физические копии перенесённых файлов в Core отсутствуют; оставшиеся в Core типы DTO и интерфейсы сохраняют собственные прежние namespaces до отдельной миграции.

`ChatMessageSent` теперь объявлен в `Messenger.Application.Messages`, но его MassTransit URN явно закреплён за прежним значением `urn:message:Messenger.Core.Messages:ChatMessageSent`, чтобы не менять wire contract.


## Результат подэтапа 2.7.13 — ILoginService

`ILoginService` перенесён в Application и отвязан от EF-сущности `Login`. Добавление принимает `CreateLoginDto`, чтение возвращает `LoginDto`, завершение сессии выполняется командой `CloseLoginAsync`. DTO чтения не раскрывает токен/хеш. Все EF-операции и mapping остаются в `LoginService` Infrastructure. `LoginsController` использует только DTO и идентификаторы.

`AttachmentDto` также перенесён в `Messenger.Application.DTOs.Messages`; публичные контракты вложений и сообщений больше не зависят от DTO вложения, расположенного в Core.


## Результат подэтапа 2.7.14 — INotificationService

`INotificationService` отвязан от EF-модели `Notification`. `GetNotificationsAsync` и `GetNotificationAsync` возвращают `NotificationDto`; создание сохраняет существующий результат `Guid`, а `MarkAsReadAsync` работает по идентификатору. Mapping и работа с зашифрованным полем `Text` остаются в `NotificationService` Infrastructure. SignalR `NotificationHub` также публикует DTO, а не EF-сущность.


## Результат подэтапа 2.7.15 — IReactionService

`IReactionService` отвязан от EF-модели `Reaction`. `AddReactionAsync` принимает идентификаторы и тип реакции и возвращает `ReactionDto`; `GetReactionsByMessageIdAsync` возвращает `IEnumerable<ReactionDto>`. API больше не создаёт EF-сущность `Reaction`. Mapping, проверка существования сообщения и получение имени пользователя выполняются в Infrastructure.


## Результат подэтапа 2.7.16 — IUserStatusService

`IUserStatusService` физически перенесён в `Messenger.Application.Interfaces` и больше не принимает/возвращает EF-сущность `UserStatus`. Контракт использует `UserStatusDto` и `UpdateUserStatusDto`; `UpdateUserStatusAsync` работает с `userId` и `online`, а `UpdateActivityAsync` обновляет время активности без передачи entity. `UserStatusService` Infrastructure проецирует EF-модель в DTO и создаёт/изменяет `UserStatus` только внутри persistence-слоя. API controller и `UserStatusHub` не создают EF-сущности.

## Результат подэтапа 2.7.17 — IPushSubscriptionService

`IPushSubscriptionService` отвязан от EF-моделей `PushSubscription` и `AccountSetting`. Создание подписки принимает `userId` и `CreatePushSubscriptionDto`, настройки используют `PushSettingsDto`/`UpdatePushSettingsDto`, а обновление подписки — `UpdatePushSubscriptionDto`. `PushSubscriptionService` Infrastructure выполняет mapping и все операции EF внутри persistence-слоя. `PushController` больше не создаёт EF-сущность подписки и не возвращает `AccountSetting`.


## Результат подэтапа 2.7.18 — IChatService

`IChatService` физически перенесён в `Messenger.Application.Interfaces` и больше не принимает/возвращает EF-сущности `Chat` и `ChatParticipant`. Для создания и изменения чата используются `CreateChatDto` и `UpdateChatDto`, чтение возвращает `ChatDto`, `ChatParticipantDto` и `ChatListItemDto`. Идентификаторы участников и чатов передаются отдельно. `ChatService` Infrastructure выполняет все EF-запросы и mapping внутри persistence-слоя; API, SignalR и consumers используют только Application-контракт.
