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

Классы Hub находятся в `Messenger.Core/Hubs`, хотя используют ASP.NET Core SignalR и сервисы приложения. Это архитектурное нарушение целевой границы Core и один из приоритетных объектов следующих этапов.

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
