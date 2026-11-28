# Этап 18 — Security Audit

## Область аудита

Проверены backend API, web frontend/BFF, authentication/authorization, SignalR, file storage, CORS, rate limiting, OAuth proxy, HTTP security headers, session cookies, secrets/configuration и существующие security tests.

## Найденные и исправленные проблемы

### SEC-18-001 — Слишком широкое окно допустимой погрешности JWT

**Severity:** Medium

API использовал `ClockSkew = 10 минут`. Это давало дополнительное окно принятия просроченного access token.

**Исправление:** окно уменьшено до 2 минут.

### SEC-18-002 — Audience JWT был захардкожен

**Severity:** Medium

Валидация использовала фиксированные значения audience, несмотря на наличие `AzureAd:Audience` в конфигурации.

**Исправление:** основной audience теперь берётся из конфигурации с fallback `messager`; `account` сохранён для совместимости существующего OIDC flow.

### SEC-18-003 — Не проверялось наличие `sub` после JWT validation

**Severity:** Medium

Идентификация локального пользователя downstream выполняется по `sub`.

**Исправление:** токен без непустого `sub` отклоняется на этапе `OnTokenValidated`.

### SEC-18-004 — OAuth token proxy принимал произвольные form fields

**Severity:** Medium

Анонимный `/oauth/token` проксировал все поля формы в Keycloak.

**Исправление:** ограничен размер form body и введён whitelist OAuth-параметров. Обязательны `grant_type` и соответствующий `code`/ `refresh_token`.

### SEC-18-005 — OAuth proxy возвращал внутренние exception messages клиенту

**Severity:** Medium

Внешнему клиенту возвращались `Exception.Message`.

**Исправление:** клиент получает безопасное сообщение, детали остаются в серверном логе.

### SEC-18-006 — Отсутствовали базовые HTTP security headers

**Severity:** Low

API и Web не задавали единый baseline для защиты от MIME sniffing, clickjacking и нежелательной передачи Referer.

**Исправление:** добавлены `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`. Для production добавляется CSP.

## Уже существующие защитные меры

- JWT issuer, audience, lifetime и signing-key validation.
- HTTPS metadata вне Development.
- `[Authorize]` на защищённых controllers/hubs.
- Ownership checks для attachments.
- Серверные имена файлов с owner prefix.
- Проверка размера, расширения и magic bytes загружаемых файлов.
- Защищённая выдача attachment вместо публичного `/uploads`.
- `nosniff` и `no-store` для защищённых attachment responses.
- Distributed rate limiting для HTTP API.
- SignalR authorization/rate-limit tests.
- CORS ограничен localhost/dev.localhost origin'ами.
- HttpOnly + Secure authentication cookie.
- HttpOnly + Secure session cookie.

## Остаточный риск

- Frontend содержит исторически большой объём DOM construction через `innerHTML`; систематический XSS refactor требует отдельного прохода.
- `/metrics` намеренно не использует пользовательский JWT; при внешнем exposure доступ должен ограничиваться private network/reverse proxy.
- OAuth proxy остаётся anonymous endpoint по архитектурной причине Scalar/OIDC flow и дополнительно защищается общим API rate limiter.
- Dependency vulnerability scanning рекомендуется выполнять в CI регулярно.

## Security test baseline

Добавлены unit tests для security headers. Существующие authorization, file-validation и rate-limit tests сохранены.

## Статус

Этап 18 реализован в ветке `refactor/architecture`. Перед переходом к этапу 19 необходимо дождаться CI и устранить возможные regression failures.
