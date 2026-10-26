# ADR-008: Платежи и идемпотентность

- **Статус:** Принято
- **Дата:** 2026-10-26

## Контекст

После checkout заказ создаётся в `Created`, а оплата должна перевести его в
`Paid`. Нужен минимальный mock-платёж без Stripe, вебхуков и reconciliation, но
с идемпотентностью подтверждения, как у checkout.

Также нужно определить:

- модель `Payment` и его статусы;
- когда создаётся платёж;
- кто и как подтверждает оплату;
- как выполняется refund при отмене и возврате;
- как подтверждение платежа защищается от повторных запросов;
- какие транзакционные границы действуют для payment confirm и refund.

## Решение

### Модель Payment

`Payment` — новая сущность:

- `Id`;
- `OrderId` (unique);
- `Amount` (`Money`);
- `Status` (`Pending`, `Confirmed`, `Failed`, `Refunded`);
- `Provider` (`mock`);
- `ProviderPaymentId` (`mock`);
- `CreatedAtUtc`;
- `ConfirmedAtUtc?`;
- `RefundedAtUtc?`;
- `RowVersion`.

`Order.PaymentId` — nullable FK на `Payment`. `OrderId` в `Payment` уникален:
у заказа не более одного платежа.

Статусы:

- `Pending` — платёж создан, но не подтверждён;
- `Confirmed` — платёж подтверждён, заказ переходит в `Paid`;
- `Failed` — mock-провайдер вернул неудачу;
- `Refunded` — выполнён refund.

`Failed` и `Refunded` терминальны в MVP+.

### Создание платежа

Checkout создаёт `Payment` в `Pending` в той же транзакции, что и заказ, и
возвращает `paymentId` в ответе `201 Created`.

Заказ создаётся в `Created`, а не в `Paid`. `PendingPayment` в `OrderStatus` не
вводится.

`IPaymentProvider.CreatePaymentAsync(orderId, amount, idempotencyKey)` получает
`Idempotency-Key` checkout. Повторный checkout с тем же ключом не создаёт второй
заказ или второй платёж.

### Подтверждение платежа

`POST /api/payments/{id}/confirm`:

- доступен только владельцу платежа; не admin и не «вебхук»;
- требует `Idempotency-Key`;
- mock: `Pending → Confirmed`;
- переводит заказ `Created → Paid` через `MarkPaid()`;
- требует `Order.PaymentId != null` (см. ADR-004).

Успешная транзакция confirm атомарно:

1. Проверяет `Payment` и `Order`.
2. Переводит `Payment` в `Confirmed`.
3. Уменьшает `Reserved` на количества заказа (ADR-005).
4. Переводит заказ в `Paid`.
5. Сохраняет результат идемпотентности.

Если платёж уже `Confirmed`, новый запрос без сохранённого результата
идемпотентности возвращает `409 Conflict` с кодом `payment_already_confirmed`.
Если платёж `Failed` — `409 Conflict` с кодом `payment_failed`. Если заказ уже
не в `Created` — `409 Conflict` с кодом `invalid_order_transition`. Если платёж
не найден или принадлежит другому пользователю — `404 Not Found` с кодом
`payment_not_found`.

### Refund

`POST /api/payments/{id}/refund`:

- admin, только Development;
- mock: `Confirmed → Refunded`;
- сумма может быть полной или частичной;
- используется при `Paid → Cancelled` и `Delivered → Returned`;
- не является отдельным «только денежным» refund без перехода заказа;
- пользовательская отмена `Paid → Cancelled` использует тот же refund-сервис;
  admin endpoint — отдельная Development-обёртка.

Refund выполняется в той же транзакции, что и отмена/возврат:

- `Payment.Confirmed → Refunded`;
- `Available += quantity` (ADR-005);
- `Order → Cancelled` (полная отмена) или `Order → Returned` (ADR-004).

Если refund падает, вся транзакция откатывается: платёж, заказ, отмены и запас
не остаются частично изменёнными.

Сумма refund не может превышать `Payment.Amount` и должна соответствовать
отменяемому/возвращаемому количеству. При полной отмене или полном возврате
refund полный; при частичной отмене — пропорционально снимкам
`OrderItem.UnitPrice`.

В MVP+ `Payment` допускает один refund. После перехода в `Refunded` повторный
refund возвращает `409 Conflict` с кодом `refund_not_allowed`. Частичный refund
также переводит платёж в `Refunded`; отдельного статуса `PartiallyRefunded` нет.
Если потребуются несколько частичных refund, потребуется отдельная модель
refund-записей или дополнительный статус — это расширение MVP+.

### IPaymentProvider

Интерфейс Application:

```text
CreatePaymentAsync(orderId, amount, idempotencyKey)
ConfirmAsync(paymentId)
RefundAsync(paymentId, amount)
```

В MVP+ используется mock-реализация. Реальный провайдер, Stripe, вебхуки и
reconciliation не вводятся.

