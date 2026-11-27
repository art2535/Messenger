# Перенос сервисных контрактов в Application

## Подэтап 2.7.2 — независимые контракты

В `Messenger.Application/Interfaces` перенесены `ICacheService` и `IEncryptionService`. Их namespace временно оставлены `Messenger.Core.Interfaces` для совместимости.

## Подэтап 2.7.3 — контракты на DTO

В Application перенесены `IAccountSettingsService`, `IAdminService`, `IBroadcastService`, `IChatFolderService` и `IModerationService`. DTO на тот момент оставались в Core.

## Подэтап 2.7.6 — контракты загрузки файлов

`IUserService` и `IMessageService` физически перенесены в Application. Для файлов введён нейтральный `IUploadedFile`; API адаптирует `IFormFile` через `FormFileUploadAdapter`. Namespace интерфейсов пока сохранены для совместимости. Контракты всё ещё используют Core-модели.

## Подэтап 2.7.7 — перенос независимых DTO

В `Messenger.Application/DTOs` перенесены `UpdateUserProfileRequest`, `UserSearch`, `MessageDto`, `MessageSearchResponse`, `ChatExportResult` и `ServiceResult<T>`. Прежние namespace `Messenger.Core.DTOs...` сохранены, чтобы не менять существующие `using` и wire-формат.

`AttachmentDto` выделен в отдельный файл и оставлен в Core: он используется `ScheduledMessageDto`, который пока является частью Core-контракта `IScheduledMessageService`. DTO `MessageDto` из Application использует `AttachmentDto` через существующую ссылку Application -> Core.

`Messenger.Web` получил прямую ссылку на Application, так как веб-проект использует перенесённый `UpdateUserProfileRequest`. API и Infrastructure уже ссылались на Application.
