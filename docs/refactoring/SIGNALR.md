# SignalR baseline

## Hubs

В текущем API зарегистрировано 3 Hub:

1. `ChatHub`
2. `UserStatusHub`
3. `NotificationHub`

## Transport

Хабы доступны под версионированными маршрутами:

- `/api/v{version}/hubs/chat`
- `/api/v{version}/hubs/userstatus`
- `/api/v{version}/hubs/notification`

## Текущее расположение

`ChatHub` размещён в отдельном проекте `Messenger.Realtime`, поскольку его тип нужен и API, и Web для публикации событий через общий SignalR Redis backplane. `UserStatusHub` и `NotificationHub` остаются в `Messenger.API`, так как используются только API.

## Разделение realtime-транспорта

`Messenger.Realtime` содержит общий тип `ChatHub` и связанные с ним rate limiter-сервисы. Проект зависит от Core и Application, но не от API или Infrastructure. API размещает endpoint хаба, а Web использует тот же тип для отправки сообщений через общий Redis backplane.

## Результаты security-аудита

- `ChatHub.JoinChat` проверяет членство через actor-aware контракт `IChatService.GetChatParticipantsAsync(chatId, actorId, ...)` до добавления в группу.
- `StartTyping` и `StopTyping` выполняют ту же проверку и нормализуют `chatId` как GUID до отправки события.
- Публичные методы `NotifyParticipant*`, `NotifyChat*`, `NotifyProfile*` удалены из Hub: серверные контроллеры используют `IHubContext<ChatHub>`, поэтому эти методы не должны быть доступны как клиентские invocations.
- `RequestAndBroadcastUserStatus` ограничен пользователями, с которыми вызывающий состоит в общем чате.
- `NotificationHub` не принимает команды на создание уведомлений от SignalR-клиента; идентификатор пользователя резолвится из `sub` через Application service.
- `UserStatusHub` также резолвит внутренний `UserId` из `sub` и использует общий presence tracker, учитывающий несколько соединений пользователя. Метрики соединений увеличиваются после успешного добавления в группу и уменьшаются только для зарегистрированного соединения.

**Оставшийся риск:** проверка членства при `JoinChat` защищает новое подключение к группе, но не отзывает уже существующее членство при последующем удалении участника. Нужно отдельно обеспечить удаление всех connection IDs удалённого пользователя из группы либо перейти на адресную доставку через пользовательские группы; до этого нельзя считать lifecycle удаления участника полностью закрытым.

## Целевое состояние

Hub должен быть thin transport adapter:

```text
SignalR Hub
    ↓
Application use case
    ↓
Domain
    ↓
Infrastructure ports
```

Бизнес-правила, сложная валидация, EF и криптография не должны находиться в Hub.

## Следующий realtime-аудит

- typed client contracts;
- группы `Chat:{chatId}` / `User:{userId}`;
- отказ от широковещательных `Clients.All` там, где нужна точечная доставка;
- единый набор событий;
- reconnect/connection lifecycle.
