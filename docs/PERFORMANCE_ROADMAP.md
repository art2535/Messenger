# Карта обновлений по производительности (Production Performance Roadmap)

**Дата анализа:** 11.10.2026 (ред. 2 — с учётом решений команды)  
**Объём:** репозиторий GUAP Messenger (.NET 10: API, Web/BFF, Core, Infrastructure; PostgreSQL + Redis + RabbitMQ + SignalR)

Все пункты основаны на конкретном коде репозитория (файлы и строки указаны).  
Приоритеты:
**P0** — критично для проде, **P1** — высокий, **P2** — средний, **P3** — низкий / hygiene.

### Область действия (scope)
- **Вне области: файлы деплоя** — `.gitlab-ci.yml`, `docker-compose.yml`, `Dockerfile.api`, `Dockerfile.web`, `nginx.conf`, `.env.example`. Они написаны другими разработчиками и рабочие, изменений не предлагают. Пункты 10–11 пересмотрены так, чтобы фиксы жили только в коде приложений (`Program.cs`, middleware, статические ассеты в `wwwroot`).
- **Удалена таблица блокировок:** `Blacklist` будет удалена из схемы БД, поэтому обращения к ней не оптимизируются — код в цикле `GetUserChats` удаляется вместе с функционалом (см. п. 1), новые индексы на `Blacklists` не заводятся (см. п. 4).

---

## Сводная таблица

| # | Эпик | Приоритет | Ожидаемый эффект | Трудозатраты |
|---|------|-----------|------------------|--------------|
| 1 | Убрать N+1 в списке чатов: удалить вызовы проверок блокировок из цикла (таблица `Blacklist` выводится) | P0 | Список чатов: с O(N) запросов до 1–2 | S |
| 2 | Поиск сообщений: перенос фильтрации в БД / отключение on-the-fly дешифрования | P0 | Потолок p95 при поиске, снятие CPU-нагрузки AES | M |
| 3 | MarkAsRead: массовый UPDATE вместо загрузки всех сообщений в память | P0 | Меньше памяти/лагов при прочтении больших чатов | S |
| 4 | Индексы БД (ChatParticipants, Attachments, Notifications) | P0 | Ускорение горячих путей выборки чатов и вложений | S |
| 5 | Кэширование зашифрованных DTO вместо повторного шифрования при промахе кэша | P1 | −CPU на записи, стабильный p95 GET latest | S |
| 6 | Rate limiting для SignalR хаба `SendMessage` | P1 | Защита от флуда, снятие нагрузки с MQ/БД | S |
| 7 | Настройки пула Npgsql + `EnableOptimizedDbContext` | P1 | Меньше contention на соединения | S |
| 8 | Redeploy-safe фоновые сервисы (session cleanup, token refresh) | P1 | Стабильность при scale-out | S |
| 9 | Health-check Redis + fallback вместо «кэш молча выключен» | P1 | Наблюдаемость деградации | S |
| 10 | Статика: сжатие и кэш-заголовки из приложения (ResponseCompression) + minify-сборки в `wwwroot` (9.5 МБ) — без правки `nginx.conf` | P2 | TTFB/LCP фронтенда | S |
| 11 | Сборка/рантайм .NET: Release-артефакты, workstation GC, layering через `Directory.Build.props`/csproj — без правки Dockerfile | P3 | Меньше памяти/GC-пауз на реплику | S |
| 12 | Observability: метрики EF/CoreCLR, медленные запросы | P2 | Основа для дальнейшей оптимизации | M |
| 13 | Глобальные broadcast-события Clients.All → адресные группы | P3 | Снижение fan-out при росте аудитории | M |
| 14 | Отдельное хранилище файлов (S3/MinIO) вместо локального диска | P3 | Масштабируемость API-реплик | M |

---

## P0 — узкие места, дающие деградацию уже сейчас

### 1. N+1-запросы в списке чатов (`ChatService.cs`, строки 139–152) — фикс через удаление

**Проблема:** в цикле `foreach (var chat in chats)` для каждого приватного чата выполняются два await-запроса
`_userService.IsBlockedByAsync(...)` (реализация — `UserService.cs:243`, `AnyAsync` на таблице `Blacklists`,
туда и обратно). Для пользователя со 100 чатами это ~200 дополнительных SQL-запросов на каждый вызов `GetUserChats`.

**Решение (с учётом того, что таблица `Blacklist` будет удалена):** оптимизировать запросы к `Blacklists` не нужно —
вместо этого **удалить обращения из цикла**:
1. В `GetUserChats` убрать блок `bool isBlocked = …` с двумя `IsBlockedByAsync` и поле `isBlocked` из DTO/маппинга
   (флаг всегда `false`).
