# Перенос сервисных контрактов в Application

## Подэтап 2.7.2 — независимые контракты

В `Messenger.Application/Interfaces` перенесены `ICacheService` и `IEncryptionService`. Их namespace временно оставлены `Messenger.Core.Interfaces` для совместимости.

## Подэтап 2.7.3 — контракты на DTO

В Application перенесены `IAccountSettingsService`, `IAdminService`, `IBroadcastService`, `IChatFolderService` и `IModerationService`. DTO на тот момент оставались в Core.

## Подэтап 2.7.6 — контракты загрузки файлов

`IUserService` и `IMessageService` физически перенесены в Application. Для файлов введён нейтральный `IUploadedFile`; API адаптирует `IFormFile` через `FormFileUploadAdapter`. Namespace интерфейсов пока сохранены для совместимости.

## Подэтап 2.7.7 — перенос независимых DTO и ответов

В `Messenger.Application/DTOs` перенесены `UpdateUserProfileRequest`, `UserSearch`, `MessageDto`, `MessageSearchResponse`, `ChatExportResult` и `ServiceResult<T>`. Прежние namespace сохранены для совместимости.

`ServiceResult<T>` теперь наследуется от общего `ApiResponse<T>` и использует единый набор свойств: `IsSuccess`, `Data`, `Error`, `ErrorCode`, `InnerError`, `Message` и `Meta`. Все имена свойств соответствуют PascalCase. Поле `InnerError` добавлено в `ApiResponse<T>`, чтобы не терять диагностическую информацию старого результата сервиса. Фабрики `Success` и `Failure` сохранены.

## Подэтап 2.7.8 — DTO-контракт пользователя

`IUserService` и `UserService` больше не возвращают EF-сущности `User` и `Role`. Для чтения, поиска по external ID, регистрации и списка ролей используются `UserDto` и `RoleDto`, физически расположенные в Application и временно сохраняющие namespace `Messenger.Core.DTOs.Users`. EF-запросы и преобразование сущностей остаются внутри Infrastructure.


## Подэтап 2.7.9 — DTO-контракт сообщений

Методы чтения `IMessageService` переведены на `MessageDto`, включая отправку, постраничное чтение, получение по ID, поиск и закреплённые сообщения. DTO формируется в Infrastructure из EF-сущностей; текст расшифровывается там же, EF-навигации не выходят за границу Persistence. Изменение текста и статуса доставки выполняется отдельными командами по идентификатору, а не передачей EF-сущности. API-контроллер и MassTransit consumer используют DTO и команды обновления.

## Подэтап 2.7.10 — интеграционное событие ChatMessageSent

`ChatMessageSent` физически перенесено из Core в `Messenger.Application/Messages`. Его CLR namespace `Messenger.Core.Messages` намеренно сохранён: MassTransit использует идентификатор типа сообщения, производный от namespace и имени CLR-типа. Сохранение namespace позволяет не менять message URN и не создавать несовместимость для уже опубликованных сообщений в RabbitMQ. Контракт по-прежнему использует `AttachmentInfo` из Core; перенос этого типа отдельно невозможен до отвязки `IScheduledMessageService` от Core, поскольку сам интерфейс находится в Core.

Событие публикуется через `IPublishEndpoint` с включённым EF Core Bus Outbox и обрабатывается `ChatMessageSentConsumer`. Перенос не меняет поля, сериализацию, consumer, endpoint, retry policy или порядок публикации.

## Подэтап 2.7.11 — контракт запланированных сообщений

`IScheduledMessageService` физически перенесён в Application (namespace `Messenger.Core.Interfaces` временно сохранён для совместимости). Методы создания, чтения и обновления возвращают `ScheduledMessageDto`, а не EF-сущность. Для фоновой отправки введён `ScheduledMessageDispatchDto`, который содержит зашифрованный текст и нейтральный список `AttachmentInfo`; он не используется как HTTP-ответ. Преобразование EF-сущности, расшифровка текста и чтение JSON вложений выполняются внутри Infrastructure. `AttachmentInfo` и `ScheduledMessageDtos.cs` физически перенесены в Application с сохранением прежних namespace.

## Подэтап 2.7.12 — контракт вложений

`IAttachmentService` физически перенесён в Application (namespace `Messenger.Core.Interfaces` сохранён для совместимости). Добавление принимает `CreateAttachmentDto` и идентификатор сообщения отдельно; чтение возвращает `AttachmentDto`. EF-сущность `Attachment` создаётся и преобразуется только внутри Infrastructure. API-контроллер и MassTransit consumer больше не создают и не передают EF-сущности в сервисный контракт.


## Единые пространства имён перенесённых контрактов

Все файлы, физически перенесённые в `Messenger.Application`, используют пространства имён по новому проекту: `Messenger.Application.Interfaces`, `Messenger.Application.DTOs` (с соответствующими подпространствами) и `Messenger.Application.Messages`. Старые `using Messenger.Core.*` для этих контрактов заменены или дополнены новыми импортами там, где в Core остаются DTO/интерфейсы с тем же родительским пространством имён. Файлы перенесённых контрактов удалены из `Messenger.Core`; старые пространства имён больше не используются как алиасы совместимости.

Для `ChatMessageSent` сохранён исходный MassTransit message URN `urn:message:Messenger.Core.Messages:ChatMessageSent` через `MessageUrnAttribute`. Это позволяет изменить CLR namespace без изменения идентификатора сообщения в брокере. `Messenger.Application` ссылается на `MassTransit.Abstractions` только ради атрибута контракта.


## Подэтап 2.7.13 — контракт сессий входа

`ILoginService` перенесён в `Messenger.Application.Interfaces`. Методы добавления и чтения используют `CreateLoginDto` и `LoginDto`; обновление активной сессии заменено на команду `CloseLoginAsync(userId, loginId)`. `LoginDto` не содержит токен или его хеш. Infrastructure создаёт и обновляет EF-сущность `Login` и отображает её в DTO. `LoginsController` больше не создаёт и не изменяет EF-сущности напрямую.
