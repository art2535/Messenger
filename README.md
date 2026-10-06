# Мессенджер для ГУАП

<div align="center">
<img src="https://upload.wikimedia.org/wikipedia/commons/3/3e/GUAP_logo.svg" alt="Логотип ГУАП" width="180">
<br><br>
<strong>GUAP Messenger</strong> — корпоративный мессенджер<br>
для студентов, преподавателей и сотрудников ГУАП
<br><br>
<strong>Реальное время · Offline-first PWA · .NET Aspire · Защищённая аутентификация</strong>
<br><br>
</div>

[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)](https://dotnet.microsoft.com/)
[![Aspire](https://img.shields.io/badge/.NET%20Aspire-13.5-purple)](https://learn.microsoft.com/dotnet/aspire/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-blue)](https://www.postgresql.org/)
[![PWA](https://img.shields.io/badge/PWA-Offline--first-success)](https://web.dev/progressive-web-apps/)
[![CI/CD](https://img.shields.io/badge/CI%2FCD-GitHub%20Actions-2088FF)](https://github.com/art2535/Messenger/actions)
[![Методология](https://img.shields.io/badge/Методология-Waterfall-orange)](https://github.com/art2535/Messenger/wiki/%D0%9F%D1%80%D0%BE%D1%86%D0%B5%D1%81%D1%81%D1%8B)

**GUAP Messenger** — современное веб-приложение для обмена сообщениями в реальном времени, разработанное специально для сообщества **Государственного университета аэрокосмического приборостроения (ГУАП)**.

Проект реализуется по классической каскадной модели (**Waterfall**) с активным внедрением DevOps-практик.  
**Заказчик** — ГУАП.  
**Дата старта проекта:** 17 сентября 2025 года.

---

## Основной функционал

### Реализовано

#### Чаты и сообщения
- Личные (1:1) и групповые чаты
- Отправка текстовых сообщений и файлов (хранение на сервере)
- Обновления в реальном времени через **SignalR**
- Индикатор «Печатает…» и онлайн-статус пользователей
- Автоматическое прочтение сообщений в открытом чате
- **Редактирование и удаление** сообщений и чатов
- **Ответы** на сообщения (reply)
- **Пересылка** сообщений в другие чаты
- **Реакции** на сообщения
- **Закрепление** чатов и сообщений
- **Черновики** сообщений
- **Расширенный поиск** по сообщениям с фильтрами
- **Экспорт чатов** с фильтрами (дата, отправитель, вложения, непрочитанные, текстовый запрос)
- **Пагинация** истории (cursor-based по `SequenceNumber`)
- Счётчик непрочитанных с корректным сбросом при удалении сообщений
- Корректный порядок чатов в списке при удалении / пересылке (SignalR + consumers)

#### Offline-first и PWA
- **Progressive Web App** на всех страницах приложения (`scope: /`)
- **IndexedDB**: кэш списка чатов, истории сообщений, очередь исходящих
- **Background Sync** — автоматическая отправка очереди при появлении сети
- Оптимистичный UI: статус «В очереди» / повтор при ошибке
- Service Worker: precache статики и страниц, network-first + cache fallback, SWR-хелперы
- Установка на домашний экран (Android / iOS / desktop)
- LRU-ограничение offline-кэша

#### Уведомления
- **Push (VAPID)** — фоновые уведомления на телефоне и десктопе
- **Стек уведомлений** (как в Telegram): отдельная карточка на каждое сообщение
- Автоскрытие через 8–12 секунд
- Без дублей: при открытом том же чате OS-уведомление не показывается
- Локальные уведомления через SignalR, когда вкладка на переднем плане
- Пропуск повторной подписки при уже активной push-подписке

#### Безопасность и инфраструктура
- Аутентификация через **OIDC SSO ГУАП**
- Синхронизация профиля из SSO-claims
- Policy-based авторизация
- Шифрование чувствительных данных (**AES**)
- **RabbitMQ** + MassTransit (Outbox для надёжной доставки, delayed redelivery)
- **Redis** — backplane SignalR + распределённый кэш (`IDistributedCache`)
- **Rate Limiting** (API, отправка, typing — per user + chat)
- API-версионирование (`/api/v{version}/...`)
- Документация API через **Scalar UI**
- **Health-эндпоинт** (`/health`)
- Индексы БД (unread, participants, reactions и др.)
- Автоматическая очистка сессий при бездействии
- Корректный logout: остановка token refresh, revoke SSO, очистка `UserTokenStore`
- Web **не зависит** от проекта API (только `Messenger.Core`; OIDC/SignalR-расширения в Web)
- Тёмная тема, адаптив для телефонов и планшетов
- Редирект загрузок файлов без проксирования через API (производительность)
- HttpClient с `SocketsHttpHandler` pooling, static files cache headers

### В активной разработке
- Дальнейшее улучшение UI/UX
- Расширение функционала групповых чатов
- Переход хранения файлов на MinIO/S3
- **End-to-end шифрование** (личные и групповые чаты, вложения; client-only decrypt)
- Автоматизация установщиков и развитие CI/CD
- Highload / production hardening (partitioning, replicas, Redis Sentinel, blue-green)

---

## Технологический стек

| Компонент | Технология | Описание |
|-----------|------------|----------|
| **Frontend** | ASP.NET Razor Pages + SignalR + **PWA** | SSR, реальное время, offline-first |
| **Offline** | **IndexedDB** + **Background Sync API** | Очередь сообщений, кэш чатов/истории |
| **Real-time** | ASP.NET Core SignalR + Redis backplane | Сообщения, typing, online, удаление |
| **Push** | Web Push (VAPID) + Service Worker | Стек уведомлений, mobile + desktop |
| **Backend** | ASP.NET Core Web API (**.NET 10**) | REST + Hubs + версионирование |
| **Оркестрация** | **.NET Aspire 13.5** | Postgres, Redis, RabbitMQ, Docker Compose |
| **Архитектура** | **Clean Architecture** | Core / Infrastructure / API / Web / AppHost |
| **БД** | PostgreSQL 17 | EF Core 10 + индексы |
| **Messaging** | RabbitMQ + MassTransit 8.5 | Outbox-паттерн |
| **Кэш / Scale-out** | Redis (StackExchange.Redis) | SignalR backplane + `IDistributedCache` |
| **Rate Limiting** | ASP.NET Core Rate Limiting | Fixed-window для API и хабов |
| **Session Cleanup** | BackgroundService + клиентский JS | Автозакрытие сессий |
| **API Docs** | Scalar.AspNetCore | OpenAPI UI |
| **Health Checks** | ASP.NET Core Health Checks | `GET /health` |
| **Тесты** | xUnit v3 | Юнит-тесты API, Web, Core, AppHost |

---

## Быстрый старт

### Требования
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (для режима Aspire + контейнеры)
- PostgreSQL 17, RabbitMQ, Redis — **или** только Docker (Aspire поднимет сам)
- Git
- Visual Studio 2026, VS Code или Rider

### Рекомендуемый запуск через Aspire

1. **Клонирование**

   ```bash
   git clone https://github.com/art2535/Messenger.git
   cd Messenger
   ```

2. **Восстановление пакетов**

   ```bash
   dotnet restore
   ```

3. **Конфигурация**  
   `dotnet user-secrets` для `Messenger.AppHost` / `Messenger.API` / `Messenger.Web`  
   или `appsettings.Development.json`  
   (PostgreSQL, Redis, RabbitMQ, VAPID, OIDC, ключи шифрования, порты Endpoints).

   В AppHost:
   - `UseDocker: true` — Postgres, RabbitMQ, Redis поднимаются в Docker (с PgAdmin, Management UI, Redis Insight).
   - `UseDocker: false` — используются connection strings к уже запущенным сервисам.

4. **Миграции** (после старта БД)

   ```bash
   dotnet ef database update --project Messenger.Infrastructure --startup-project Messenger.API
   ```

5. **Запуск всего стека**

   ```bash
   dotnet run --project Messenger.AppHost
   ```

   Aspire Dashboard откроет ссылки на:
   - **Messenger Web App**
   - **Scalar API Docs** (`/scalar`)
   - инфраструктуру (Postgres, Redis, RabbitMQ)

### Альтернатива без Aspire

* **Visual Studio**: Multiple startup projects → `Messenger.API` + `Messenger.Web`
* **Терминал** (два окна):

  ```bash
  cd Messenger.API && dotnet run
  cd Messenger.Web && dotnet run
  ```

В Development: документация API — **Scalar UI**.  
Health-check: `GET /health`.

Подробнее → [**Инструкции по запуску**](https://github.com/art2535/Messenger/wiki/Инструкции)

### PWA / offline

1. Откройте приложение по **HTTPS** (или `localhost`).
2. DevTools → Application → Service Workers — статус **activated**.
3. Manifest: имя **GUAP Messenger**, scope `/`.
4. Offline: отключите сеть → список чатов из кэша, текст уходит в очередь IndexedDB и отправится после `online` / Background Sync.

### Тесты

```bash
dotnet test --project Messenger.Tests
```

Покрытие: сервисы, контроллеры

---

## Структура проекта

```
Messenger/
├── Messenger.AppHost/          # .NET Aspire 13.5 — оркестрация (Postgres, Redis, RabbitMQ, API, Web)
├── Messenger.Core/             # Домен, DTO, интерфейсы, хабы
├── Messenger.Infrastructure/   # EF Core, Redis, RabbitMQ/MassTransit
├── Messenger.API/              # REST, SignalR, Scalar, Rate Limiting, Health, Consumers
├── Messenger.Web/              # Razor Pages, PWA, JS-клиент (только ссылка на Core)
├── Messenger.Tests/            # xUnit (API, Web, Core, AppHost)
├── images/                     # Диаграммы
└── README.md
```

**Зависимости проектов:**
- `Messenger.Web` → только `Messenger.Core` (без ProjectReference на API)
- `Messenger.API` → `Core` + `Infrastructure`
- `Messenger.AppHost` → `API` + `Web` (+ Aspire.Hosting.PostgreSQL / Redis / RabbitMQ / Docker)

---

## Документация

Полная документация — в [**GitHub Wiki**](https://github.com/art2535/Messenger/wiki).

---

## Как внести вклад

1. Форкните репозиторий  
2. Ветка от `feature/orchestration` или `main` (`feature/...` / `fix/...`)  
3. Изменения + тесты при необходимости  
4. Pull Request  

Метки: `good first issue`, `help wanted`, `ci-cd`, `notifications`, `pwa`, `aspire`, `performance`.

---

## Ведущий разработчик

[**Артём Петров**](https://github.com/art2535) — студент 1 курса ГУАП, направление 09.03.04 «Программная инженерия»

---

**Спасибо за интерес к проекту!**  
Вместе сделаем лучший университетский мессенджер 🚀
