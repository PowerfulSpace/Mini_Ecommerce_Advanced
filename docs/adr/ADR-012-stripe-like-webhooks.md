# ADR-012: Stripe-like вебхуки

- **Статус:** Принято
- **Дата:** 2026-10-31

## Контекст

MVP+ использует только mock-провайдер: confirm выполняется синхронно через
`IPaymentProvider`, вебхуки и реальный провайдер не входят в объём
([ADR-008](ADR-008-payments-and-idempotency.md)).

Этап 23 добавляет учебный Stripe-like режим, чтобы отработать асинхронное
подтверждение платежа внешним событием: HMAC-подпись вебхука, идемпотентность
событий и подтверждение платежа после фактического commit checkout. Это не
интеграция со Stripe: Stripe.net, реальные ключи, ClientSecret и reconciliation
не вводятся.

## Решение

### Feature flag и двухидейный checkout

Режим управляется `Stripe:Enabled` (`StripeOptions`). При `Enabled = false`
поведение совпадает с MVP+. При `Enabled = true`:

- checkout по-прежнему создаёт `Payment` в `Pending` в основной транзакции
  вместе с заказом;
- после успешного commit checkout выполняет отдельную транзакцию инициализации:
  `Payment.Provider = Stripe:ProviderName` (по умолчанию `stripe`) и
  `ProviderPaymentId = pi_mock_<guid>`;
- ошибка инициализации логируется и не откатывает уже закоммиченный заказ;
  платёж остаётся с mock-провайдером.

Провайдерские идентификаторы хранятся в `Payment.Provider` и
`Payment.ProviderPaymentId`. Unique index по `ProviderPaymentId` отфильтрован
условием `Provider = 'stripe'`, поэтому mock-платежи не конфликтуют.

### Webhook endpoint

`POST /api/webhooks/stripe` — анонимный endpoint в Development и Production.
Тело читается как raw body; подпись передаётся в заголовке `Stripe-Signature`
в формате `t=<unix>,v1=<hex>[,v1=<hex>...]`.

Проверка подписи:

- HMAC-SHA256 по строке `{timestamp}.{payload}` с `Stripe:WebhookSecret`;
- `Stripe:SignatureToleranceSeconds` (по умолчанию 300) ограничивает возраст
  timestamp;
- принимается любая из переданных `v1`-подписей; сравнение выполняется
  fixed-time;
- при `Enabled = false` endpoint отвечает `200` без проверки подписи и без
  изменений — это no-op для выключенного режима.

Ответы: `200` — событие принято, обработано или осознанно пропущено;
`400` — отсутствует/некорректна подпись, timestamp вне допуска или тело не
является JSON-объектом с `id` и `type`. Ошибки не раскрывают детали секрета.

### ProcessedStripeEvents

Таблица `ProcessedStripeEvents` хранит факт обработки события:

| Свойство | Тип SQL | Требования |
|---|---|---|
| `Id` | `uniqueidentifier` | Первичный ключ |
| `EventId` | `nvarchar(200)` | Обязательно; уникально |
| `Type` | `nvarchar(100)` | Обязательно |
| `ProcessedAtUtc` | `datetime2` | Обязательно; UTC |

Уникальный индекс по `EventId` даёт идемпотентность: повторная доставка
события возвращает `200` без повторного изменения платежа, заказа, запаса и
без повторной публикации событий. Запись `ProcessedStripeEvent` и бизнес-изменение
фиксируются в одной транзакции. Гонка вставок обрабатывается как unique
violation SQL Server (`2601`/`2627`) и трактуется как уже обработанное событие.

Cleanup обработанных событий не выполняется.

### payment_intent.succeeded

Обработка события:

1. Найти `Payment` по `ProviderPaymentId` из `data.object.id`.
2. Проверить, что событие относится к Stripe-платежу и сумма совпадает с
   `Payment.Amount`, валюта — `usd`, а заказ находится в `Created`.
3. Под `UPDLOCK` заблокировать `Payment`, затем `Order`.
4. Выполнить `MarkConfirmed`, списать `Reserved`, перевести заказ в `Paid`,
   записать outbox-сообщения и `ProcessedStripeEvent` в одной транзакции.
5. После commit опубликовать in-process события `PaymentConfirmed` и `OrderPaid`
   по правилам [ADR-007](ADR-007-domain-events.md); audit остаётся
   at-most-once по [ADR-010](ADR-010-audit.md).

Событие, которое не удаётся применить (платёж не найден, неверная сумма или
валюта, заказ не в `Created`, платёж уже `Confirmed`/`Failed`/`Refunded`),
считается обработанным: оно записывается в `ProcessedStripeEvents` и
возвращает `200`, чтобы провайдер не повторял доставку. Неподдерживаемые типы
также подтверждаются и записываются.

При `Enabled = true` синхронный `POST /api/payments/{paymentId}/confirm`
возвращает `409 payment_confirm_not_supported`: подтверждение выполняет только
вебхук.

### charge.refunded и refund.updated

События `charge.refunded` и `refund.updated` подтверждаются и записываются в
`ProcessedStripeEvents`, но не меняют `Payment` или `Refund`. Refund в
Stripe-like режиме остаётся синхронным mock-вызовом:

- `PaymentRefundService` создаёт `Refund` в существующей транзакции
  admin refund / cancel / return;
- если `Payment.Provider` совпадает с `Stripe:ProviderName`, сервис
  устанавливает `Refund.ProviderRefundId = re_mock_<guid>`;
- для mock-платежа `ProviderRefundId` остаётся `null`.

Таким образом, `ProviderRefundId` появляется из mock refund, а не из вебхука.
Реальное обновление состояния по `charge.refunded` отнесено к этапу 24.

### Границы

- Stripe.net и реальные платежные SDK не вводятся — этап 24.
- ClientSecret и платёжные формы не вводятся — этап 24.
- Reconciliation, retry и порядок доставки вебхуков — этап 24.
- Реальный `charge.refunded`, обновляющий `Refund`/`Payment`, — этап 24.
- Мультивалютность не вводится: поддерживается только `usd`.

## Последствия

### Преимущества

- Асинхронное подтверждение платежа внешним событием отработано без реального
  провайдера.
- Идемпотентность события обеспечена уникальностью `EventId` и транзакцией.
- Выключенный режим полностью сохраняет поведение MVP+.
- Подпись и tolerance проверяются до разбора и обработки события.

### Компромиссы

- Checkout становится двухидейным: инициализация провайдера выполняется после
  commit и может не удаться, оставив mock-платёж.
- События, которые не удалось применить, всё равно помечаются обработанными;
  повторная доставка их не исправит.
- `charge.refunded` и `refund.updated` — no-op; источник истины по refund —
  синхронный mock-вызов.
- Таблица `ProcessedStripeEvents` растёт без cleanup.
- Confirm через HTTP недоступен при `Enabled = true`.

## Ссылки

- [ADR-007: Domain events](ADR-007-domain-events.md)
- [ADR-008: Платежи и идемпотентность](ADR-008-payments-and-idempotency.md)
- [ADR-010: Аудит](ADR-010-audit.md)
- [payments.md](../architecture/payments.md)
- [data-model.md §4.8, §4.9, §4.14](../architecture/data-model.md)
- [api.md §3.5, §3.7](../architecture/api.md)
- [events.md §4](../architecture/events.md)
