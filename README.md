# Мессенджер для ГУАП

<div align="center">
<img src="https://upload.wikimedia.org/wikipedia/commons/3/3e/GUAP_logo.svg" alt="Логотип ГУАП" width="180">
<br><br>
<strong>GUAP Messenger</strong> — корпоративный мессенджер<br>
для студентов, преподавателей и сотрудников ГУАП
<br><br>
<strong>Реальное время · Offline-first PWA · .NET Aspire · Наблюдаемость (Prometheus + Grafana) · Защищённая аутентификация</strong>
<br><br>
</div>

[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)](https://dotnet.microsoft.com/)
[![Aspire](https://img.shields.io/badge/.NET%20Aspire-13.5-purple)](https://learn.microsoft.com/dotnet/aspire/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-blue)](https://www.postgresql.org/)
[![Redis](https://img.shields.io/badge/Redis-7.4-DC382D)](https://redis.io/)
[![PWA](https://img.shields.io/badge/PWA-Offline--first-success)](https://web.dev/progressive-web-apps/)
[![OpenTelemetry](https://img.shields.io/badge/Observability-OpenTelemetry%20%2B%20Prometheus%20%2B%20Grafana-E6522C)](https://opentelemetry.io/)
[![Методология](https://img.shields.io/badge/Методология-Waterfall-orange)](https://github.com/art2535/Messenger/wiki/%D0%9F%D1%80%D0%BE%D1%86%D0%B5%D1%81%D1%81%D1%8B)

**GUAP Messenger** — современное веб-приложение для обмена сообщениями в реальном времени, разработанное специально для сообщества **Государственного университета аэрокосмического приборостроения (ГУАП)**.

Проект реализуется по классической каскадной модели (**Waterfall**) с активным внедрением DevOps-практик.  
Система уже работает в production и сопровождается дорожной картой по производительности — см. [`docs/PERFORMANCE_ROADMAP.md`](docs/PERFORMANCE_ROADMAP.md).  
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

#### Безопасность
- Аутентификация через **OIDC SSO ГУАП** (Keycloak)
- Синхронизация профиля из SSO-claims
- Policy-based авторизация
- Шифрование тел сообщений в покое (**AES-GCM**, мастер-ключ из конфигурации)
- Корректный logout: остановка token refresh, revoke SSO, очистка `UserTokenStore`
- Эндпоинт `/metrics` защищён **Basic-аутентификацией** (`MetricsAuth`); в production без заданных учётных данных отвечает `503`
- Web **не зависит** от проекта API (только `Messenger.Core`; OIDC/SignalR-расширения в Web)

#### Инфраструктура и масштабирование
- **RabbitMQ** + MassTransit (Outbox для надёжной доставки, delayed redelivery)
- **Redis** — backplane SignalR + распределённый кэш страниц сообщений/вложений; при недоступности Redis сервис деградирует до прямых запросов в PostgreSQL (с отражением в health-check)
- **Распределённая блокировка на Redis** (`RedisDistributedLock`) — фоновые задачи (очистка неактивных сессий) безопасно работают при нескольких репликах API
- **Rate Limiting**: REST API (per user), отправка сообщений и typing — в том числе для вызовов через SignalR-хаб
- API-версионирование (`/api/v{version}/...`), документация API через **Scalar UI** (Development)
- Автоматическое применение миграций EF Core при старте API
- Настроенный пул соединений Npgsql (`NpgsqlDataSource`, Max Pool Size 100 / Min 5, `CommandTimeout` 15 с)
- Индексы БД под горячие пути (сообщения, непрочитанные, закреплённые, реакции, уведомления, push-подписки)
- Массовая отметка прочтения одним SQL-запросом (`ExecuteUpdateAsync`) вместо загрузки сообщений в память
- Устранён N+1 в списке чатов: функционал блокировок (`Blacklist`) удалён из кода и схемы БД
- Автоматическая очистка сессий при бездействии

#### Наблюдаемость и логирование
- **OpenTelemetry** метрики в API и Web: ASP.NET Core, .NET Runtime, HttpClient (Web), EF Core, MassTransit
- Собственные метрики (`Messenger`, `Messenger.Cache`, `Messenger.Redis`): активные SignalR-подключения, отправленные сообщения и уведомления, длительность действий хаба, ошибки SignalR, cache hit/miss/error, состояние Redis
- **Prometheus + Grafana** в составе AppHost, готовые дашборды `messenger-api-dashboard.json` и `messenger-web-dashboard.json`, экспортёры Redis и RabbitMQ
- **Health-checks**: `GET /health` (всё), `GET /health/live` (liveness), `GET /health/ready` (PostgreSQL + Redis; недоступный Redis даёт статус `Degraded`, а не падение)
- **Serilog**: консоль, файлы `logs/*.log` (14 дней), таблица `logging."Logs"` в PostgreSQL; SQL-запросы EF в БД-логи не попадают
- **Serilog UI** (`/serilog-ui`, только Development) и автоочистка старых логов (`LogRetention`, по умолчанию 30 дней)

#### Фронтенд и производительность
- Сжатие ответов в Web: **Brotli + Gzip** (`ResponseCompression`, в том числе по HTTPS)
- Кэш-заголовки статики из приложения: `/lib`, `/css`, `/js`, `/images` и шрифты — `max-age=31536000, immutable`; остальная статика — сутки
- Загрузки файлов отдаются напрямую из API (`/uploads/`), без прохода через бизнес-логику; в Web — потоковый fallback
- Тёмная тема, адаптив для телефонов и планшетов
- HttpClient с `SocketsHttpHandler` pooling

### В активной разработке
- Дальнейшее улучшение UI/UX
- Расширение функционала групповых чатов
- Переход хранения файлов на MinIO/S3
- Production hardening по [дорожной карте](docs/PERFORMANCE_ROADMAP.md): серверный поиск по сообщениям без дешифрования в памяти, точечная рассылка событий вместо `Clients.All`, конфигурация пула и рантайма .NET, Redis Sentinel, partitioning, blue-green

---

## Технологический стек

| Компонент | Технология | Описание |
|-----------|------------|----------|
| **Frontend** | ASP.NET Razor Pages + SignalR + **PWA** | SSR, реальное время, offline-first |
| **Offline** | **IndexedDB** + **Background Sync API** | Очередь сообщений, кэш чатов/истории |
| **Real-time** | ASP.NET Core SignalR + Redis backplane | Сообщения, typing, online, удаление |
| **Push** | Web Push (VAPID) + Service Worker | Стек уведомлений, mobile + desktop |
| **Backend** | ASP.NET Core Web API (**.NET 10**) | REST + Hubs + версионирование |
| **Оркестрация** | **.NET Aspire 13.5** | Postgres, Redis, RabbitMQ, мониторинг, Docker Compose |
| **Архитектура** | **Clean Architecture** | Core / Infrastructure / API / Web / AppHost |
| **БД** | PostgreSQL 17 | EF Core 10 + Npgsql, индексы, пул соединений |
| **Messaging** | RabbitMQ + MassTransit 8.5 | Outbox-паттерн |
| **Кэш / Scale-out** | Redis 7.4 (StackExchange.Redis) | SignalR backplane, `ICacheService`, распределённые блокировки |
| **Rate Limiting** | ASP.NET Core Rate Limiting | Fixed-window для API, отправки и typing |
| **Метрики** | OpenTelemetry + Prometheus + Grafana | `/metrics` (Basic auth), дашборды API и Web |
| **Логи** | Serilog + PostgreSQL + Serilog UI | Console / File / БД, retention |
| **Health Checks** | ASP.NET Core Health Checks | `/health`, `/health/live`, `/health/ready` |
| **Session Cleanup** | BackgroundService + Redis lock + клиентский JS | Автозакрытие сессий |
| **API Docs** | Scalar.AspNetCore | OpenAPI UI |
| **Деплой** | Docker Compose + nginx + GitLab CI | Автодеплой при push в `main` |
| **Тесты** | xUnit v3 + Moq + FluentAssertions | Юнит-тесты API, Web, Core, сервисов, AppHost |

---

## Быстрый старт

### Требования
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) — Aspire поднимает PostgreSQL, RabbitMQ, Redis и стек мониторинга в контейнерах
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
   или `appsettings.Development.json`:
   - OIDC (`AzureAd:*`), ключ шифрования (`Encryption:MasterKeyBase64`), VAPID (`Vapid:*`);
   - в AppHost — секции `Ports` (порты сервисов), `Credentials` (пароли Postgres / RabbitMQ / Grafana), `Database:Name`;
   - `MetricsAuth:Username` / `MetricsAuth:Password` — одинаковые для API и Web (в Development `/metrics` доступен и без них).

4. **Запуск всего стека**

   ```bash
   dotnet run --project Messenger.AppHost
   ```

   Миграции БД применяются автоматически при старте API.

   Aspire Dashboard откроет ссылки на:
   - **Messenger Web App**
   - **Scalar API Docs** (`/scalar`)
   - pgAdmin, RabbitMQ Management
   - **Prometheus** (по умолчанию `:9090`) и **Grafana** (`:3000`, дашборды подключаются автоматически)

### Альтернатива без Aspire

Нужны запущенные PostgreSQL 17, RabbitMQ и Redis (строки подключения — в конфигурации).

* **Visual Studio**: Multiple startup projects → `Messenger.API` + `Messenger.Web`
* **Терминал** (два окна):

  ```bash
  cd Messenger.API && dotnet run
  cd Messenger.Web && dotnet run
  ```

В Development: документация API — **Scalar UI**, логи — **Serilog UI** (`/serilog-ui`).

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

Покрытие: сервисы, контроллеры API, Core, Web, AppHost.

---

## Production-развёртывание

Production работает на Docker Compose за nginx, деплой — через GitLab CI.

```bash
cp .env.example .env     # заполнить секреты
COMPOSE_PROJECT_NAME=messenger docker compose up -d --build
```

- `docker-compose.yml` — PostgreSQL 17, RabbitMQ, `api`, `web`, `nginx` (Redis подключается отдельно через `ConnectionStrings__Redis`)
- `Dockerfile.api` / `Dockerfile.web` — multi-stage сборка на .NET 10
- `nginx.conf` — маршрутизация `/hubs/` (WebSocket), `/api/`, `/uploads/` на API, остального на Web; увеличенные буферы под OIDC-куки
- `.gitlab-ci.yml` — автодеплой при push в `main`
- `docker-aspire/docker-compose.yaml` — compose, сгенерированный Aspire (справочный вариант)

> [!IMPORTANT]
> `COMPOSE_PROJECT_NAME=messenger` обязателен и в CI, и при ручном запуске — иначе Compose создаст новый том и база будет потеряна.

Обязательные переменные окружения (шаблон — `.env.example`): `POSTGRES_*`, `RABBITMQ_*`, `URL__API__HTTPS`, `AzureAd__*`, `Encryption__MasterKeyBase64`, `Vapid__*`, `MetricsAuth__Username` / `MetricsAuth__Password`.

Подробности — в [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

---

## Мониторинг и эксплуатация

| Что | Где | Примечание |
|-----|-----|------------|
| Health | `GET /health`, `/health/live`, `/health/ready` | `ready` проверяет PostgreSQL и Redis (Redis — `Degraded`) |
| Метрики API / Web | `GET /metrics` | Prometheus-формат, Basic auth (`MetricsAuth`) |
| Дашборды | Grafana (`Messenger.AppHost/monitoring/`) | API, Web; источник — Prometheus |
| Метрики Redis / RabbitMQ | экспортёры в AppHost | `redis-exporter`, `rabbitmq-exporter` |
| Логи | консоль, `logs/`, таблица `logging."Logs"` | в Development — Serilog UI |

> [!NOTE]
> Prometheus должен использовать те же `MetricsAuth`-учётные данные (`basic_auth` в `Messenger.AppHost/monitoring/prometheus.yml`), иначе скрейп `/metrics` в production вернёт `401`/`503`.

---

## Структура проекта

```
Messenger/
├── Messenger.AppHost/          # .NET Aspire 13.5 — оркестрация (Postgres, Redis, RabbitMQ, API, Web, Prometheus, Grafana)
│   └── monitoring/             # prometheus.yml, дашборды Grafana
├── Messenger.Core/             # Домен, DTO, интерфейсы, хабы, метрики
├── Messenger.Infrastructure/   # EF Core + миграции, Redis (кэш, lock), RabbitMQ/MassTransit, сервисы
├── Messenger.API/              # REST, SignalR, Scalar, Rate Limiting, Health, Serilog, OpenTelemetry, Consumers
├── Messenger.Web/              # Razor Pages, PWA, JS-клиент, сжатие и кэш статики (только ссылка на Core)
├── Messenger.Tests/            # xUnit v3 (API, Web, Core, Services, AppHost)
├── docs/                       # ARCHITECTURE.md, PERFORMANCE_ROADMAP.md
├── docker-aspire/              # compose, сгенерированный Aspire
├── docker-compose.yml          # production-стек
├── Dockerfile.api / Dockerfile.web
├── nginx.conf
├── .gitlab-ci.yml
└── README.md
```

**Зависимости проектов:**
- `Messenger.Web` → только `Messenger.Core` (без ProjectReference на API)
- `Messenger.Infrastructure` → `Core`
- `Messenger.API` → `Core` + `Infrastructure`
- `Messenger.AppHost` → `API` + `Web` (+ Aspire.Hosting.PostgreSQL / Redis / RabbitMQ / Docker)

---

## Документация

- [**Архитектура и эксплуатация**](docs/ARCHITECTURE.md) — схема системы, конфигурация, API, деплой
- [**Дорожная карта по производительности**](docs/PERFORMANCE_ROADMAP.md) — анализ узких мест, приоритеты P0–P3, порядок релизов
- [**GitHub Wiki**](https://github.com/art2535/Messenger/wiki) — полная документация проекта

---

## Как внести вклад

1. Форкните репозиторий  
2. Ветка от `main` (`feature/...` / `fix/...`)  
3. Изменения + тесты при необходимости  
4. Pull Request  

Метки: `good first issue`, `help wanted`, `ci-cd`, `notifications`, `pwa`, `aspire`, `performance`, `observability`.

---

## Ведущий разработчик

[**Артём Петров**](https://github.com/art2535) — студент 1 курса ГУАП, направление 09.03.04 «Программная инженерия»

---

**Спасибо за интерес к проекту!**  
Вместе сделаем лучший университетский мессенджер 🚀