2. Вычистить оставшийся функционал блокировок вместе с таблицей: `BlockUser`/`UnblockUser`/`IsBlockedByAsync`/
   `GetBlacklistAsync` в `UserService.cs` (строки 47, 167, 211, 243), контроллер-эндпоинты, модель `Blacklist`,
   `DbSet<Blacklists>` и конфигурацию в `GuapMessengerContext.cs` — единой миграцией `DROP TABLE Blacklist`.
   Это снимает и N+1, и мёртвый код за один заход.

**Метрика успеха:** кол-во SQL-запросов на `GET /chats` не растёт с числом чатов (проверить через EF-логи);
в схеме БД таблицы `Blacklist` больше нет.

### 2. Поиск сообщений выполняется в памяти процесса (`MessageService.SearchMessagesAsync`, строки ~85–110)

**Проблема:** метод берёт **до 300 последних сообщений чата целиком** (`GetMessagesByChatIdPagedAsync(chatId, null, 300, …)`),
затем **дешифрует каждое сообщение** (`TryDecryptSafe`) и фильтрует `Contains` в C#. Это:
- O(300) операций AES-GCM на каждый поиск (CPU);
- поиск только по последним 300 сообщениям (и функционально неполный, и тяжело для прод-нагрузки);
- логирование CACHE HIT/MISS на уровне `Information` спамит в логи при каждом поиске.

