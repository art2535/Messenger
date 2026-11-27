# Перенос сервисных контрактов в Application

## Подэтап 2.7.2 — первая вертикаль

В `Messenger.Application` перенесены контракты `ICacheService` и `IEncryptionService`. Они не зависят от EF-моделей, ASP.NET типов или других контрактов Core, поэтому подходят для первого изолированного переноса.

Файлы находятся в `Messenger.Application/Interfaces`. Пространство имён временно оставлено `Messenger.Core.Interfaces` для совместимости с существующими потребителями и минимизации риска массового изменения импортов в одном коммите. Это осознанный переходный шаг: физическая сборка-владелец контрактов уже Application, но логическое переименование namespace ещё предстоит.

Реализации `RedisCacheService` и `AesGcmEncryptionService` остаются в Infrastructure. Регистрация DI остаётся в API. Сигнатуры интерфейсов не изменены.

## Что остаётся в Core

Остальные сервисные интерфейсы пока используют DTO Core и/или EF-модели. В частности, `IUserService`, `IMessageService`, `IChatService`, `ILoginService`, `INotificationService`, `IAttachmentService`, `IReactionService`, `IPushSubscriptionService`, `IScheduledMessageService` возвращают или принимают модели Core. `IUserService` и `IMessageService` также используют `IFormFile`.

Их перенос будет выполняться после подготовки Application DTO и нейтрального контракта для файлов, с последующим переводом реализаций и потребителей.

## Проверки

Проверено по исходникам, что оба интерфейса не содержат ссылок на Core-модели и ASP.NET/EF типы, а Infrastructure уже ссылается на Application. `dotnet build` и тесты не запускались; успешная компиляция не подтверждена.
