# Этап 2 — аудит и очистка Messenger.Core

Документ отражает состояние ветки `refactor/architecture` по мере выполнения этапа 2.

## Состав Core и выявленные связи

В `Messenger.Core/Models` сейчас находятся 19 файлов модели/вспомогательного типа; 18 типов участвуют в EF-модели, а `UtcDateTime` является helper. Модели `Log`, `Login` и `PushSubscription` уже вынесены в Infrastructure. `AccountSetting` остаётся в Core как предметная модель настроек аккаунта; её EF mapping вынесен в Fluent-конфигурацию Infrastructure. В большинстве оставшихся EF-моделей одновременно присутствуют:
- свойства предметной области;
- EF-аннотации (`Key`, `Column`, `ForeignKey`, `InverseProperty`, `Table`);
- навигационные свойства EF;
- JSON-аннотации (`JsonIgnore`).

До подэтапа 2.7.23 `User`, `Message`, `Chat` и связанные типы содержали EF mapping attributes и навигационные свойства. В подэтапе 2.7.23 EF-аннотации удалены из `User`, а scalar mapping полностью перенесён в `UserConfiguration`; предметные свойства, навигации и `JsonIgnore` сохранены. Для `AccountSetting` аналогичная очистка выполнена на подэтапе 2.7.22. Остальные модели очищаются отдельно после проверки их конфигураций и потребителей.

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

1. Перенос сервисных контрактов в Application на DTO завершён для всех текущих сервисных интерфейсов.
2. Продолжать аудит оставшихся моделей: технические persistence-only типы переносить в Infrastructure только после проверки потребителей; предметные модели оставлять в Core, вынося EF mapping в Infrastructure. `Log`, `Login` и `PushSubscription` уже перенесены.
3. Продолжить перевод API/Web и SignalR с оставшихся Core/EF-моделей на DTO/Application contracts.
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


### 2.7.13. Контракт ILoginService

`ILoginService` и DTO сессий перенесены в Application. Публичный контракт больше не принимает и не возвращает EF-сущность `Login`; чтение отдаёт безопасный `LoginDto` без токена/хеша. Сущность и её изменения остаются внутри Infrastructure.

`AttachmentDto` перемещён в `Messenger.Application.DTOs.Messages`; Core больше не содержит DTO вложения, используемый контрактами Application.


### 2.7.14. Контракт INotificationService

`INotificationService` и DTO уведомлений перенесены в Application. Core больше не содержит интерфейс сервиса уведомлений или request DTO его API-контракта. EF-сущность `Notification` остаётся persistence-моделью Core, но не выходит через Application-контракт.


### 2.7.15. Контракт IReactionService

`IReactionService` и request DTO реакций перенесены из Core в Application. EF-сущность `Reaction` остаётся persistence-моделью Core, но больше не используется публичным контрактом сервиса.


### 2.7.16. Контракт IUserStatusService

`IUserStatusService` физически перенесён из Core в `Messenger.Application.Interfaces`. Вместо EF-сущности `UserStatus` контракт использует `UserStatusDto` и `UpdateUserStatusDto` из `Messenger.Application.DTOs.UserStatuses`. Обновление статуса выполняется по идентификатору пользователя и признаку online, а для обновления времени активности добавлена отдельная операция `UpdateActivityAsync`. EF-сущность `UserStatus` остаётся в Core и используется только внутри `UserStatusService` Infrastructure.

### 2.7.17. Контракт IPushSubscriptionService

`IPushSubscriptionService` физически перенесён из Core в `Messenger.Application.Interfaces`. DTO push-подписок и настроек находятся в `Messenger.Application.DTOs.Push`. Контракт больше не принимает и не возвращает `PushSubscription` или `AccountSetting`; создание подписки использует `userId` и `CreatePushSubscriptionDto`, чтение настроек возвращает `PushSettingsDto`, а обновление подписки использует `UpdatePushSubscriptionDto`. EF-сущности создаются, изменяются и проецируются только внутри Infrastructure. `PushController` использует Application DTO и не создаёт persistence-модели.


## Подэтап 2.7.18 — граница чатов

Контракт `IChatService` больше не находится в Core и не зависит от EF-моделей `Chat`/`ChatParticipant`. Core-модели продолжают использоваться как persistence-модели внутри Infrastructure, но наружу из `ChatService` выходят только DTO Application.


### 2.7.19. Перенос Log в Infrastructure

Техническая модель `Log` перенесена в `Messenger.Infrastructure.Persistence.Entities`. Она не имеет предметных связей и не используется сервисными контрактами. DbContext, Fluent-конфигурация и актуальный Model Snapshot переведены на новый CLR-тип. Таблица `logging.Logs`, колонки, ограничения, PK и индексы сохранены; исторические миграции не менялись.


### 2.7.20. Перенос Login в Infrastructure

