# Документация (русский) — перевод

`docs/en/` — источник истины, `docs/ru/` — перевод-компаньон. При расхождении верна английская версия.

## Правила двуязычности

- `docs/ru/` — перевод-компаньон. При расхождении EN и RU верна английская версия.
- Статус перевода отслеживается внутри каждого RU-файла через заголовочную пометку в `docs/ru/`; все 11 RU-документов полностью переведены. Для `DECISIONS.md` и ADR русская версия должна строго синхронизироваться.
- Корневые [`README.md`](../../README.md) и [`AGENTS.md`](../../AGENTS.md) находятся вне политики двуязычности: README остаётся русским как человекочитаемая точка входа; AGENTS.md теперь на русском.

## Непереводимые токены

- Идентификаторы кода.
- Коды ошибок.
- HTTP-заголовки.
- SQL-токены.
- Имена сущностей.
- Статусы ADR.
- Ключи конфигурации и переменных окружения.
- Имена типов .NET.
- Ключи frontend-хранилища.
- Маршруты API.
- Имена полей ProblemDetails.

## Статус перевода

Все 11 RU-документов полностью переведены и проверены. `docs/en/` остаётся источником истины: при расхождении EN и RU верна английская версия. Для `DECISIONS.md` и ADR русская версия должна строго синхронизироваться при изменении английского файла; для остальных документов в RU-файле допускается пометка `⚠ translation outdated (commit <sha>)`, если английский файл изменился, а русский не обновлён. Битые ссылки в документации можно обнаружить, запустив `scripts/check-doc-links.ps1` из корня репозитория.

## Порядок перевода

Перевод выполнялся в следующем порядке:

1. `PROJECT.md`
2. `DOMAIN.md`
3. `DECISIONS.md`
4. `adr/ADR-001-cart-concurrency.md`
5. `adr/ADR-002-checkout-idempotency.md`
6. `architecture/architecture.md`
7. `architecture/data-model.md`
8. `architecture/api.md`
9. `SPEC.md`
10. `FRONTEND.md`
11. `ROADMAP.md`

## Навигация

- [PROJECT.md](PROJECT.md)
- [SPEC.md](SPEC.md)
- [DOMAIN.md](DOMAIN.md)
- [DECISIONS.md](DECISIONS.md)
- [ROADMAP.md](ROADMAP.md)
- [FRONTEND.md](FRONTEND.md)
- [architecture/architecture.md](architecture/architecture.md)
- [architecture/data-model.md](architecture/data-model.md)
- [architecture/api.md](architecture/api.md)
- [adr/ADR-001-cart-concurrency.md](adr/ADR-001-cart-concurrency.md)
- [adr/ADR-002-checkout-idempotency.md](adr/ADR-002-checkout-idempotency.md)
