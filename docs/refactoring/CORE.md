# Этап 2 — аудит и очистка Messenger.Core

Документ отражает состояние ветки `refactor/architecture` после commit `eece268b9388f26b99531ffce2fd959b8c3c7155`.

## Что было в Core

По дереву исходников Core содержит следующие группы:

- `Models` — доменные/EF-модели (23 файла на baseline);
- `DTOs` — HTTP/API и внутренние DTO;
- `Interfaces` — сервисные интерфейсы, используемые API и Infrastructure;
- `Messages` — контракты сообщений;
- `Responses` — API response-модели;
- `Metrics` — метрики;
- `Services` — rate limiter helpers;
- `Hubs` — SignalR transport-адаптеры (перенесены на этапе 2).

## Выполнено

### 2.2. Удаление ASP.NET/SignalR из Core — первый перенос

Перенесены:

- `ChatHub`
- `NotificationHub`
- `UserStatusHub`

Новые пути:

- `Messenger.API/Hubs/ChatHub.cs`
- `Messenger.API/Hubs/NotificationHub.cs`
- `Messenger.API/Hubs/UserStatusHub.cs`

Namespace изменён на `Messenger.API.Hubs`, импорт в `Program.cs` обновлён. Маршруты и вызовы `MapHub` оставлены прежними.

Из `Messenger.Core.csproj` удалены прямые зависимости `Microsoft.AspNetCore.Authentication.JwtBearer` и `Microsoft.EntityFrameworkCore`. Это убирает эти пакеты из Core, но само по себе ещё не доказывает, что все оставшиеся исходники Core полностью очищены от инфраструктурных концепций.

## Что осталось

### 2.1. EF Core и persistence concerns

- проверить каждую модель на EF-specific атрибуты и навигационные свойства;
- создать отдельные persistence entities/configurations в Infrastructure;
- перенести mapping без изменения схемы БД;
- только после миграции всех потребителей удалить persistence attributes из доменных типов.

### 2.2. ASP.NET concerns

- Hub-классы уже вынесены;
- проверить оставшиеся файлы Core на ASP.NET/HTTP/JWT типы;
- переносить найденные transport-specific типы в API, не добавляя обратных ссылок.

### 2.3. Serialization concerns

В моделях, например `Message` и `User`, ещё присутствуют `JsonIgnore` и другие serialization attributes. Их нужно заменить на отдельные DTO/контракты и настройки сериализации на границах приложения. Удалять их до замены нельзя: это может изменить JSON-ответы и раскрыть навигационные графы.

### 2.4. Domain и DTO

DTO, request/response модели и сервисные интерфейсы пока остаются в Core. Их переносить после подготовки Application contracts и обновления зависимых потребителей. Нельзя заставлять Core ссылаться на Application.

## Безопасный порядок продолжения

1. Инвентаризировать все типы Core и их потребителей.
2. Создать persistence entities/configurations в Infrastructure и перенести mapping по одной сущности за раз.
3. Перевести Infrastructure services/repositories на persistence entities, сохранив публичные контракты на переходный период.
4. Создать Application.Contracts и перенести DTO с одновременным обновлением API/Web/Infrastructure.
5. Перенести сервисные интерфейсы в Application.Common.Interfaces, когда Core перестанет зависеть от них.
6. Удалить из Core EF mapping и serialization attributes после полного перевода потребителей.
7. Зафиксировать отсутствие ASP.NET, EF Core и transport-зависимостей в Core архитектурной проверкой.

## Проверки

Для текущего изменения проверены Git tree, расположение файлов Hub, namespace и импорт в Program.cs, а также содержимое Core csproj. Сборка и тесты не запускались; поэтому совместимость после удаления PackageReference пока не подтверждена компилятором.
