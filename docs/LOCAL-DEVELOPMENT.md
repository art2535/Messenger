# Локальная разработка GUAP Messenger

Это руководство описывает локальный запуск и проверку приложения. Оно не описывает production-развёртывание и не требует настройки CI/CD.

## Требования

- .NET 10 SDK;
- Docker Desktop или совместимый Docker Engine — для локальных зависимостей Aspire и интеграционных тестов Testcontainers;
- Visual Studio 2026, Rider или VS Code;
- Git.

## Получение и подготовка проекта

1. Клонируйте репозиторий и откройте Messenger.sln.
2. Восстановите NuGet-пакеты средствами IDE либо выполните dotnet restore Messenger.sln.
3. Создайте локальные User Secrets для проектов, которым нужны секреты. В Visual Studio используйте контекстное меню проекта → Manage User Secrets. В CLI: dotnet user-secrets init --project Messenger.API, затем dotnet user-secrets set --project Messenger.API "Section:Key" "value". Повторите для Web/AppHost, если соответствующая секция нужна этому процессу.
4. Заполняйте только локальные секреты и значения из таблицы архитектуры конфигурации. Не копируйте секреты в appsettings*.json, коммиты, скриншоты или сообщения об ошибках.
5. Для локального запуска через Aspire задайте обязательные credentials AppHost и параметры OIDC, Encryption и VAPID, необходимые выбранным сценариям. Конфигурация проверяется при старте; отсутствующие обязательные значения должны приводить к понятной ошибке конфигурации, а не к скрытому fallback.

## Запуск приложения через UI IDE

Рекомендуемый сценарий — запуск Messenger.AppHost как startup project:
1. Убедитесь, что Docker запущен.
2. В Visual Studio выберите Messenger.AppHost в качестве Startup Project и нажмите Start (F5). В Rider выберите конфигурацию запуска AppHost.
3. Откройте Messenger Web по URL, который Aspire Dashboard показывает для ресурса Web.
4. Используйте ссылки Dashboard для диагностики локальных ресурсов и перехода к API UI, если он включён в Development.

AppHost предназначен для локальной оркестрации зависимостей и приложений. Не используйте его настройки как production-конфигурацию.

### Запуск без AppHost

Если инфраструктурные сервисы уже запущены отдельно, создайте конфигурацию запуска IDE с двумя проектами: Messenger.API и Messenger.Web. Перед запуском проверьте строки подключения PostgreSQL, Redis и RabbitMQ, адрес API, OIDC и остальные обязательные параметры. Не запускайте Web без доступного API.

## База данных

API применяет EF Core migrations при старте. Убедитесь, что PostgreSQL доступен и строка подключения соответствует локальной базе. Если запуск останавливается на миграции, сначала проверьте доступность PostgreSQL, имя базы и права пользователя; не удаляйте volume или базу как первый способ устранения ошибки.

## Проверка через UI

Проверьте основные пользовательские сценарии из матрицы UI-проверки: вход/выход, список чатов, личные и групповые чаты, сообщения, вложения, поиск, настройки, уведомления и переподключение SignalR.

Для проверки браузера используйте DevTools:
- Console — фильтр уровня Verbose/Warnings/Errors; успешный сценарий не должен порождать ошибки и предупреждения приложения;
- Network — нет неожиданных 4xx/5xx, повторяющихся запросов или загрузок ресурсов со статусом Failed;
- Application — Service Worker активен, manifest загружается, offline-кэш и IndexedDB работают ожидаемым образом.

Ошибки расширений браузера и сторонних расширений не относятся к приложению. Для воспроизводимой диагностики повторите сценарий в чистом профиле браузера.

## Тестирование

Для быстрых проверок используйте Test Explorer IDE: выберите проект Messenger.Tests и запустите весь набор тестов. Интеграционные тесты требуют Docker, поскольку используют Testcontainers.

Полный набор тестов: dotnet test Messenger.Tests/Messenger.Tests.csproj --configuration Debug

Сборка solution: dotnet build Messenger.sln --configuration Debug

Не запускайте интеграционные тесты без работающего Docker daemon. Ошибка старта контейнера в таком случае является проблемой тестового окружения, а не результатом проверки бизнес-логики.

## Типичные проблемы

| Симптом | Что проверить |
|---|---|
| AppHost не стартует | Docker daemon, обязательные AppHost credentials и конфликт портов |
| API останавливается при старте | обязательные options, строки подключения, PostgreSQL и миграции |
| Web не загружает данные | доступность API, URL:API, HTTPS-сертификат и Network в DevTools |
| Вход через SSO не завершается | OIDC authority/tenant/client settings, redirect URI и доступность SSO |
| SignalR не подключается | Hub URL, токен, доступность API, WebSocket/HTTPS и Network → WS |
| Push не включается | HTTPS/localhost, разрешение браузера, VAPID public key и активный Service Worker |
| Offline-сообщение не отправляется | IndexedDB, Service Worker, состояние сети и очередь исходящих |
| Integration tests не стартуют | Docker daemon, доступность registry и используемые теги контейнеров |

## Связанные документы

- [Архитектура конфигурации](refactoring/CONFIGURATION-ARCHITECTURE.md)
- [Backend testing](refactoring/BACKEND-TESTING-RESULTS.md)
- [Security audit](refactoring/SECURITY-AUDIT-RESULTS.md)
- [UI verification](refactoring/UI-VERIFICATION.md)