# Этап 2.7 — аудит границы Domain / Persistence

Документ фиксирует аудит ветки `refactor/architecture` после этапа 2.6. Это проектное решение перед созданием отдельных persistence entities. На этом подэтапе сущности и потребители не перемещаются: сначала фиксируются реальные зависимости и безопасный порядок миграции.

## 1. Текущее состояние

- В `Messenger.Core/Models` — 19 файлов модели/вспомогательного типа; 18 оставшихся Core-сущностей участвуют в EF-модели. `UtcDateTime` — helper, а не EF-сущность. Технические модели `Log`, `Login` и `PushSubscription` уже вынесены в `Messenger.Infrastructure/Persistence/Entities`.
- `GuapMessengerContext` находится в Infrastructure; большая часть EF-сущностей использует CLR-типы `Messenger.Core.Models.*`, а вынесенные `Log`, `Login` и `PushSubscription` — типы `Messenger.Infrastructure.Persistence.Entities.*`.
- Все Fluent-конфигурации вынесены в `Messenger.Infrastructure/Data/Configurations`; `OnModelCreating` оставлен для PostgreSQL extension, MassTransit Inbox/Outbox и сканирования конфигураций.
- `GuapMessengerContextModelSnapshot.cs` содержит CLR-имена обоих пространств имён: `Messenger.Core.Models.*` для ещё не перенесённых сущностей и `Messenger.Infrastructure.Persistence.Entities.*` для вынесенных persistence-типов. При каждом следующем переносе требуется синхронно обновлять контекст, конфигурацию и актуальный snapshot; исторические миграции не переписывать.
- `Messenger.Application` уже содержит прикладные DTO и все текущие сервисные интерфейсы; use cases/handlers и специализированные repository ports ещё не выделены.
- `Messenger.Web` ссылается непосредственно на Core. Удаление или переименование Core-моделей без предварительной замены Web-контрактов приведёт к каскадным изменениям в Razor PageModel и UI.
- `Messenger.API` ссылается на Application и Infrastructure. Контроллеры сейчас используют Core DTO, интерфейсы и модели.

## 2. Разделение типов по назначению

| Группа | Типы | Решение |
|---|---|---|
| Основные предметные понятия | `User`, `Role`, `Chat`, `ChatParticipant`, `Message`, `Attachment`, `Reaction` | Оставить в Domain как независимые от EF модели. Связи выражать идентификаторами и методами/операциями предметной области; EF-навигации не переносить в Domain автоматически. |
| Предметные функции вокруг аккаунта и общения | `AccountSetting`, `Notification`, `UserStatus`, `Broadcast`, `BroadcastRecipient`, `ChatFolder`, `ChatFolderChat`, `ScheduledMessage` | Оставить в Domain после уточнения инвариантов. `AccountSetting` подтверждён как предметная модель настроек аккаунта и остаётся в Core; его EF mapping находится в Infrastructure. Не копировать механически: определить, какие типы являются агрегатами, дочерними сущностями или настройками. |
| Модерация | `MessageReport`, `UserMute`, `ModerationLog` | Разделить предметные данные и технические поля хранения; связи с User/Message в Domain — через ID, если они не нужны как агрегатные объекты. |
| Инфраструктурные данные | `Log`, `Login`, `PushSubscription` | Все три технические модели перенесены в Infrastructure; прикладные контракты используют DTO. |
| Вспомогательные типы | `MessageDeliveryStatus`, `UtcDateTime` | Enum оставить в Domain, если статус является частью языка сообщений. `UtcDateTime` оценить отдельно: это helper времени, а не persistence entity. |

Группировка предварительная. Перед переносом конкретного типа нужно проверить все его вызовы и JSON-контракты; таблица не является разрешением на массовое копирование классов.

## 3. Найденные сцепления

### 3.1. Core содержит EF и ASP.NET детали

В части моделей остаются `Key`, `Column`, `ForeignKey`, `InverseProperty`, `Table`, `Index`, `PrimaryKey`, `StringLength`, `Required` и другие EF-аннотации. EF mapping `AccountSetting` уже переведён в Fluent-конфигурацию Infrastructure; предметная связь с `User` сохранена, поскольку используется `UserService`.

Интерфейсы Core также используют ASP.NET типы, например `IFormFile` в `IUserService` и `IMessageService`. Поэтому удаление EF PackageReference само по себе не сделало Core независимым от инфраструктуры. Все такие зависимости нужно перенести/заменить до окончательного удаления соответствующих ссылок.

### 3.2. Сервисные контракты возвращают persistence-модели

