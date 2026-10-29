# ADR-011: Outbox

- **Статус:** Принято
- **Дата:** 2026-10-29

## Контекст

Доменные события публикуются после commit in-process через `IMediator.Publish`.
Доставка работает at-most-once: при сбое handler'а событие или audit-запись
теряется, автоматический retry отсутствует. Это зафиксировано в
[ADR-007](ADR-007-domain-events.md) и [ADR-010](ADR-010-audit.md).

Для будущих уведомлений и интеграций нужна гарантированная доставка внешним
подписчикам. При этом существующий in-process audit не должен меняться и должен
оставаться at-most-once.

## Решение

### Таблица OutboxMessages

Таблица `OutboxMessages`:

| Свойство | Тип SQL | Требования |
|---|---|---|
| `Id` | `uniqueidentifier` | Первичный ключ |
| `EventType` | `nvarchar(512)` | Обязательно; полное имя типа события без assembly-версии |
| `Payload` | `nvarchar(max)` | Обязательно; JSON |
| `CreatedAtUtc` | `datetime2` | Обязательно; UTC |
| `ProcessedAtUtc` | `datetime2` | NULL; время обработки; также используется для Failed |
| `LastAttemptAtUtc` | `datetime2` | NULL; время последней попытки |
| `Attempts` | `int` | Обязательно; default 0 |
| `LastError` | `nvarchar(2000)` | NULL; последняя ошибка |

Индекс `(ProcessedAtUtc, CreatedAtUtc)` используется для выборки необработанных
сообщений.

`Payload` сериализуется в JSON с `MoneyJsonConverter`: `Money` передаётся
числом (`decimal`) и восстанавливается через `JsonConverter<Money>`
(round-trip).

### Запись в транзакции

Сообщение записывается в той же транзакции, что и бизнес-операция, до commit, в
девяти точках:

- checkout;
- cancel;
- return;
- ship;
- deliver;
- confirm;
- refund;
- admin stock;
- admin deactivate.

Если транзакция откатывается, сообщение не сохраняется. Admin stock после
`ExecuteUpdate` отсоединяет tracked `Product`, иначе возникает
`DbUpdateConcurrencyException`.

### Доставка внешним подписчикам

`IOutboxSubscriber` определён в Application и не является
`INotificationHandler`. `OutboxDispatcher` резолвит подписчиков из DI и вызывает
их напрямую. `AuditHandler` dispatcher'ом не вызывается: audit остаётся
in-process и работает at-most-once по [ADR-010](ADR-010-audit.md).

`OutboxDispatcher` — `BackgroundService`:

- poll каждые 1 секунду;
- batch до 20 сообщений;
- до 5 попыток;
- backoff 1/2/4/8/16 секунд в зависимости от `Attempts`;
- при `Attempts >= 5` сообщение помечается Failed через `ProcessedAtUtc`;
  отдельного `FailedAtUtc` нет.

Доставка внешним подписчикам — at-least-once. Подписчики должны быть
идемпотентными и корректно обрабатывать повторную доставку. Порядок между
сообщениями не гарантирован.

Cleanup обработанных и Failed записей отсутствует, как и для
`CheckoutAttempt`/`PaymentAttempt`; таблица растёт без автоматической очистки.

## Последствия

### Преимущества

- Внешние подписчики получают события надёжнее, чем при in-process публикации.
- Сообщение и бизнес-изменение фиксируются атомарно.
- In-process audit и его at-most-once-семантика не меняются.

### Компромиссы

- Внешняя доставка at-least-once, audit — at-most-once.
- Возможны дубликаты; подписчики обязаны быть идемпотентными.
- Порядок сообщений не гарантирован.
- Обработанные и Failed записи не очищаются, таблица растёт.
- Появляются фоновая обработка и дополнительная инфраструктура.

### Что не делаем

- Не меняем ADR-010: audit остаётся at-most-once.
- Не используем outbox для audit.
- Не вводим cleanup обработанных или Failed записей.
- Не гарантируем порядок сообщений.
- Не заменяем `IMediator.Publish` в handler'ах.

## Ссылки

- [ADR-007: Domain events](ADR-007-domain-events.md)
- [ADR-010: Аудит](ADR-010-audit.md)
- [events.md](../architecture/events.md)
- [data-model.md §4.13](../architecture/data-model.md#413-outboxmessage)
- [data-model.md §11](../architecture/data-model.md#11-схема-миграций)
