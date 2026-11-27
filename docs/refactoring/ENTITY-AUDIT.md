# Аудит и очистка сущностей Core

Актуально для ветки `refactor/architecture`, HEAD на момент аудита: `dec56375dd2ad30a5c7111b30cb89112bc17ea13`.

## Цель

Убрать зависимость предметных моделей Core от EF Core mapping attributes. Схема БД, имена таблиц и колонок, ключи, индексы, ограничения, значения по умолчанию и поведение связей должны полностью задаваться Fluent API в Infrastructure.

Не переносить предметные сущности в Infrastructure только из-за наличия FK или навигационных свойств. Не удалять навигации без отдельной проверки всех потребителей и сценариев сериализации.

## Реестр сущностей

| Сущность | Решение | Состояние / замечания |
|---|---|---|
| AccountSetting | Core | EF-аннотации уже удалены; mapping в AccountSettingConfiguration |
| Attachment | Core | Требуется очистка атрибутов и полное описание колонок/таблицы в AttachmentConfiguration |
| Broadcast | Core | Требуется очистка атрибутов; проверить nullable/required и DeleteBehavior для Sender |
| BroadcastRecipient | Core | Требуется очистка атрибутов; сохранить составной ключ и обе связи |
| Chat | Core | Требуется очистка атрибутов; явно описать Chats, колонки, creator FK и навигации |
| ChatFolder | Core | Требуется очистка атрибутов; сохранить ChatFolders и связь с User |
| ChatFolderChat | Core | Требуется очистка атрибутов; сохранить уникальный индекс FolderId + ChatId и обе связи |
| ChatParticipant | Core | Требуется очистка атрибутов; сохранить составной PK, таблицу Chat_Participants и обе связи |
| Message | Core | Требуется очистка атрибутов; явно описать Messages, все нестандартные колонки, индексы и связи |
| MessageReport | Core | Требуется очистка атрибутов; проверить полное описание строковых ограничений и индексов |
| ModerationLog | Core | Требуется очистка атрибутов; сохранить Moderation_Logs и строковые ограничения |
| Notification | Core | Требуется очистка атрибутов; явно описать нестандартные имена колонок и значения по умолчанию |
| Reaction | Core | Требуется очистка атрибутов; сохранить уникальный индекс MessageId + UserId и обе связи |
| Role | Core | EF-аннотации уже удалены; mapping в RoleConfiguration |
| ScheduledMessage | Core | Требуется очистка атрибутов; mapping уже содержит большую часть колонок, индексов и FK |
| User | Core | EF-аннотации уже удалены; mapping в UserConfiguration |
| UserMute | Core | Требуется очистка атрибутов; сохранить User_Mutes и все нестандартные имена колонок |
| UserStatus | Core | Требуется очистка атрибутов; сохранить User_Statuses и one-to-one FK |

## Не являются предметными сущностями Core

Следующие persistence-only модели уже находятся в Infrastructure и должны там остаться:

- `Log` — технические журналы.
- `Login` — persistence-модель сессии/авторизации.
- `PushSubscription` — техническая регистрация Web Push.

`UtcDateTime` — доменный utility-класс, не EF entity и не часть очистки mapping.

## Выявленные риски в текущем Fluent mapping

Проверка текущих моделей, конфигураций и ModelSnapshot показала, что атрибуты пока не везде заменены эквивалентным Fluent API. Перед удалением атрибутов требуется дополнить конфигурации.

- `AttachmentConfiguration`: явно указать таблицу Attachments и все колонки, включая File_Name, File_Type, Size_in_Bytes и URL.
- `BroadcastConfiguration`: явно указать Message_Text и Title, ограничения Title, а также сверить обязательность SenderId с исторической моделью. В текущей конфигурации DeleteBehavior.SetNull сочетается с обязательным FK — это требует отдельной проверки и не должно молча корректироваться в рамках удаления атрибутов.
- `ChatConfiguration`: явно указать таблицу Chats, Chat_ID, User_ID, Creation_Date, длины Name/Type и обязательность свойств.
- `ChatParticipantConfiguration`: явно указать Chat_Participants и Chat_ID/User_ID/Join_Date, сохранив составной ключ и ограничения.
- `MessageConfiguration`: явно указать Messages и все нестандартные колонки (Message_ID, Sender_ID, Chat_ID, Message_Text, Has_Attachments, Send_Time, Sequence_Number, Delivery_Status, Read_Time, Is_Pinned, Pinned_At).
- `NotificationConfiguration`: явно указать Notification_ID, User_ID, Creation_Date, Read_At; Text и Read оставить с фактическими именами колонок.
- `ReactionConfiguration`: явно указать Reactions, Reaction_ID, Message_ID, User_ID и Reaction_Type.
- `ChatFolderConfiguration` / `ChatFolderChatConfiguration`: проверить полную явную настройку всех колонок, имена таблиц, ключи и индексы.
- `MessageReportConfiguration`, `ModerationLogConfiguration`, `UserMuteConfiguration`, `UserStatusConfiguration`: проверить ограничения длины, типы timestamp with time zone, имена всех колонок и ключей.
- `ScheduledMessageConfiguration`: mapping уже содержит основные имена колонок, но необходимо сверить его целиком с ModelSnapshot.

## Навигации и связи

На этапе очистки сохраняются существующие навигационные свойства. Это намеренное решение: текущие конфигурации и snapshot описывают связи, которые используются сервисами, контроллерами, SignalR и другими частями приложения. Удаление или замена навигации на scalar FK выполняется только отдельным изменением после проверки всех потребителей.

Обязательные для сохранения связи:

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
- User — роли (many-to-many), а также остальные существующие предметные коллекции.

## ModelSnapshot и миграции

ModelSnapshot подтверждает текущие имена таблиц/колонок, ключи, индексы, ограничения, значения по умолчанию и связи. Он должен оставаться согласованным с итоговой моделью. Исторические миграции не редактировать.

После каждого логического набора очистки требуется:
1. Сверить Fluent mapping с текущим ModelSnapshot.
2. Проверить, что EF model diff не предлагает неожиданных изменений схемы.
3. Проверить отсутствие EF mapping attributes и ссылок на EF Core в очищенных Core-моделях.
4. Запустить сборку и тесты, если доступна среда выполнения.

## Порядок реализации

1. Основные сущности чатов и сообщений: Chat, ChatParticipant, Message, Attachment, Reaction.
2. Вспомогательные сущности чатов: ChatFolder, ChatFolderChat, ScheduledMessage.
3. Уведомления и статусы: Notification, UserStatus.
4. Рассылки: Broadcast, BroadcastRecipient.
5. Модерация: MessageReport, ModerationLog, UserMute.
6. Финальный аудит всех Core/Models и сверка snapshot.

На момент создания документа сборка, тесты и EF model diff не запускались.