Выполнены миграции контрактов `IUserService`, `IChatService`, `IMessageService`, `IAttachmentService`, `ILoginService`, `INotificationService`, `IReactionService`, `IScheduledMessageService`, `IUserStatusService` и `IPushSubscriptionService` в Application. Эти интерфейсы используют DTO и прикладные команды вместо EF-сущностей. Текущий аудит всех `Messenger.Application/Interfaces` не выявил прямых ссылок на `Messenger.Core.Models` или ASP.NET `IFormFile`.

Основная оставшаяся граница теперь находится между Infrastructure и Core-моделями, используемыми как EF-типы в `DbContext`, а также в отдельных прямых потребителях Core-моделей в API/Web. Необходимо продолжить перенос persistence entities и постепенно переводить оставшиеся потребители на Application DTO.

### 3.3. Infrastructure работает непосредственно с Core-моделями

Реализации `UserService`, `ChatService`, `MessageService`, `LoginService`, `NotificationService`, `ReactionService` и другие сервисы по-прежнему используют Core-модели через `DbSet<Core.Models.*>`, EF Include/ThenInclude, tracking, ExecuteUpdate и навигационные свойства. `ScheduledMessageService` уже возвращает Application DTO, но пока читает Core `ScheduledMessage` и использует навигации `Chat` / `Sender` внутри Infrastructure.

Это означает, что безопасный переход требует одновременно:
1. новых persistence типов;
2. перевода запросов и конфигураций на них;
3. явного преобразования между persistence и domain/application contracts;
4. перевода всех потребителей старых контрактов.

### 3.4. API и Web используют модели как контракты

В исходном состоянии контроллеры возвращали модели напрямую и читали navigation properties. В ходе подэтапов 2.7.8–2.7.18 большинство API-контрактов и SignalR payload уже переведены на Application DTO. Оставшиеся прямые обращения к Core-моделям нужно выявлять по конкретным потребителям, прежде всего в Web/Razor PageModel и отдельных API flows.

Нельзя удалять навигации или `JsonIgnore` до замены таких мест на DTO/projection. Иначе изменится форма JSON либо появятся циклические/избыточные графы.

## 4. Целевая схема зависимостей

```text
Messenger.Core (Domain)
  - сущности/значимые объекты без EF и ASP.NET
  - enum и предметные правила

Messenger.Application
  - use cases / handlers
  - входные и выходные contracts (DTO)
  - порты репозиториев и внешних сервисов
  - не ссылается на Infrastructure

Messenger.Infrastructure
  - GuapMessengerContext
  - Persistence/Entities
  - EF Configurations + Migrations
  - реализации портов Application
  - маппинг Persistence <-> Domain / Application

Messenger.API
  - HTTP/SignalR адаптеры
  - request/response contracts Application
  - не возвращает EF entities

Messenger.Web
  - Razor/UI contracts
  - использует Application contracts, не Infrastructure и не EF entities
```

Схема является целевым направлением, а не описанием уже достигнутого состояния. До перевода Web его прямую ссылку на Core нужно сохранить, пока UI использует доменные типы; затем заменить на Application contracts.

## 5. Порядок миграции

### 2.7.1. Зафиксировать модель хранения
- Создать каталог `Messenger.Infrastructure/Persistence/Entities`.
- Определить отдельные persistence классы для таблиц и их навигаций.
- Переносить mapping-аннотации в Fluent-конфигурации, включая те ограничения длины/required, которые пока задаются только атрибутами.
- Сохранить имена таблиц/колонок, ключи, индексы, defaults, FK и delete behavior.
- Не менять схему БД и не создавать миграцию «на перенос типов».

### 2.7.2. Начать с изолированных сущностей
Переносы `Log`, `Login` и `PushSubscription` выполнены в подэтапах 2.7.19–2.7.21. Для каждого типа проверены навигации, конфигурации, snapshot и места создания/чтения.

### 2.7.3. Перевести контракты на Application
- Интерфейсы, которые возвращают/принимают Core EF entities, заменить на DTO, domain objects или специализированные command/result contracts.
- Перенести интерфейсы сервисов из Core в Application.
- Заменить `IFormFile` на application-level file abstraction (например, метаданные + stream/provider), а ASP.NET адаптацию оставить в API.
- Не переносить все DTO одним коммитом: группировать по use case.

### 2.7.4. Перевести Infrastructure
- Переключить DbSet и конфигурации на persistence-типы.
- Добавить явные мапперы/проекции; не возвращать persistence entities за пределы Infrastructure.
- Перевести операции записи на методы/команды, а не принимать EF entities от API.
- Сохранить транзакционные границы, кеширование, outbox, последовательность сообщений и правила удаления.