### Идемпотентность confirm

Для confirm используется отдельная таблица `PaymentAttempt`, не
`CheckoutAttempt`:

- `Id`;
- `UserId`;
- `IdempotencyKey`;
- `RequestFingerprint`;
- `Status` (`InProgress`, `Completed`);
- `PaymentId?`;
- `ResponseStatusCode?`;
- `ResponsePayload?`;
- `LeaseExpiresAtUtc`;
- `CreatedAtUtc`;
- `UpdatedAtUtc`.

Уникальность: `(UserId, IdempotencyKey)`. `PaymentAttempt` относится к
Infrastructure, как `CheckoutAttempt`; `CheckoutAttempt` не обобщается.

Fingerprint не включает `Idempotency-Key`: ключ уже является заголовком и частью
области уникальности. Fingerprint вычисляется как
`SHA-256("POST /api/payments/{id}/confirm")`; `id` платежа входит в путь запроса.
Один и тот же ключ с другим путём даёт другой fingerprint и конфликт
`idempotency_key_reused`.

Повторное использование ключа с другим fingerprint возвращает `409 Conflict` с
кодом `idempotency_key_reused`. Завершённая попытка воспроизводит сохранённый
статус и тело. `InProgress` использует bounded wait и lease; фоновый cleanup не
вводится. Восстановление выполняется по требованию, как в ADR-002.

### Транзакционные границы

- **Confirm:** `Payment.Pending → Confirmed`, `Order.Created → Paid`,
  `Reserved` consumed — одна транзакция.
- **Refund при cancel/return:** `Payment.Confirmed → Refunded`,
  `Available += quantity`, `Order → Cancelled/Returned` — одна транзакция.
- Если операция падает, все изменения откатываются.
- События `PaymentConfirmed`, `PaymentRefunded`, `OrderPaid`,
  `OrderCancelled`, `OrderReturned` публикуются после commit по ADR-007.

### Чего не делаем

- Не вводим `PendingPayment` в `OrderStatus`.
- Не используем Stripe, реальные вебхуки и reconciliation.
- Не обобщаем `CheckoutAttempt`.
- Не добавляем несколько refund на один `Payment`.
- Не добавляем реального провайдера платежей.
- Не делаем частичные платежи: `Payment.Amount` равен сумме заказа.

## Последствия

### Преимущества

- Появляется минимальный, но явный платёжный lifecycle.
- Confirm идемпотентен и защищён от повторных запросов.
- Refund связан с отменой/возвратом и запасом в одной транзакции.
- `CheckoutAttempt` не усложняется обобщением.
- Нет зависимости от внешнего платёжного провайдера.

### Компромиссы

- Нужны `Payment`, `PaymentAttempt`, `Order.PaymentId` и миграции.
- Один refund на платёж ограничивает несколько частичных refund.
- `Failed` терминален: повторная оплата в MVP+ не описана.
- Mock-провайдер не проверяет реальные платежи.
- Нет reconciliation и вебхуков.

### Что ломается

- Checkout теперь создаёт `Payment` и возвращает `paymentId`.
- Заказ после checkout остаётся `Created`, а не сразу `Paid`.
- Появляются новые endpoints, ошибки и транзакционные границы.
- Тесты и API, ожидающие только `Created`/`Cancelled`, требуют обновления.

## Требования к реализации

- Добавить `Payment` с полями и статусами из этого ADR; `OrderId` unique.
- Добавить nullable `Order.PaymentId`; платёж создаётся в checkout в `Pending`.
- Создавать `Payment` и заказ в одной транзакции; возвращать `paymentId` в
  `201 Created`.
- Реализовать `IPaymentProvider` и mock-реализацию.
- Реализовать `POST /api/payments/{id}/confirm` только для владельца и с
  `Idempotency-Key`.
- Реализовать `PaymentAttempt` отдельно от `CheckoutAttempt`, с уникальностью
  `(UserId, IdempotencyKey)`, fingerprint, lease и bounded wait.
- Выполнять confirm, consume `Reserved`, `MarkPaid()` и завершение
  идемпотентности в одной транзакции.
- Реализовать `POST /api/payments/{id}/refund` для admin в Development; refund,
  изменение запаса и переход заказа — в одной транзакции.
- Не допускать refund больше `Payment.Amount`, повторный refund и refund без
  допустимого перехода заказа.
- Публиковать `PaymentConfirmed` и `PaymentRefunded` после commit.
- Добавить тесты на:
  - создание платежа при checkout;
  - успешный confirm и переход в `Paid`;
  - повторный confirm и `payment_already_confirmed`;
  - `payment_failed`;
  - идемпотентность confirm и `idempotency_key_reused`;
  - bounded wait и lease;
  - refund при отмене и возврате;
  - откат при ошибке refund;
  - запрет повторного refund и refund больше суммы;
  - отсутствие `PendingPayment` в `OrderStatus`.
