# Frontend: текущее состояние и стабилизация

## Стек

- ASP.NET Core Razor Pages и серверный рендеринг;
- JavaScript без отдельного SPA framework;
- SignalR для realtime;
- Service Worker, Web App Manifest, IndexedDB и Background Sync;
- Web Push (VAPID);
- Bootstrap на общих страницах и utility-классы на странице чатов.

## Структура

Messenger.Web содержит Razor Pages, Services и Extensions. Клиентские ресурсы находятся в wwwroot: app.js, css/ и js/{auth,chats,offline,settings,utils}.

Страница чатов использует набор глобальных скриптов и общее состояние в window. Это существующая архитектура, поэтому стабилизация не должна превращаться в рискованный одномоментный rewrite.

## Realtime и PWA

- SignalR connection создаётся в chats-signalr.js; после reconnect выполняется восстановление подписок и статусов.
- PWA Service Worker регистрируется централизованно в Pages/Shared/_LayoutPWA.cshtml.
- app.js ожидает готовую регистрацию для инициализации push, а не запускает повторную регистрацию.
- Offline subsystem использует IndexedDB и очередь исходящих сообщений; ручная проверка описана в UI-VERIFICATION.md.

## Безопасность DOM

Данные из API и SignalR считаются недоверенными:
- текстовые имена и подписи должны добавляться через textContent;
- URL изображений проверяются и ограничиваются HTTP(S);
- innerHTML допустим только для статической разметки или заранее экранированных данных;
- нельзя включать токены и персональные payloads в console logging.

В этапе 20 обработчики SignalR для поиска пользователей и создания новых чатов/участников переведены на безопасное построение DOM. Другие участки с динамическим innerHTML остаются предметом последующего контекстного аудита и не должны считаться автоматически безопасными.

## Стабилизация

Проверять:
1. отсутствие unhandled exceptions и rejected promises;
2. успешную загрузку собственных ресурсов и API;
3. работу auth/session, chat CRUD, messages, attachments, search и settings;
4. восстановление SignalR после потери соединения;
5. работу Service Worker, IndexedDB, offline outbox и push;
6. доступность клавиатурной навигации, labels, focus и responsive layout.

Подробная матрица: [UI-VERIFICATION.md](UI-VERIFICATION.md).