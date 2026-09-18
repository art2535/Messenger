# Мессенджер для ГУАП

<div align="center">
<img src="https://upload.wikimedia.org/wikipedia/commons/3/3e/GUAP_logo.svg" alt="Логотип ГУАП" width="180">
<br><br>
<strong>GUAP Messenger</strong> — корпоративный мессенджер<br>
для студентов, преподавателей и сотрудников ГУАП
<br><br>
<strong>Реальное время · Защищённая аутентификация · Только для университета</strong>
<br><br>
</div>

[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-blue)](https://www.postgresql.org/)
[![License](https://img.shields.io/badge/License-Не%20определена-lightgrey)](LICENSE)
[![CI/CD](https://img.shields.io/badge/CI%2FCD-GitHub%20Actions-2088FF)](https://github.com/art2535/Messenger/actions)
[![Методология](https://img.shields.io/badge/Методология-Waterfall-orange)](https://github.com/art2535/Messenger/wiki/%D0%9F%D1%80%D0%BE%D1%86%D0%B5%D1%81%D1%81%D1%8B)

**GUAP Messenger** — современное веб-приложение для обмена сообщениями в реальном времени, разработанное специально для сообщества **Государственного университета аэрокосмического приборостроения (ГУАП)**.

Проект реализуется по классической каскадной модели (**Waterfall**) с активным внедрением DevOps-практик.  
**Заказчик** — ГУАП.  
**Дата старта проекта:** 17 сентября 2025 года.

## Основной функционал

### Реализовано
- Личные (1:1) и групповые чаты
- Отправка текстовых сообщений и файлов (с хранением на сервере)
- Обновления в реальном времени через **SignalR**
- Индикатор «Печатает…» и онлайн-статус пользователей
- Автоматическое прочтение сообщений в открытом чате
- Аутентификация через **OIDC SSO ГУАП**
- Синхронизация профиля из SSO-claims
- Policy-based авторизация
- Шифрование чувствительных данных (**AES**)
- **Push-уведомления (VAPID)** с улучшенной работой на мобильных устройствах
- **RabbitMQ** + MassTransit (Outbox-паттерн для надёжной доставки сообщений и уведомлений)
- API-версионирование (`/api/v{version}/...`)
- Документация API через **Scalar UI**
- **Пагинация сообщений** (cursor-based по `SequenceNumber`)
- **Индексы БД** для быстрой выборки сообщений по чату
- **Rate Limiting** (защита API, отправки сообщений и typing)
- **Redis backplane** для SignalR (горизонтальное масштабирование)
- **Автоматическая очистка сессий** при долгом бездействии (фоновый сервис + клиентская логика)
- Корректная очистка сессии при выходе из системы
- Автоматический редирект на чаты при активном session token
- Полноценная поддержка мобильных телефонов и планшетов (**Android / iOS**) + PWA
- Поддержка **тёмной темы**
- Улучшенный адаптивный интерфейс чатов и главной страницы

### В активной разработке
- Дальнейшее улучшение UI/UX и отзывчивости интерфейса
- Расширение функционала групповых чатов
- Подготовка к переходу хранения файлов на MinIO/S3
- Автоматизация сборки установщиков и дальнейшее развитие CI/CD

## Технологический стек

| Компонент            | Технология                                      | Описание                                          |
|----------------------|-------------------------------------------------|---------------------------------------------------|
| **Frontend**         | ASP.NET Razor Pages + SignalR Client + PWA      | Серверный рендеринг + реальное время + мобильная адаптация |
| **Real-time**        | ASP.NET Core SignalR + Redis backplane          | Сообщения, typing, online, уведомления            |
| **Backend**          | ASP.NET Core Web API (.NET 10)                  | REST API + SignalR Hubs + версионирование         |
| **Архитектура**      | **Clean Architecture**                          | Core / Infrastructure / API / Web                 |
| **БД**               | PostgreSQL 17                                   | Entity Framework Core 10 + индексы                |
| **Messaging**        | RabbitMQ + MassTransit 8.5                      | Outbox-паттерн                                    |
| **Кэш / Scale-out**  | Redis (StackExchange.Redis)                     | SignalR backplane                                 |
| **Rate Limiting**    | ASP.NET Core Rate Limiting                      | Fixed-window лимиты для API и хабов               |
| **Session Cleanup**  | BackgroundService + клиентский JS               | Автозакрытие сессий при бездействии               |
| **API Docs**         | Scalar.AspNetCore                               | Современный UI для OpenAPI                        |
| **Push**             | VAPID                                           | Браузерные push-уведомления (в т.ч. мобильные)    |
| **Аутентификация**   | OIDC                                            | SSO ГУАП                                          |
| **Шифрование**       | AES                                             | Мастер-ключ в конфигурации                        |
| **CI/CD**            | GitHub Actions                                  | Сборка, тесты, релиз                              |
| **Тестирование**     | xUnit v3                                        | Unit-тесты                                        |

## Установка и запуск (локально)

### Требования
- .NET SDK 10.0+
- PostgreSQL 17
- RabbitMQ
- Redis (опционально, для SignalR backplane в multi-instance режиме)
- Git
- Рекомендуемая IDE: Visual Studio 2026

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

3. **Настройка конфигурации**  
Рекомендуется использовать `dotnet user-secrets` или `appsettings.Development.json`  
(строка подключения к PostgreSQL, Redis, URL-ы, ключи шифрования, VAPID, OIDC и т.д.).

   Пример секции для очистки сессий:
   ```json
   "SessionCleanup": {
     "IdleMinutes": 30,
     "IntervalMinutes": 5
   }
   ```

4. **Применение миграций**

   ```bash
   dotnet ef database update --project Messenger.Infrastructure --startup-project Messenger.API
   ```

5. **Запуск**

   * **Через Visual Studio**: Multiple startup projects → `Messenger.API` + `Messenger.Web`
   * **Через терминал** (два окна):

      ```bash
      # API + SignalR
      cd Messenger.API && dotnet run

      # Web-интерфейс
      cd Messenger.Web && dotnet run
      ```

В режиме Development документация API доступна через **Scalar UI**.

Подробная инструкция → [**Инструкции по запуску**](https://github.com/art2535/Messenger/wiki/Инструкции)

## CI/CD

Настроены **GitHub Actions**:

* Автоматическая сборка и запуск тестов при push/merge в `main`
* Поддержка ручного запуска workflow
* Release workflow (сборка под ОС Windows и Linux)

## Структура проекта

* `Messenger.Core` — доменная модель и бизнес-логика
* `Messenger.Infrastructure` — EF Core, репозитории, RabbitMQ/MassTransit
* `Messenger.API` — REST API + SignalR Hubs + Scalar + API Versioning + Rate Limiting + Session Cleanup
* `Messenger.Web` — Razor Pages + клиент (PWA, тёмная тема, мобильная адаптация)
* `Messenger.Tests` — юнит-тесты (xUnit v3)
* `Deployment/` — файлы для Windows Installer

## Документация

Полная документация доступна в [**GitHub Wiki**](https://github.com/art2535/Messenger/wiki).

## Как внести вклад

1. Форкните репозиторий
2. Создайте ветку от `main` (`feature/название` или `fix/проблема`)
3. Внесите изменения + тесты (при необходимости)
4. Откройте **Pull Request**

Ищите задачи с метками `good first issue`, `help wanted`, `ci-cd`, `notifications`.

## Ведущий разработчик

[**Артём Петров**](https://github.com/art2535) — студент 1 курса ГУАП специальности 09.03.04 "Программная инженерия"

---

**Спасибо за интерес к проекту!**  
Вместе сделаем лучший университетский мессенджер в России 🚀