**Решение (два шага):**
1. Быстро: ограничить объём, кешировать результат поиска в Redis на короткое время, снизить шум логов.
2. Правильно: так как текст хранится **зашифрованным (AES-GCM)**, полнотекстовый поиск в Postgres невозможен без
   отдельного поля-индекса. Завести `messages.search_index` — детерминированный токен-поиск (например, HMAC-темы
   n-грамм или отдельный поисковый сервис/Elasticmeilis), либо нормализованное поле, по которому можно искать
   серверной стороной (`WHERE ... LIMIT/OFFSET` в SQL вместо цикла в C#).

**Метрика успеха:** p95 поиска < 150 мс; CPU API не скачет при всплесках поиска.

### 3. `MarkMessagesAsReadAsync` грузит все непрочитанные сообщения в память (`MessageService.cs`, строки ~113–135)

**Проблема:** `ToListAsync()` выбирает ВСЕ непрочитанные сообщения чата, затем помечает их по одному и делает
`SaveChangesAsync` (UPDATE по каждой строке). В активных чатах это тысячи трейков EF и десятки тысяч round-trip-строк.

**Решение:** одно массовое обновление:
```csharp
var updated = await _context.Messages
    .Where(m => m.ChatId == chatId && m.SenderId != readerId && m.ReadTime == null)
    .ExecuteUpdateAsync(s => s
        .SetProperty(m => m.ReadTime, now)
        .SetProperty(m => m.DeliveryStatus, MessageDeliveryStatus.Read), ct);
```
Аналогично проверить другие пути массовой отметки прочтения (BroadcastService/NotificationService).

**Метрика успеха:** один SQL-запрос вместо N; отсутствие LOH-аллокаций при прочтении большого чата.

### 4. Недостающие индексы (`GuapMessengerContext.cs`)

Есть только индексы на `Messages (ChatId, SequenceNumber/SendTime/ReadTime)` и `Reactions (MessageId, UserId)`.
Для горячих запросов нужны:

| Таблица | Индекс | Зачем |
|---|---|---|
| `ChatParticipants` | `(UserId)` include `ChatId` и `(ChatId)` | выборка чатов пользователя (`GetUserChats`), `InvalidateUserChatsCacheAsync` |
| `Attachments` | `(MessageId)` | `GetAttachmentsByMessageIdAsync`, join в историях |
| `Notifications` / `PushSubscriptions` | по пользователю + статусу | счётчики и доставка push |

**Не заводим:** индексы на `Blacklists` — таблица выводится из схемы (см. п. 1), оптимизация её запросов нецелесообразна.

**Решение:** добавить в `OnModelCreating` + миграция `CREATE INDEX IF NOT EXISTS` (CONCURRENTLY на живой базе).
В ту же миграцию включить `DROP TABLE Blacklist` (после удаления навигаций из модели — иначе FK-констрейнты `fk_blocked_to`/`fk_blocked_by` заблокируют DROP).

---

## P1 — архитектура под продакшн-нагрузку

### 5. Кэш страниц сообщений содержит сырые шифртексты (`MessageService.ToCacheItem`, строки ~600+)

**Проблема:** в Redis кладётся `CachedMessageItem` с **зашифрованным** `MessageText`; при промахе кэша каждое
сообщение шифруется заново (`EncryptForRecipient`). При холодном кэше (после инвалидации на каждое отправленное
сообщение — `InvalidateMessageCachesAsync`) вся нагрузка шифрования ложится на PostgreSQL-промах.

**Решение:** хранить в кэше уже готовые для отправки DTO (как они выглядят после маппинга), инвалидировать точечно
(добавление сообщения = unshift в кэш-лист вместо полного сброса ключей `{1,20,30,50,100,200}`).

### 6. Rate limiting не применяется к отправке через SignalR (`ChatHub.SendMessage` / `SendTyping`)

**Проблема:** политики `send-message` и `typing` объявлены в `RateLimitingExtension.cs`, но контроллеры ограничены
политикой `"api"` (`Program.cs`: `MapControllers().RequireRateLimiting("api")`), а методы хаба вызываются напрямую —
фиксированное окно 120 req/min не защищает путь «чат-трафик». Флуд через хаб = удар по RabbitMQ, БД и fan-out.

**Решение:** применить `[EnableRateLimiting("send-message")]` / `("typing")]` к методам `ChatHub`
(rate limiting работает для MVC; для SignalR — ручной `RateLimiter.TryAcquirePermit` в методе хаба).

### 7. Конфигурация DbContext и пула соединений (`PostgreSQLExtension.cs:29`)

**Проблема:** `AddDbContext` без настроек: нет `NpgsqlDataSource` с явным пулом, нет `EnableOptimizedDbContext`
(Npgsql 9+/EF10), нет лимита времени команды, нет `AddResiliency` (уместна только вне транзакций — у нас
`PublishChatMessageAsync` использует явные транзакции, значит retry-стратегию включать нельзя молча).

**Решение:**
```csharp
var dataSource = new NpgsqlDataSourceBuilder(connectionString)
    .WithMultiRangeTypeMapping() // если нужно
    .Build();
dataSource.ConnectionLifetime = ...; // tune Maximum Pool Size в строке подключения (напр. 100)
services.AddDbContext<GuapMessengerContext>(o => o.UseNpgsql(dataSource, x => x
    .EnableOptimizedDbContext()
    .CommandTimeout(15)));
```
Плюс: `ServerSideBatching` там, где применимо; мониторинг `Npgsql pool waits`.

### 8. Фоновые сервисы небезопасны при нескольких репликах

- `SessionCleanupBackgroundService` (API): стартует с `Task.Delay(15s)` и циклом — каждая реплика будет чистить
  сессии параллельно (гонка, дубли DELETE).
- `TokenRefreshBackgroundService` (Web): аналогично, периодический таймер на каждой реплике.

**Решение:**distributed lock (Redis `SET NX` / `RedLock`) либо переключатель `LeaderElection` (Aspire уже в стеке),
либо идемпотентные операции + `pg_advisory_lock`.

### 9. Прозрачность состояния кэша (`RedisCacheService.cs`)

**Проблема:** при недоступном Redis сервис «молча» деградирует (`_enabled=false`), но health-check об этом не знает:
прод может работать на прямых запросах в Postgres без алерта. Плюс `LogInformation` на каждый HIT/MISS — дорого при
высоком RPS.

**Решение:** зарегистрировать Redis в health checks (`HealthChecksExtension.cs`), HIT/MISS перевести в `LogDebug`,
добавить счётчик cache-hit ratio в метрики.

---

## P2 — инфраструктура и фронтенд

### 10. Статика весит ~9.5 МБ raw (`Messenger.Web/wwwroot/lib`: bootstrap 8.4M + jquery 920K + …)

**Важно:** `nginx.conf` — рабочий файл деплоя, не трогаем. Все изменения — только в коде приложения `Messenger.Web`:

- **Сжатие на уровне приложения:** включить `ResponseCompression` в `Program.cs` (Web/BFF отдаёт статику сама):
  ```csharp
  services.AddResponseCompression(o =>
  {
      o.EnableForHttps = true;
      o.Providers.Add<BrotliCompressionProvider>();
      o.Providers.Add<GzipCompressionProvider>();
  });
  ```
  с настроенным `MimeTypes` под css/js/woff — для текстовой статики даёт ~70–80% экономии трафика без nginx.
- **Кэш-заголовки из приложения:** для `/lib`, `/css`, `/js` выставить `Cache-Control: public, max-age=31536000, immutable`
  через `StaticFileOptions.OnPrepareResponse` в `Program.cs` (без правки конфигов).
- **Minified-сборки в `wwwroot`:** заменить bootstrap.bundle.min.js/bootstrap.min.css и jquery.min.js на актуальные
  min-версии (уже существуют в поставке библиотек) — экономия ~60% объёма до сжатия.
- Долгосрочно: bundling/minify на сборке (esbuild) — тоже в рамках проекта Web, вне файлов деплоя.

### 11. Конфигурация рантайма .NET внутри приложений (без правки Dockerfile.api / Dockerfile.web)

`Dockerfile.*` — рабочие файлы деплоя, не трогаем. Что можно сделать только средствами репозитория:
- `Directory.Build.props` в корне решения: `<DebugType>none</DebugType>` / `<SatelliteResourceLanguages>ru</SatelliteResourceLanguages>`
  для Release — меньше размер артефакта и время старта;
- тюнинг GC кодом/конфигом приложения (`gcServer` в `runtimeconfig.template.json` проектов API/Web), а не env-переменной в compose;
- убедиться, что `Configure<AspNetCoreOptions>` нигде не включает `RequireExactPromotionOrdering`/файловые провайдеры с `RefreshOnRead`
  (пересбор статики на каждый запрос).

Эффект умеренный (P3), но полностью в нашей зоне ответственности.

### 12. Наблюдаемость

Сейчас нет метрик вообще (только логи). Минимальный набор — весь в коде приложений (Program.cs/DI-экстеншены):
- OpenTelemetry: ASP.NET Core request duration, EF Core (`Microsoft.EntityFrameworkCore` diagnostics),
  SignalR connections, RabbitMQ consumer lag, Redis latency;
- `dotnet counters` / Prometheus-экспортер (`/metrics` эндпоинт) + дашборд p50/p95/p99 по эндпоинтам;
- slow query log Postgres (`log_min_duration_statement=100ms`) — параметр на стороне сервера БД, применяется DBA
  (файлы деплоя не затрагиваются).

---

## P3 — масштабирование в долгую

### 13. Fan-out через `Clients.All` (`ChatHub.cs:80, 119, 189, 198, 207, 278`)

Смена статуса онлайн/аватар/имя рассылается **всем подключённым** (плюс дубль `Clients.User`). При росте базы это
линейно увеличивает трафик backplane Redis. Заменить на целевые группы (`User_{id}`, участники чатов, подписчики).

### 14. Хранение файлов на локальном диске (`UserService.cs:130` FileStream в `wwwroot/uploads`-подобную папку)

Не работает при нескольких репликах API и бэкапах. Вынести в S3-совместимое хранилище (MinIO; развёртывание —
через AppHost, без правки `docker-compose.yml`), отдача — presigned URL / редирект (в README этот подход уже заявлен как цель).

### 15. Прочее (hygiene)

- `AttachmentService.GetAttachmentsByMessageIdAsync` — добавить `AsNoTracking()`.
- `SearchMessagesAsync` вызывает `GetMessagesByChatIdPagedAsync(..., 300, ...)`, что при `beforeSequence==null`
  пишет 300-элементную страницу в кэш — не нужно засорять кэш поисковым лимитом (кэшировать только «нативные» лимиты).
- `PublishChatMessageAsync`: публикация в RabbitMQ внутри DB-транзакции без Outbox — при падении между
  commit и publish возможна потеря/дублирование; рассмотреть `TransportTransactionMode.SendsAtomicWithReceive`
  (уже частично настроено в `RabbitExtension.cs`) + outbox-таблицу.
- `PrefetchCount = 15` в MassTransit — проверить/поднять после появления метрик consumer lag.

---

## Рекомендуемый порядок релизов

| Релиз | Состав | Риск |
|---|---|---|
| **v1 (горячие фиксы, ~1 неделя)** | Пункты 1 (+миграция `DROP TABLE Blacklist`), 3, 4 (+миграция индексов CONCURRENTLY), 9 | Низкий |
| **v2 (CPU/latency, ~1–2 недели)** | Пункты 2 (шаг 1), 5, 6, 7 | Средний (нагрузка на Redis) |
| **v3 (platform, ~2 недели)** | Пункты 8, 10, 11, 12 | Низкий |
| **v4 (scale-out)** | Пункты 2 (правильный поиск), 13, 14, 15 | Высокий, требует design review |

**Критерий готовности каждого релиза:** сравнение p95/p99 и allocation-rate до/после на staging под нагрузкой
(scripts/k6 в репозитории; CI-конфиг `.gitlab-ci.yml` не трогаем — прогон выполняем вручную/отдельным стендом).