Модель сессии `Login` перенесена в `Messenger.Infrastructure.Persistence.Entities`; Core больше не содержит эту EF-сущность и обратную коллекцию `User.Logins`. Infrastructure сохраняет навигацию `Login.User` к оставшейся в Core сущности `User`. Fluent-конфигурация и актуальный Model Snapshot обновлены с сохранением таблицы, колонок, PK, индекса, обязательного FK, каскадного удаления и имени ограничения. Прикладной контракт авторизации продолжает использовать DTO Application; исторические миграции не редактировались.


### 2.7.21. Перенос PushSubscription в Infrastructure

Persistence-модель push-подписки перенесена в `Messenger.Infrastructure.Persistence.Entities`. DbContext, Fluent-конфигурация, сервис Infrastructure и актуальный Model Snapshot используют новый CLR-тип. Обратная коллекция `User.PushSubscriptions` удалена как неиспользуемая; односторонняя навигация `PushSubscription.User` и обязательный FK сохранены. Прикладной контракт `IPushSubscriptionService` и DTO не менялись. Таблица, колонки, PK, индекс, FK, каскадное удаление и исторические миграции сохранены.


### 2.7.22. Аудит AccountSetting и связи с User

`AccountSetting` оставлена в Core: настройки уведомлений, приватности, внешнего вида и поведения аккаунта являются предметными данными, а не технической записью хранения. `User.Account` и `AccountSetting.User` сохранены, так как `UserService` использует эту связь для загрузки/сохранения аватара и формирования пользовательского DTO; сервисы настроек, push и сессий также читают и изменяют `AccountSetting`.

Из `AccountSetting` удалены EF mapping attributes. Полная конфигурация таблицы, колонок, PK, уникального индекса и one-to-one FK перенесена в `AccountSettingConfiguration`. JSON `JsonIgnore` сохранён; навигации не удалялись. Связь конфигурируется в одном месте — в `AccountSettingConfiguration`, а не дублируется в `UserConfiguration`. Схема БД и исторические миграции не менялись; Model Snapshot остаётся с CLR-типом `Messenger.Core.Models.AccountSetting`.


### 2.7.23. Аудит и очистка User

User подтверждена как центральная доменная сущность и остаётся в Messenger.Core.Models. Аудированы её потребители в Infrastructure и API, GuapMessengerContext, конфигурации связанных сущностей и актуальный Model Snapshot.

Из User удалены EF-аннотации [Key], [Column], [StringLength], [ForeignKey] и [InverseProperty]. Скалярный mapping перенесён в UserConfiguration: таблица Users, PK Users_pkey, колонка User_ID, длины и обязательность FirstName/LastName, Login (50), ExternalId (255), Registration_Date и External_ID. Существующие связи с AccountSetting, Chat, ChatParticipant, Message, Notification, Reaction, Role, UserStatus, Broadcast и BroadcastRecipient оставлены и конфигурируются в соответствующих Fluent-конфигурациях.

Все JsonIgnore сохранены, чтобы не менять существующее поведение сериализации. DbSet, CLR-имя сущности в Model Snapshot и схема БД не менялись; исторические миграции не редактировались.


### 2.7.24. Аудит и очистка Role

Role остаётся доменной сущностью Core: роли определяют права и тип доступа пользователя. EF-аннотации Key, Column, StringLength, ForeignKey и InverseProperty удалены; скалярная конфигурация таблицы Roles, ключа Role_ID, длины Name (50) и обязательности Name/Description перенесена в RoleConfiguration. Навигация Users и JsonIgnore сохранены.

Связь User.Roles <-> Role.Users остаётся many-to-many и конфигурируется единообразно в UserConfiguration через join table User_Roles, с исходными именами колонок, PK и FK. Проверены потребители: UserService назначает роли и строит RoleDto, JwtService формирует role claims, AdminService проверяет административную роль, а Web использует claims для авторизации. Seed-данные шести ролей сохранены без изменений.

Model Snapshot и исторические миграции не редактировались; схема и CLR-тип Messenger.Core.Models.Role не менялись.


### 2.7.25–2.7.28. Полный аудит и очистка EF-аннотаций моделей

Проведён полный аудит 18 EF-сущностей в `Messenger.Core/Models`, соответствующих Fluent-конфигураций и актуального ModelSnapshot. EF mapping attributes и EF/DataAnnotations imports удалены из всех 18 моделей. Fluent mapping дополнен явными именами таблиц и колонок, ограничениями строк, типами дат, ключами, индексами, defaults и связями в соответствии с текущей моделью хранения.

Все предметные модели оставлены в Core; `Log`, `Login` и `PushSubscription` остаются в Infrastructure. Предметные свойства, CLR-типы, DbSet, навигации и `JsonIgnore` сохранены. ModelSnapshot и исторические миграции не менялись. Подробный реестр и ограничения проверки находятся в [ENTITY-AUDIT.md](ENTITY-AUDIT.md).

Проверка всех 18 файлов моделей подтвердила отсутствие EF mapping attributes и EF-specific using. Ссылка `Microsoft.EntityFrameworkCore` в Core csproj пока оставлена до отдельного поиска EF-типов во всех исходниках Core. Сборка, тесты и автоматический EF model diff не запускались.