### 2.7.5. Перевести API, SignalR и Web
- Ввести response DTO для всех endpoints, которые сейчас сериализуют сущности.
- Ввести отдельные SignalR payload DTO; не передавать entity graphs.
- Перевести Razor PageModel на Application contracts.
- Проверить совместимость JSON: имена, nullable, вложенные объекты, даты, перечисления и поведение отсутствующих полей.

### 2.7.6. Завершить очистку Core
- Удалить EF-аннотации и EF/ASP.NET namespaces только после перевода всех consumers.
- Удалить EF-навигации из Domain; оставить идентификаторы и предметные операции.
- Удалить из Core инфраструктурные типы и технические сервисы.
- Добавить CI-проверки, запрещающие EF Core и ASP.NET references/namespaces в Core и зависимости Application от Infrastructure.

## 6. Риски миграций и критерии приёмки

Самый высокий риск — смена CLR entity type в EF Model Snapshot. Snapshot сейчас содержит полные имена `Messenger.Core.Models.*`; замена на `Messenger.Infrastructure.Persistence.Entities.*` может привести к тому, что EF сочтёт типы новыми сущностями. Поэтому перед каждым срезом требуется:
- сравнить текущую EF-модель со snapshot;
- сгенерировать пробную миграцию и убедиться, что в ней нет неожиданных Drop/Create/Rename;
- при необходимости вручную скорректировать snapshot или использовать совместимую стратегию перехода;
- проверить миграции на копии PostgreSQL базы;
- проверить seed data ролей и MassTransit Inbox/Outbox;
- подтвердить отсутствие изменений API/SignalR payload.

Критерии готовности этапа 2.7:
- EF mapping-конфигурации и технические persistence-only entities живут в Infrastructure; предметные сущности остаются в Core;
- Core не содержит ссылок на EF Core/ASP.NET и EF mapping attributes; предметные связи и навигации очищаются по мере перевода их потребителей;
- Application не зависит от Infrastructure;
- API/Web не сериализуют EF entities;
- snapshot и миграции проверены на отсутствие непредусмотренных изменений схемы;
- сборка и тесты проходят.

## 7. Что не менялось на подэтапе 2.7

На этом коммите не создавались копии persistence entities и не переключался DbContext. Это осознанно: текущие сервисные интерфейсы и API/Web ещё напрямую используют Core-модели, а массовое дублирование классов без замены контрактов создало бы две конкурирующие модели данных и риск расхождения поведения.


## Подэтап 2.7.19 — перенос Log в Infrastructure

`Log` не является предметной сущностью: это техническая модель таблицы Serilog в схеме `logging`. Она не используется сервисными контрактами и не имеет связей с другими сущностями. Тип физически перенесён из `Messenger.Core.Models` в `Messenger.Infrastructure.Persistence.Entities`. Конфигурация `LogConfiguration` и `DbSet<Log>` теперь ссылаются на Infrastructure-тип. Сохранены имя таблицы/схемы, колонки, типы, ограничения длины, PK, default `gen_random_uuid()` и три индекса. Текущий Model Snapshot обновлён на новый CLR type; исторические миграции не переписывались. Serilog sink и пакетная очистка логов продолжают работать с теми же таблицей и колонками.


## Актуализация после подэтапа 2.7.21

Технические модели `Log`, `Login` и `PushSubscription` перенесены в Infrastructure. В Core осталось 19 файлов в `Models`, из которых 18 типов являются EF entities и один (`UtcDateTime`) — вспомогательный тип. Все проверенные прикладные интерфейсы находятся в Application и используют DTO/команды; следующий этап — аудит оставшихся Core-моделей и их EF-навигаций.


## Подэтап 2.7.20 — перенос Login в Infrastructure

Persistence-модель `Login` физически перенесена из `Messenger.Core.Models` в `Messenger.Infrastructure.Persistence.Entities`. `GuapMessengerContext`, `LoginConfiguration` и актуальный Model Snapshot используют новый CLR-тип. Прикладные контракты `ILoginService`, `CreateLoginDto` и `LoginDto` не менялись: токен/хеш остаётся внутренним полем persistence-модели.

Связь с `User` сохранена как односторонняя навигация `Login.User`; неиспользуемая обратная коллекция `User.Logins` удалена из Core. Сохранены обязательный FK `User_ID`, индекс по `UserId`, каскадное удаление и имя ограничения `fk_login_user`. Таблица `Logins`, имена колонок, типы данных, PK `Logins_pkey` и исторические миграции не менялись. В актуальном snapshot изменены CLR-имя сущности и описание навигации без изменения реляционной схемы.


