# Архитектурный baseline и целевой граф

## Текущее состояние на начало этапа 1

До начала изменений:

- Infrastructure → Core
- API → Core + Infrastructure
- Web → Core
- AppHost — Aspire host; отдельные ProjectReference в его csproj отсутствуют.

## Состояние после этапа 1.3

```text
                 Messenger.Core
                   ↑         ↑
                   |         |
          Messenger.Application
                   ↑
             ┌─────┴─────┐
             |           |
     Infrastructure      API
             ↑           |
             └───────────┘

          Web → Core
```

Фактические прямые ProjectReference:

| Проект | Прямые ссылки |
|---|---|
| Messenger.Core | нет |
| Messenger.Application | Messenger.Core |
| Messenger.Infrastructure | Messenger.Core, Messenger.Application |
| Messenger.API | Messenger.Application, Messenger.Infrastructure |
| Messenger.Web | Messenger.Core |
| Messenger.AppHost | нет |

API получает типы Core транзитивно через Infrastructure. Прямую ссылку API → Core удалили, чтобы зафиксировать зависимость transport-слоя от Application и Infrastructure.

## Правила направления зависимостей

- Core не ссылается на Application, Infrastructure, API или Web.
- Application ссылается только на Core.
- Infrastructure ссылается на Application и Core.
- API ссылается на Application и Infrastructure.
- Web ссылается только на Core.
- AppHost остаётся точкой композиции Aspire и не содержит бизнес-логики.

## Автоматическая проверка

Скрипт `scripts/check-architecture.py` проверяет точный набор прямых ProjectReference:

```bash
python3 scripts/check-architecture.py
```

Проверка добавлена отдельным job `validate-architecture` в GitLab CI и запускается для branch/MR pipeline.

## Что пока не переносилось

Контракты сервисов и DTO пока остаются в Core. Это намеренно: в Core всё ещё находятся SignalR hubs, которые используют часть этих интерфейсов. Их перенос без предварительного отделения transport-адаптеров создал бы обратную зависимость Core → Application. Перенос контрактов будет выполняться после подготовки Core в следующем архитектурном шаге.

## Ограничения проверки

На данном этапе проверяется граф прямых ссылок проектов. Проверка не заменяет `dotnet build`, тесты и запуск приложения. Их необходимо выполнить в среде с .NET 10 SDK и зависимостями проекта.
