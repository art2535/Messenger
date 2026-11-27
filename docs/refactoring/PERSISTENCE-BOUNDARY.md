# Этап 2.7 — аудит границы Domain / Persistence

Документ фиксирует аудит ветки `refactor/architecture` после этапа 2.6. Это проектное решение перед созданием отдельных persistence entities. На этом подэтапе сущности и потребители не перемещаются: сначала фиксируются реальные зависимости и безопасный порядок миграции.

## 1. Текущее состояние

- В `Messenger.Core/Models` — 21 файл модели/вспомогательного типа; 20 типов зарегистрированы в `GuapMessengerContext` через `DbSet`. `UtcDateTime` — helper, а не EF-сущность. Техническая модель `Log` уже вынесена в `Messenger.Infrastructure/Persistence/Entities`.
- `GuapMessengerContext` находится в Infrastructure, но EF строит модель непосредственно на CLR-типах `Messenger.Core.Models.*`.
- Все Fluent-конфигурации вынесены в `Messenger.Infrastructure/Data/Configurations`; `OnModelCreating` оставлен для PostgreSQL extension, MassTransit Inbox/Outbox и сканирования конфигураций.
- `GuapMessengerContextModelSnapshot.cs` также описывает сущности под именами `Messenger.Core.Models.*`. Следовательно, замена CLR-типов на Infrastructure-типы затронет не только контекст, но и snapshot/историю миграций.
- `Messenger.Application` уже содержит прикладные DTO и все текущие сервисные интерфейсы; use cases/handlers и специализированные repository ports ещё не выделены.
- `Messenger.Web` ссылается непосредственно на Core. Удаление или переименование Core-моделей без предварительной замены Web-контрактов приведёт к каскадным изменениям в Razor PageModel и UI.
- `Messenger.API` ссылается на Application и Infrastructure. Контроллеры сейчас используют Core DTO, интерфейсы и модели.

## 2. Разделение типов по назначению

| Группа | Типы | Решение |
|---|---|---|
| Основные предметные понятия | `User`, `Role`, `Chat`, `ChatParticipant`, `Message`, `Attachment`, `Reaction` | Оставить в Domain как независимые от EF модели. Связи выражать идентификаторами и методами/операциями предметной области; EF-навигации не переносить в Domain автоматически. |
| Предметные функции вокруг аккаунта и общения | `AccountSetting`, `Notification`, `UserStatus`, `Broadcast`, `BroadcastRecipient`, `ChatFolder`, `ChatFolderChat`, `ScheduledMessage` | Оставить в Domain после уточнения инвариантов. Не копировать механически: определить, какие типы являются агрегатами, дочерними сущностями или настройками. |
| Модерация | `MessageReport`, `UserMute`, `ModerationLog` | Разделить предметные данные и технические поля хранения; связи с User/Message в Domain — через ID, если они не нужны как агрегатные объекты. |
| Инфраструктурные данные | `Log`, `Login`, `PushSubscription` | `Log` перенесён в Infrastructure. `Login` и `PushSubscription` остаются Core persistence-моделями до отдельного переноса; их прикладные контракты уже используют DTO. |
| Вспомогательные типы | `MessageDeliveryStatus`, `UtcDateTime` | Enum оставить в Domain, если статус является частью языка сообщений. `UtcDateTime` оценить отдельно: это helper времени, а не persistence entity. |

Группировка предварительная. Перед переносом конкретного типа нужно проверить все его вызовы и JSON-контракты; таблица не является разрешением на массовое копирование классов.

## 3. Найденные сцепления

### 3.1. Core содержит EF и ASP.NET детали

В моделях присутствуют `Key`, `Column`, `ForeignKey`, `InverseProperty`, `Table`, `Index`, `PrimaryKey`, `StringLength`, `Required` и другие атрибуты. Есть EF-specific `using Microsoft.EntityFrameworkCore` в `AccountSetting` и `ChatParticipant`.

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
Первый срез — `Log` — выполнен в подэтапе 2.7.19. Следующие кандидаты — `Login` и `PushSubscription`: их сервисные контракты уже отвязаны от EF-моделей, но сами классы ещё находятся в Core и имеют связи с `User`. До переноса каждого типа нужно проверить навигации, конфигурации, snapshot и все места создания/чтения.

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
- все EF entities живут в Infrastructure;
- Core не содержит EF/ASP.NET зависимостей и EF navigation properties;
- Application не зависит от Infrastructure;
- API/Web не сериализуют EF entities;
- snapshot и миграции проверены на отсутствие непредусмотренных изменений схемы;
- сборка и тесты проходят.

## 7. Что не менялось на подэтапе 2.7

На этом коммите не создавались копии persistence entities и не переключался DbContext. Это осознанно: текущие сервисные интерфейсы и API/Web ещё напрямую используют Core-модели, а массовое дублирование классов без замены контрактов создало бы две конкурирующие модели данных и риск расхождения поведения.


## Подэтап 2.7.19 — перенос Log в Infrastructure

`Log` не является предметной сущностью: это техническая модель таблицы Serilog в схеме `logging`. Она не используется сервисными контрактами и не имеет связей с другими сущностями. Тип физически перенесён из `Messenger.Core.Models` в `Messenger.Infrastructure.Persistence.Entities`. Конфигурация `LogConfiguration` и `DbSet<Log>` теперь ссылаются на Infrastructure-тип. Сохранены имя таблицы/схемы, колонки, типы, ограничения длины, PK, default `gen_random_uuid()` и три индекса. Текущий Model Snapshot обновлён на новый CLR type; исторические миграции не переписывались. Serilog sink и пакетная очистка логов продолжают работать с теми же таблицей и колонками.


## Актуализация после подэтапа 2.7.19

Изолированная техническая модель `Log` перенесена в Infrastructure. В Core осталось 21 файл в `Models`, из которых 20 типов являются EF entities и один (`UtcDateTime`) — вспомогательный тип. Все проверенные прикладные интерфейсы находятся в Application и используют DTO/команды; следующий безопасный срез persistence migration — `Login` или `PushSubscription`, после отдельного аудита навигаций `User` и текущего snapshot.
