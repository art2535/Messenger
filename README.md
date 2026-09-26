# Мессенджер для ГУАП

<div align="center">
<img src="https://upload.wikimedia.org/wikipedia/commons/3/3e/GUAP_logo.svg" alt="Логотип ГУАП" width="180">
<br><br>
<strong>GUAP Messenger</strong> — корпоративный мессенджер<br>
для студентов, преподавателей и сотрудников ГУАП
<br><br>
<strong>Реальное время · Offline-first PWA · Защищённая аутентификация</strong>
<br><br>
</div>

[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-blue)](https://www.postgresql.org/)
[![PWA](https://img.shields.io/badge/PWA-Offline--first-success)](https://web.dev/progressive-web-apps/)
[![CI/CD](https://img.shields.io/badge/CI%2FCD-GitHub%20Actions-2088FF)](https://github.com/art2535/Messenger/actions)
[![Методология](https://img.shields.io/badge/Методология-Waterfall-orange)](https://github.com/art2535/Messenger/wiki/%D0%9F%D1%80%D0%BE%D1%86%D0%B5%D1%81%D1%81%D1%8B)
[![License](https://img.shields.io/badge/License-Не%20определена-lightgrey)](LICENSE)

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
- **Экспорт чатов** в разных форматах
- **Пагинация** истории (cursor-based по `SequenceNumber`)
- Счётчик непрочитанных с корректным сбросом при удалении сообщений

#### Offline-first и PWA
- **Progressive Web App** на всех страницах приложения (`scope: /`)
- **IndexedDB**: кэш списка чатов, истории сообщений, очередь исходящих
- **Background Sync** — автоматическая отправка очереди при появлении сети
- Оптимистичный UI: статус «В очереди» / повтор при ошибке
- Service Worker: precache статики и страниц, network-first + cache fallback
- Установка на домашний экран (Android / iOS / desktop)

#### Уведомления
- **Push (VAPID)** — фоновые уведомления на телефоне и десктопе
- **Стек уведомлений** (как в Telegram): отдельная карточка на каждое сообщение
- Автоскрытие через 4–8 секунд
- Без дублей: при открытом том же чате OS-уведомление не показывается
- Локальные уведомления через SignalR, когда вкладка на переднем плане

#### Безопасность и инфраструктура
- Аутентификация через **OIDC SSO ГУАП**
- Синхронизация профиля из SSO-claims
- Policy-based авторизация
- Шифрование чувствительных данных (**AES**)
- **RabbitMQ** + MassTransit (Outbox для надёжной доставки)
- **Redis** — backplane SignalR + кэш
- **Rate Limiting** (API, отправка, typing)
- API-версионирование (`/api/v{version}/...`)
- Документация API через **Scalar UI**
- **Health-эндпоинт** (`/health`)
- Индексы БД для быстрой выборки сообщений
- Автоматическая очистка сессий при бездействии
- Корректный logout и редирект при активной сессии
- Тёмная тема, адаптив для телефонов и планшетов

### В активной разработке
- Дальнейшее улучшение UI/UX
- Расширение функционала групповых чатов
- Переход хранения файлов на MinIO/S3
- End-to-end шифрование личных чатов (MVP)

---

## Технологический стек

| Компонент | Технология | Описание |
|-----------|------------|----------|
| **Frontend** | ASP.NET Razor Pages + SignalR + **PWA** | SSR, реальное время, offline-first |
| **Offline** | **IndexedDB** + **Background Sync API** | Очередь сообщений, кэш чатов/истории |
| **Real-time** | ASP.NET Core SignalR + Redis backplane | Сообщения, typing, online, удаление |
| **Push** | Web Push (VAPID) + Service Worker | Стек уведомлений, mobile + desktop |
| **Backend** | ASP.NET Core Web API (**.NET 10**) | REST + Hubs + версионирование |
| **Архитектура** | **Clean Architecture** | Core / Infrastructure / API / Web |
| **БД** | PostgreSQL 17 | EF Core 10 + индексы |
| **Messaging** | RabbitMQ + MassTransit 8.5 | Outbox-паттерн |
| **Кэш / Scale-out** | Redis (StackExchange.Redis) | SignalR backplane + кэш |
| **Rate Limiting** | ASP.NET Core Rate Limiting | Fixed-window для API и хабов |
| **Session Cleanup** | BackgroundService + клиентский JS | Автозакрытие сессий |
| **API Docs** | Scalar.AspNetCore | OpenAPI UI |
| **Health Checks** | ASP.NET Core Health Checks | `GET /health` |
| **Тесты** | xUnit v3 | Юнит-тесты доменной логики |

---

## Быстрый старт

### Требования
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL 17
- RabbitMQ
- Redis
- Git
- Visual Studio 2022/2026 или VS Code / Rider

### Пошаговая инструкция

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
   `dotnet user-secrets` или `appsettings.Development.json`  
   (PostgreSQL, Redis, RabbitMQ, VAPID, OIDC, ключи шифрования).

4. **Миграции**

   ```bash
   dotnet ef database update --project Messenger.Infrastructure --startup-project Messenger.API
   ```

5. **Запуск**

   * **Visual Studio**: Multiple startup projects → `Messenger.API` + `Messenger.Web`
   * **Терминал** (два окна):

     ```bash
     cd Messenger.API && dotnet run
     cd Messenger.Web && dotnet run
     ```

В Development: документация API — **Scalar UI**.  
Health-check: `GET /health`.

Подробнее → [**Инструкции по запуску**](https://github.com/art2535/Messenger/wiki/Инструкции)

### PWA / offline (проверка)

1. Откройте приложение по **HTTPS** (или `localhost`).
2. DevTools → Application → Service Workers — статус **activated**.
3. Manifest: имя **GUAP Messenger**, scope `/`.
4. Offline: отключите сеть → список чатов из кэша, текст уходит в очередь IndexedDB и отправится после `online` / Background Sync.

---

## Документация

Полная документация — в [**GitHub Wiki**](https://github.com/art2535/Messenger/wiki).

---

## Как внести вклад

1. Форкните репозиторий  
2. Ветка от `main` (`feature/...` или `fix/...`)  
3. Изменения + тесты при необходимости  
4. Pull Request  

Метки: `good first issue`, `help wanted`, `ci-cd`, `notifications`, `pwa`.

---

## Ведущий разработчик

[**Артём Петров**](https://github.com/art2535) — студент 1 курса ГУАП, направление 09.03.04 «Программная инженерия»

---

**Спасибо за интерес к проекту!**  
Вместе сделаем лучший университетский мессенджер 🚀