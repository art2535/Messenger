# Перенос сервисных контрактов в Application

## Подэтап 2.7.2 — независимые контракты

В `Messenger.Application/Interfaces` перенесены `ICacheService` и `IEncryptionService`. Они не зависят от EF-моделей, ASP.NET типов или других контрактов Core.

Пространство имён временно оставлено `Messenger.Core.Interfaces` для совместимости с существующими потребителями и минимизации риска массового изменения импортов. Это переходный шаг: физическая сборка-владелец контрактов уже Application, логическое переименование namespace ещё предстоит.

Реализации Redis и AES-GCM остаются в Infrastructure, регистрация DI — в API. Сигнатуры интерфейсов не изменены.

## Подэтап 2.7.3 — контракты на DTO

В `Messenger.Application/Interfaces` перенесены:

- `IAccountSettingsService`
- `IAdminService`
- `IBroadcastService`
- `IChatFolderService`
- `IModerationService`

Перед переносом проверены DTO, используемые этими интерфейсами. Они содержат только простые .NET-типы и коллекции; DTO модерации используют стандартные атрибуты `System.ComponentModel.DataAnnotations`. Ссылок на EF-сущности, EF API или ASP.NET-типы в этих пяти контрактах нет.

DTO пока остаются в `Messenger.Core.DTOs`, а namespace интерфейсов — `Messenger.Core.Interfaces`. Это сохраняет текущие импорты и сигнатуры, пока Application зависит от Core. Отдельный этап будет посвящён переносу DTO и переименованию namespace.

## Что остаётся в Core

Остальные сервисные интерфейсы пока используют модели Core и/или платформенные типы. В частности, `IUserService` и `IMessageService` используют `IFormFile`; `IAttachmentService`, `IChatService`, `ILoginService`, `INotificationService`, `IReactionService`, `IPushSubscriptionService`, `IScheduledMessageService` принимают или возвращают Core-модели. Их перенос требует предварительного отделения persistence-моделей и платформенных абстракций.

## Проверки

Проверены сигнатуры пяти интерфейсов и исходники соответствующих DTO. Файлы перемещены без изменения содержимого; реализации и DI-регистрация не менялись. `dotnet build` и тесты не запускались, успешная компиляция не подтверждена.


## Подэтап 2.7.6 — контракты загрузки файлов

`IUserService` и `IMessageService` физически перенесены в `Messenger.Application/Interfaces`. Их namespace пока оставлен `Messenger.Core.Interfaces`, чтобы не менять все существующие импорты потребителей в одном изменении.

Введён `Messenger.Application.Files.IUploadedFile` с метаданными файла и методом `OpenReadStream()`. Application-контракты больше не принимают `Microsoft.AspNetCore.Http.IFormFile`.

На границе API добавлен `FormFileUploadAdapter`, который оборачивает `IFormFile`. `UserService` открывает и асинхронно копирует поток внутри операции, затем освобождает его. Поток не сохраняется за пределами HTTP-запроса. `MessageService` принимает `IUploadedFile[]`; текущая реализация параметр файлов не использует, как и до изменения.

Контракты по-прежнему возвращают Core-модели (`User`, `Role`, `Message` и `ServiceResult<Message>`). Это промежуточный перенос владельца интерфейсов, а не завершение миграции entity-based API.
