# Архитектурный baseline

## Текущее состояние

```text
                    Messenger.Core
                 /        |        \
                /         |         \
               v          v          v
Infrastructure            API       Web
               \          |
                \         |
                 v         v
                  AppHost
```

Фактические ProjectReference:

- Infrastructure → Core
- API → Core + Infrastructure
- Web → Core
- AppHost → API + Web

## Целевое состояние после рефакторинга

```text
                         Messenger.Core
                              ↑
                              |
                    Messenger.Application
                         ↑          ↑
                         |          |
              Infrastructure       API
                         \          /
                          \        /
                           Messenger.Web
```

В более точной форме направление зависимостей должно быть:

```text
Core
  ↑
Application
  ↑
Infrastructure
  ↑
API
```

Web должен зависеть только от публичных contracts/core-типов, которые действительно нужны UI, и не получать зависимости на Infrastructure/API.

## Запреты этапа 1

- Core не ссылается на Infrastructure.
- Core не ссылается на API.
- Application не ссылается на Infrastructure.
- Application не ссылается на API.
- Infrastructure реализует интерфейсы Application/Core.
- API отвечает за transport concerns, а не за бизнес-логику.

## Что пока остаётся

Baseline не переносит классы и не меняет ProjectReference. Это отдельные изменения следующего PR, чтобы каждый архитектурный шаг можно было проверить независимо.
