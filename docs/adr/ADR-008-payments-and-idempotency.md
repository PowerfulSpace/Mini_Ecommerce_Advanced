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
- `RefundedAmount` (`Money`, накопительно);
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
- `Refunded` — выполнен полный refund (`RefundedAmount == Amount`).

`Refunded` терминален.

`Failed` не терминален для retry: повторный `POST /api/payments/{id}/confirm` с
тем же `Idempotency-Key` и fingerprint → `Failed → Pending → Confirmed` (mock).
С новым `Idempotency-Key` — новая попытка на том же `Payment` (не создаёт второй
`Payment`). `Payment.Status = Refunded` — терминален (retry не допускается).

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
Если платёж `Failed`, confirm с тем же ключом и fingerprint выполняет retry
`Failed → Pending → Confirmed`; с новым ключом создаётся новая попытка на том же
`Payment`. Если mock снова возвращает неудачу, возвращается `409 Conflict` с
кодом `payment_failed`. Если заказ уже не в `Created` — `409 Conflict` с кодом
`invalid_order_transition`. Если платёж не найден или принадлежит другому
пользователю — `404 Not Found` с кодом `payment_not_found`.

### Refund

`POST /api/payments/{id}/refund` — admin endpoint, доступный только в
Development. Это ручной возврат: он изменяет только `Payment` и создаёт
`Refund`, но не меняет заказ и запас.

- mock: `Confirmed → Refunded` только при полном возврате; частичный возврат
  оставляет `Confirmed`;
- сумма может быть полной или частичной.

Автоматический refund при `Paid → Cancelled` и `Delivered → Returned`
выполняется внутренним refund-сервисом, не через endpoint, в одной транзакции
с изменением заказа и запаса:

- `Payment.RefundedAmount += amount`; `Payment.Status = Refunded` только при
  полном возврате;
- `Available += quantity` (ADR-005);
- `Order → Cancelled` (полная отмена) или `Order → Returned` (ADR-004).

См. [payments.md](../architecture/payments.md), §5 «Возврат».

Если refund падает, вся транзакция откатывается: платёж, заказ, отмены и запас
не остаются частично изменёнными.

Сумма refund не может превышать `Payment.Amount - Payment.RefundedAmount` и
должна соответствовать отменяемому/возвращаемому количеству. При полной отмене
или полном возврате refund равен оставшейся сумме; при частичной отмене —
пропорционально снимкам `OrderItem.UnitPrice`.

Refund поддерживает несколько частичных возвратов. `Payment` имеет
`RefundedAmount` (`Money`, накопительно) и `Status = Refunded` только при
`RefundedAmount == Amount` (полный возврат). Частичные refund — отдельная таблица
`Refund`: `Id`, `PaymentId`, `Amount` (`Money`), `RefundedAtUtc`, `Reason?`.
Каждый refund, включая полный, создаёт запись `Refund`.
`Payment.RefundedAmount = SUM(Refund.Amount)`. Инвариант:
`RefundedAmount <= Amount`.

Повторный refund после полного возврата (`Status = Refunded`) возвращает
`409 Conflict` с кодом `refund_not_allowed`. Частичные refund не меняют статус
на `Refunded`; отдельного статуса `PartiallyRefunded` нет. `Payment.RefundedAtUtc`
устанавливается при полном возврате.

### IPaymentProvider

Интерфейс Application:

```text
CreatePaymentAsync(orderId, amount, idempotencyKey)
ConfirmAsync(paymentId)
RefundAsync(paymentId, amount)
```

В MVP+ используется mock-реализация. Реальный провайдер, Stripe, вебхуки и
reconciliation не вводятся.

### Идемпотентность confirm и refund

Для confirm и refund используется отдельная таблица `PaymentAttempt`, не
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

Уникальность: `(UserId, IdempotencyKey)`. `PaymentAttempt` покрывает и confirm, и
refund; относится к Infrastructure, как `CheckoutAttempt`; `CheckoutAttempt` не
обобщается.

Fingerprint не включает `Idempotency-Key`: ключ уже является заголовком и частью
области уникальности.

- Для confirm:
  `SHA-256("POST /api/payments/{id}/confirm\n{id}")` — метод, путь и
  `paymentId`.
- Для refund:
  `SHA-256("POST /api/payments/{id}/refund\n{id}\n{amount}\n{reason}")` — метод,
  путь, `paymentId` и нормализованные `amount` и `reason`. Тело refund влияет на
  результат, поэтому повтор с другой суммой не должен воспроизводить старый
  ответ.

