# Database baseline

## Стек

- PostgreSQL 17
- EF Core 10
- Npgsql
- `GuapMessengerContext`
- MassTransit Entity Framework Outbox/Inbox

## EF model

В `GuapMessengerContext` зафиксировано 20 явных `DbSet`:

- AccountSetting
- Attachment
- Chat
- ChatParticipant
- Login
- Message
- Notification
- Reaction
- Role
- User
- UserStatus
- Broadcast
- BroadcastRecipient
- PushSubscription
- Log
- ChatFolder
- ChatFolderChat
- MessageReport
- UserMute
- ModerationLog
- ScheduledMessage

Фактически список содержит 21 сущность: `ScheduledMessage` также объявлен как `DbSet`. Baseline оставляет это количество как исходную точку для аудита.

Кроме доменных сущностей присутствуют:

- связь `User_Roles`;
- MassTransit Inbox/Outbox entities.

## Migrations

Зафиксировано 22 migration-файла EF Core, не считая model snapshot.

## Основные hot paths

По существующей архитектуре особое внимание уделяется:

- Messages;
- Chats;
- ChatParticipants;
- Notifications;
- Reactions;
- PushSubscriptions.

Индексы для этих путей уже присутствуют. Следующий этап должен не создавать их заново, а провести аудит проекций, запросов и конфигураций.

## Важное ограничение

На этапе 0 не меняем:

- схему;
- migrations;
- DbContext;
- connection pool;
- способ применения миграций.

Это будет отдельным этапом Infrastructure/Database.
