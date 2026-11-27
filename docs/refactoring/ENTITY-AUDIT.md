# Аудит и очистка сущностей Core

Актуальное состояние ветки `refactor/architecture`. Аудит охватывает все 18 EF-сущностей из `Messenger.Core/Models`, их Fluent-конфигурации и актуальный ModelSnapshot.

## Итог

**18 из 18 сущностей очищены от EF-аннотаций.**

| Сущность | Размещение | Состояние |
|---|---|---|
| AccountSetting | Core | Очищена ранее, mapping в AccountSettingConfiguration |
| Attachment | Core | Очищена, mapping в AttachmentConfiguration |
| Broadcast | Core | Очищена, mapping в BroadcastConfiguration |
| BroadcastRecipient | Core | Очищена, mapping в BroadcastRecipientConfiguration |
| Chat | Core | Очищена, mapping в ChatConfiguration |
| ChatFolder | Core | Очищена, mapping в ChatFolderConfiguration |
| ChatFolderChat | Core | Очищена, mapping в ChatFolderChatConfiguration |
| ChatParticipant | Core | Очищена, mapping в ChatParticipantConfiguration |
| Message | Core | Очищена, mapping в MessageConfiguration |
| MessageReport | Core | Очищена, mapping в MessageReportConfiguration |
| ModerationLog | Core | Очищена, mapping в ModerationLogConfiguration |
| Notification | Core | Очищена, mapping в NotificationConfiguration |
| Reaction | Core | Очищена, mapping в ReactionConfiguration |
| Role | Core | Очищена, mapping в RoleConfiguration |
| ScheduledMessage | Core | Очищена, mapping в ScheduledMessageConfiguration |
| User | Core | Очищена, mapping в UserConfiguration |
| UserMute | Core | Очищена, mapping в UserMuteConfiguration |
| UserStatus | Core | Очищена, mapping в UserStatusConfiguration |

`UtcDateTime` — вспомогательный класс, не EF-сущность. Технические persistence-only сущности `Log`, `Login` и `PushSubscription` находятся в Infrastructure.

## Что сделано в ходе 2.7.25–2.7.28

- Проверены все 18 Core entities, их соответствующие конфигурации и актуальный `GuapMessengerContextModelSnapshot`.
- Удалены EF mapping attributes и ненужные `System.ComponentModel.DataAnnotations*` / `Microsoft.EntityFrameworkCore` imports из всех 18 моделей.
- Fluent-конфигурации дополнены явными именами таблиц и колонок, длинами строк, обязательностью, типами дат, ключами, индексами, SQL defaults и связями там, где это требовалось для замены атрибутов.
- Сохранены предметные свойства, типы, значения и навигационные свойства. `JsonIgnore` не удалялся.
- Сохранены существующие DbSet и CLR-типы `Messenger.Core.Models.*`; ModelSnapshot и исторические миграции не редактировались.
- Вспомогательные сущности чатов, уведомления, статусы, рассылки и модерация очищены отдельными логическими наборами.

## Связи, которые сохранены

- Chat — User (создатель), ChatParticipant, Message.
- ChatParticipant — Chat и User.
- Message — Chat, Sender, Attachment, Reaction.
- Attachment — Message.
- Reaction — Message и User.
- Notification — User.
- Broadcast — Sender и BroadcastRecipient.
- BroadcastRecipient — Broadcast и User.
- ChatFolder — User и ChatFolderChat.
- ChatFolderChat — Chat и ChatFolder.
- ScheduledMessage — Chat и Sender.
- UserStatus — User (one-to-one).
- MessageReport — Message.
- AccountSetting — User (one-to-one).
- User — Role (many-to-many) и остальные существующие предметные коллекции.

Навигации не удалялись: в рамках очистки mapping это позволило не менять потребителей и поведение загрузки/сериализации. Их дальнейшее устранение — отдельная задача по переводу Infrastructure на persistence entities и DTO, а не механическая часть удаления атрибутов.

## Особые замечания по mapping

- У `Broadcast.SenderId` сохранена текущая конфигурация `DeleteBehavior.SetNull` вместе с required FK, поскольку именно такое состояние отражено в текущем ModelSnapshot. Это потенциально противоречивая комбинация, требующая отдельного решения по бизнес-правилу и миграции; в рамках очистки она не исправлялась.
- Для связей, индексов и SQL defaults сохранены текущие настройки из Fluent-конфигураций/ModelSnapshot; исторические миграции не менялись.
- `Messenger.Core.csproj` пока всё ещё содержит ссылку на `Microsoft.EntityFrameworkCore`. Удаление PackageReference отложено до отдельного полного поиска EF-типов во всех исходниках Core. Это не означает, что в 18 моделях остались EF-аннотации: проверка файлов моделей подтвердила отсутствие EF attributes и EF-specific using.

## Удаление зависимости EF Core из проекта Core (2.7.29)

Проверены все 57 C# исходников Core (DTO, enums, messages, metrics, models, responses и services). Прямых использований EF Core API или типов не обнаружено. Из `Messenger.Core.csproj` удалён `PackageReference` на `Microsoft.EntityFrameworkCore` 10.0.13. `FrameworkReference` на `Microsoft.AspNetCore.App` оставлен, поскольку это независимая зависимость и её удаление требует отдельной проверки ASP.NET Core типов.

## Удаление FrameworkReference ASP.NET Core из Core (2.7.30)

Проверены все 57 C# исходников Core: DTO, enums, messages, metrics, models, responses и services. Не обнаружено ASP.NET Core типов или API: `HttpContext`, `IFormFile`, MVC `ActionResult`/controllers, SignalR hubs, hosted/background services, `IHttpContextAccessor`, ASP.NET Core logging/options/configuration abstractions и других прямых ссылок на `Microsoft.AspNetCore` / `Microsoft.Extensions.*`. Из `Messenger.Core.csproj` удалён `FrameworkReference` на `Microsoft.AspNetCore.App`. В результате Core больше не содержит PackageReference или FrameworkReference.

## Проверка

Для всех 18 файлов `Messenger.Core/Models/*.cs` выполнена проверка: не обнаружены EF mapping attributes и using из `Microsoft.EntityFrameworkCore` / `System.ComponentModel.DataAnnotations`.

Не запускались `dotnet build`, `dotnet test`, генерация пробной миграции или автоматическое сравнение EF model с snapshot. Поэтому компиляция после удаления PackageReference и FrameworkReference, а также полная эквивалентность runtime EF-модели пока не подтверждены. Перед созданием миграций необходимо выполнить model diff и проверить схему на копии PostgreSQL базы.
