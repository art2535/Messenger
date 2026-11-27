# Frontend baseline

## Стек

- ASP.NET Razor Pages
- JavaScript
- SignalR
- PWA
- Service Worker
- IndexedDB
- Background Sync
- Web Push

## JavaScript

В baseline зафиксировано 29 JS-файлов.

Существующая структура уже содержит:

```text
wwwroot/js/
├── auth/
├── chats/
├── offline/
├── settings/
└── utils/
```

Одновременно приложение использует глобальное состояние и значения через `window` (например API/Hub configuration и состояние realtime).

## Offline

Уже существуют:

- IndexedDB;
- outgoing queue;
- cache;
- Background Sync;
- Service Worker;
- optimistic UI.

## Целевое направление

```text
Pages
  ↓
Components
  ↓
State
  ↓
Services
  ├── ApiClient
  └── SignalRConnectionManager
  ↓
API
```

Offline subsystem должен стать отдельной подсистемой:

```text
IndexedDB
   ↓
Outbox
   ↓
SyncEngine
   ├── pushOutgoing()
   ├── pullUpdates()
   ├── retryFailed()
   └── resolveConflicts()
```

На этапе 0 существующие JS-модули и PWA не переписываются.