## Подэтап 2.7.21 — перенос PushSubscription в Infrastructure

Модель push-подписки физически перенесена из `Messenger.Core.Models` в `Messenger.Infrastructure.Persistence.Entities`. `GuapMessengerContext`, `PushSubscriptionConfiguration`, `PushSubscriptionService` и актуальный Model Snapshot переведены на новый тип. Контракт `IPushSubscriptionService` и DTO Application не менялись: API и прикладные потребители не работают с EF-сущностью.

Связь с `User` сохранена как односторонняя навигация `PushSubscription.User`; неиспользуемая обратная коллекция `User.PushSubscriptions` удалена из Core. Сохранены обязательный FK `UserId`, индекс `IX_PushSubscriptions_UserId`, каскадное удаление и имя ограничения `fk_push_subscription_user`. Таблица `PushSubscriptions`, свойства и типы колонок, PK `PushSubscriptions_pkey` и исторические миграции не менялись. Обновлён только актуальный snapshot.


## Подэтап 2.7.22 — аудит AccountSetting и связи с User

Аудит подтвердил, что `AccountSetting` является частью предметной области: настройки управляют уведомлениями, приватностью, темой и поведением аккаунта. Модель остаётся в `Messenger.Core.Models`; перенос в Infrastructure не выполняется.

`User.Account` и `AccountSetting.User` сохранены. Связь используется Infrastructure: `UserService` загружает настройки для аватара и формирования пользовательского DTO, а сервисы настроек и push-функциональности работают с тем же типом. Удалять навигацию сейчас означало бы менять поведение нескольких сценариев.

EF mapping удалён из атрибутов `AccountSetting` и перенесён в `AccountSettingConfiguration`: таблица `Account_Settings`, имена колонок, PK `Account_Settings_pkey`, уникальный индекс `Account_Settings_Account_ID_key`, связь one-to-one по общему ключу, FK `fk_account` и каскадное удаление заданы явно во Fluent API. Связь перенесена из `UserConfiguration` в конфигурацию самой `AccountSetting`. JSON-аннотация `JsonIgnore` сохранена для прежнего поведения сериализации.

Актуальный Model Snapshot не изменён: CLR-тип остаётся `Messenger.Core.Models.AccountSetting`, а реляционная модель намеренно сохраняется без изменений. Исторические миграции не редактировались.


## Подэтап 2.7.23 — аудит и очистка User

User оставлена в Core как центральная доменная сущность. Проверены потребители её навигаций и конфигурации связанных сущностей. Infrastructure использует User.Account для настроек/аватара, User.ChatParticipants и User.Chats для чатов, User.MessageSenders для отправителя сообщений, а также связи с Notifications, Reactions, Roles, UserStatus, BroadcastsCreated и BroadcastRecipients. Эти навигации не удалялись.

EF-аннотации Key, Column, StringLength, ForeignKey и InverseProperty удалены из User. Все scalar mapping details перенесены в UserConfiguration, включая имена таблицы/колонок, PK, длины строк и required для имени/фамилии. Fluent-конфигурации зависимых сущностей уже задают связи с User; many-to-many UserRole остаётся в UserConfiguration. JSON-аннотации JsonIgnore сохранены.

GuapMessengerContext и CLR-тип Messenger.Core.Models.User в Model Snapshot не менялись; snapshot не редактировался, исторические миграции не изменялись. Очистка User не означает, что все остальные Core-модели уже свободны от EF-аннотаций.


## Подэтап 2.7.24 — аудит и очистка Role

`Role` оставлена в Core как доменная сущность авторизации. Удалены EF-аннотации `Key`, `Column`, `StringLength`, `ForeignKey` и `InverseProperty`; Fluent mapping таблицы `Roles`, PK `Roles_pkey`, колонки `Role_ID`, длины `Name` (50) и обязательности `Name`/`Description` закреплён в `RoleConfiguration`. `Users` navigation и `JsonIgnore` сохранены.

Many-to-many `User.Roles` / `Role.Users` настроена один раз в `UserConfiguration` через `User_Roles`; имена колонок, составной PK, FK и delete behavior сохранены. Потребители проверены: `UserService`, `JwtService`, `AdminService`, API DTO и Web claims authorization. Шесть seed-записей в `RoleConfiguration` не изменены.

Model Snapshot, CLR-тип `Messenger.Core.Models.Role` и исторические миграции не менялись. Подэтап не меняет формат role claims и контрактов API.
