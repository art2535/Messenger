# Этап 19. Архитектура конфигурации

## Принципы

1. Несекретные параметры хранятся в appsettings.json и appsettings.Development.json.
2. Секреты передаются через .NET User Secrets или переменные окружения. Секреты не коммитятся.
3. Переменные окружения используют стандартное отображение двойного подчёркивания: ConnectionStrings__DefaultConnection.
4. Обязательные параметры проверяются при старте приложения. Ошибочная конфигурация останавливает запуск с понятной ошибкой.
5. API и Web используют единые модели IdentityProviderOptions и SignalROptions.
6. AppHost не подставляет известные слабые пароли по умолчанию: локальные credentials должны быть заданы явно.

## Источники конфигурации

ASP.NET Core поддерживает переопределение appsettings через environment variables и User Secrets в Development. Для локальной разработки безопасные defaults хранятся в appsettings.json, а пароли, OAuth client secret, VAPID private key и master encryption key — в User Secrets. В окружениях без User Secrets используются переменные окружения.

## Messenger.API

| Ключ | Назначение |
|---|---|
| ConnectionStrings:DefaultConnection | PostgreSQL |
| ConnectionStrings:RabbitMQ | AMQP URI |
| ConnectionStrings:Redis | Redis; при отсутствии API сохраняет fallback без Redis |
| AzureAd:Instance | HTTPS URL корня realms OIDC |
| AzureAd:TenantId | Realm / tenant |
| AzureAd:Audience | Ожидаемая audience access token |
| URL:API:HTTPS / HTTP / Version | Адреса API и версия маршрутов; обязательны в API и Web |
| Encryption:MasterKeyBase64 | Base64 ключ AES-256, ровно 32 байта |
| Vapid:Subject / PublicKey / PrivateKey | Данные VAPID; ключи проверяются при старте |
| SignalR:ChannelPrefix | Redis channel prefix |
| SessionCleanup:IdleMinutes | Порог неактивности |
| SessionCleanup:IntervalMinutes | Интервал фоновой очистки |
| LogRetention:RetentionDays | Срок хранения логов |
| LogRetention:RunIntervalHours | Период запуска очистки |
| LogRetention:BatchSize | Размер пачки удаления |

## Messenger.Web

Использует ConnectionStrings:Redis, общие секции AzureAd и SignalR, а также TokenRefresh и Session.

Для OIDC-клиента Web обязательны AzureAd:ClientSecret, ClientId, Instance и TenantId. Callback paths по умолчанию: /signin-oidc и /signout-callback-oidc.

TokenRefresh:RefreshBeforeExpirySeconds должен быть меньше TokenRefresh:IdleTimeoutMinutes, переведённого в секунды. Session:IdleTimeoutMinutes задаёт время жизни ASP.NET Core Session.

## Messenger.AppHost

Обязательные ключи: Database:Name, Credentials:Postgres:Password, Credentials:RabbitMQ:Username, Credentials:RabbitMQ:Password, Credentials:Grafana:AdminUser и Credentials:Grafana:AdminPassword.

Порты задаются в Ports. Значения должны быть в диапазоне 1–65535. Aspire передаёт connection strings приложениям через имена ресурсов: DefaultConnection, RabbitMQ, Redis.

## User Secrets

Выполнять из корня репозитория:

```powershell
dotnet user-secrets set "AzureAd:ClientSecret" "<новый-secret>" --project Messenger.Web
dotnet user-secrets set "AzureAd:ClientSecret" "<новый-secret>" --project Messenger.API
dotnet user-secrets set "Encryption:MasterKeyBase64" "<base64-ключ-32-байта>" --project Messenger.API
dotnet user-secrets set "Vapid:PrivateKey" "<vapid-private-key>" --project Messenger.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<postgres-connection-string>" --project Messenger.API
dotnet user-secrets set "ConnectionStrings:RabbitMQ" "<amqp-uri>" --project Messenger.API
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379,abortConnect=false" --project Messenger.API
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379,abortConnect=false" --project Messenger.Web
```

Для AppHost задавать Credentials:* через его User Secrets или локальный appsettings.Development.json, который исключён из Git.

## Ротация раскрытых секретов

Если секрет попадал в чат, issue или лог, считать его раскрытым и заменить у соответствующего провайдера. Простая замена значения в локальном конфиге не отзывает старый секрет.

Для master encryption key нужна отдельная процедура миграции и перешифрования данных: нельзя просто заменить ключ при наличии уже зашифрованных пользовательских данных.

## Проверка

```powershell
dotnet build Messenger.sln --configuration Debug
dotnet test Messenger.Tests/Messenger.Tests.csproj --configuration Debug --no-build
```