Один и тот же ключ с другим fingerprint возвращает `409 Conflict` с кодом
`idempotency_key_reused`. Завершённая попытка воспроизводит сохранённый статус и
тело. Исключение — окончательный `Failed` для confirm: повтор с тем же
ключом/fingerprint выполняет retry по разделу «Статусы». Для refund `Failed` не
применяется: в mock такого состояния нет, а `Refunded` терминален. `InProgress`
использует bounded wait и lease; фоновый cleanup не вводится. Восстановление
выполняется по требованию, как в ADR-002.

### Транзакционные границы

- **Confirm:** `Payment.Pending → Confirmed`, `Order.Created → Paid`,
  `Reserved` consumed — одна транзакция.
- **Refund при cancel/return:** `Payment.RefundedAmount += amount`,
  `Payment.Status = Refunded` только при полном возврате, `Available += quantity`,
  `Order → Cancelled/Returned` — одна транзакция.
- Если операция падает, все изменения откатываются.
- События `PaymentConfirmed`, `PaymentRefunded`, `OrderPaid`,
  `OrderCancelled`, `OrderReturned` публикуются после commit по ADR-007.

### Чего не делаем

- Не вводим `PendingPayment` в `OrderStatus`.
- Не используем Stripe, реальные вебхуки и reconciliation.
- Не обобщаем `CheckoutAttempt`.
- Не добавляем отдельный статус `PartiallyRefunded`; частичные refund
  учитываются в `Refund`.
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

- Нужны `Payment`, `PaymentAttempt`, `Refund`, `Order.PaymentId` и миграции.
- Несколько частичных refund требуют таблицы `Refund` и накопления
  `RefundedAmount`.
- `Failed` не терминален: retry выполняется на том же `Payment`.
- Mock-провайдер не проверяет реальные платежи.
- Нет reconciliation и вебхуков.

### Что ломается

- Checkout теперь создаёт `Payment` и возвращает `paymentId`.
- Заказ после checkout остаётся `Created`, а не сразу `Paid`.
- Появляются новые endpoints, ошибки и транзакционные границы.
- Тесты и API, ожидающие только `Created`/`Cancelled`, требуют обновления.

## Требования к реализации

- Добавить `Payment` с полями и статусами из этого ADR; `OrderId` unique.
- Добавить `Refund` (`Id`, `PaymentId`, `Amount`, `RefundedAtUtc`, `Reason?`) и
  накопительный `Payment.RefundedAmount`.
- Добавить nullable `Order.PaymentId`; платёж создаётся в checkout в `Pending`.
- Создавать `Payment` и заказ в одной транзакции; возвращать `paymentId` в
  `201 Created`.
- Реализовать `IPaymentProvider` и mock-реализацию.
- Реализовать `POST /api/payments/{id}/confirm` только для владельца и с
  `Idempotency-Key`.
- Реализовать `PaymentAttempt` для confirm и refund отдельно от
  `CheckoutAttempt`, с уникальностью `(UserId, IdempotencyKey)`, fingerprint,
  lease и bounded wait.
- Выполнять confirm, consume `Reserved`, `MarkPaid()` и завершение
  идемпотентности в одной транзакции.
- Реализовать `POST /api/payments/{id}/refund` для admin в Development как
  ручной возврат только `Payment`/`Refund`.
- Выполнять автоматический refund при cancel/return внутренним сервисом в одной
  транзакции с изменением заказа и запаса.
- Не допускать refund больше `Payment.Amount`, refund после полного возврата и
  refund при недопустимом статусе платежа.
- Поддерживать несколько частичных refund; `Payment.Status = Refunded`
  устанавливать только при `RefundedAmount == Amount`.
- Публиковать `PaymentConfirmed` и `PaymentRefunded` после commit.
- Добавить тесты на:
  - создание платежа при checkout;
  - успешный confirm и переход в `Paid`;
  - повторный confirm и `payment_already_confirmed`;
  - `payment_failed`;
  - идемпотентность confirm и `idempotency_key_reused`;
  - идемпотентность refund, включая fingerprint с `amount`/`reason`;
  - bounded wait и lease;
  - refund при отмене и возврате;
  - несколько частичных refund и `Refunded` только при полном возврате;
  - откат при ошибке refund;
  - запрет refund после полного возврата и refund больше суммы;
  - retry `Failed → Pending → Confirmed`;
  - отсутствие `PendingPayment` в `OrderStatus`.

## Amendments

- Refund на этапе 19 выполняется in-process через `IPaymentRefundService`;
  `IPaymentProvider.RefundAsync` не вызывается (для будущего реального
  провайдера).
