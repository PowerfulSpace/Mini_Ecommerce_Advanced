# Платежи

## 1. Назначение и границы

Документ описывает платёжную модель MVP+ Mini E-commerce и её место в
архитектуре.

В MVP+ используется mock-провайдер. Реальный платёжный провайдер, Stripe,
вебхуки и reconciliation не входят в объём. Admin refund доступен только в
Development через тестовую аутентификацию `X-Test-User: admin`.

Платёж является частью заказа и не вводит отдельный статус
`PendingPayment` в `OrderStatus`. Заказ после checkout остаётся в `Created`,
пока платёж не подтверждён.

Утверждённые решения:

- [ADR-008: Платежи и идемпотентность](../adr/ADR-008-payments-and-idempotency.md);
- [ADR-004: State machine заказа](../adr/ADR-004-order-state-machine.md);
- [ADR-005: Резервы стока](../adr/ADR-005-stock-reservations.md);
- [ADR-009: Admin API и роли](../adr/ADR-009-admin-api-and-roles.md).

Схема хранения описана в `data-model.md`, §4.8–4.9; `PaymentAttempt` — в §4.10.
HTTP-контракты — в `api.md`, §2.5, §3.5 и §6.2.

## 2. Модель платежа

`Payment` принадлежит заказу; `Payment.OrderId` уникален. У заказа не более
одного платежа. `Order.PaymentId` — nullable-ссылка на `Payment`, которая
заполняется при создании платежа в checkout.

Поля `Payment`:

- `Id`;
- `OrderId` (unique);
- `Amount` (`Money`);
- `RefundedAmount` (`Money`, накопительно);
- `Status`;
- `Provider` (`mock`);
- `ProviderPaymentId` (`mock`);
- `CreatedAtUtc`;
- `ConfirmedAtUtc?`;
- `RefundedAtUtc?`;
- `RowVersion`.

Статусы:

- `Pending` — платёж создан, но не подтверждён;
- `Confirmed` — платёж подтверждён, заказ переходит в `Paid`;
- `Failed` — mock-провайдер вернул неудачу;
- `Refunded` — выполнен полный возврат
  (`RefundedAmount == Amount`).

`Refunded` терминален. `Failed` не терминален для retry: повторный confirm с
тем же `Idempotency-Key` и fingerprint выполняет
`Failed → Pending → Confirmed` для того же `Payment`.

`Payment.Amount` равен сумме заказа. Частичные платежи не поддерживаются.
`Payment.RefundedAmount` накапливается и не превышает `Amount`.

`Refund` — отдельная сущность частичного или полного возврата:

- `Id`;
- `PaymentId`;
- `Amount` (`Money`, `> 0`);
- `RefundedAtUtc`;
- `Reason?`.

Каждый refund, включая полный, создаёт запись `Refund`. Несколько частичных
refund допускаются. `Payment.Status = Refunded` устанавливается только при
полном возврате; частичный возврат оставляет `Confirmed`. Отдельного статуса
`PartiallyRefunded` нет. `Payment.RefundedAtUtc` устанавливается только при
полном возврате.

Инварианты:

- `Payment.Amount >= 0`;
- `Refund.Amount > 0`;
- `Payment.RefundedAmount >= 0`;
- `Payment.RefundedAmount <= Payment.Amount`;
- `Payment.RefundedAmount == SUM(Refund.Amount)`;
- refund не превышает `Payment.Amount - Payment.RefundedAmount`;
- `Payment.Status = Refunded` только при полном возврате.

`Payment.RowVersion` используется для конкурентности. Все денежные значения
представлены `Money` и сохраняются как `decimal(18,2)`; валюта в MVP+ — только
`USD` (ADR-003).

## 3. Создание платежа

Checkout создаёт `Payment` в состоянии `Pending` в той же транзакции, что и
заказ, снимки позиций, резерв выбранных товаров и очистка выбранных позиций
корзины.

Правила:

- checkout оформляет только выбранные позиции;
- `Payment.Amount` равен итоговой сумме созданного заказа;
- `Payment.OrderId` ссылается на созданный заказ;
- `Order.PaymentId` заполняется созданным платежом;
- ответ `201 Created` содержит `paymentId`;
- повторный checkout с тем же `Idempotency-Key` не создаёт второй заказ или
  второй платёж;
- если транзакция checkout откатывается, `Payment` не сохраняется.

`IPaymentProvider.CreatePaymentAsync(orderId, amount, idempotencyKey)` получает
`Idempotency-Key` checkout. Провайдер не создаёт отдельный платёж при повторном
вызове с тем же ключом в рамках успешной идемпотентной попытки.

Заказ создаётся в `Created`, а не в `Paid`. `PendingPayment` в `OrderStatus` не
вводится.

## 4. Подтверждение платежа

`POST /api/payments/{paymentId}/confirm` подтверждает платёж текущего
пользователя.

Обязательные условия:

- доступен только владельцу платежа;
- требует `Idempotency-Key`;
- `If-Match` не требуется;
- mock-провайдер выполняет `Pending → Confirmed`;
- заказ переходит `Created → Paid` через `MarkPaid()`;
- `Order.PaymentId` должен быть заполнен.

Успешная транзакция confirm атомарно:

1. Проверяет `Payment` и `Order`.
2. Переводит `Payment` в `Confirmed`.
3. Уменьшает `Reserved` на количества заказа (ADR-005).
4. Переводит заказ в `Paid`.
5. Завершает запись `PaymentAttempt`.
6. Фиксирует транзакцию.

После commit публикуются `PaymentConfirmed` и `OrderPaid`.

Ошибки и особые случаи:

- платёж не найден или принадлежит другому пользователю —
  `404 payment_not_found`;
- заказ уже не в `Created` — `409 invalid_order_transition`;
- платёж уже `Confirmed` с тем же fingerprint — воспроизводится сохранённый
  успешный ответ;
- платёж уже `Confirmed` с другим fingerprint — `409 payment_already_confirmed`;
- mock вернул `Failed` — `409 payment_failed`;
- `Failed` с тем же ключом и fingerprint — retry
  `Failed → Pending → Confirmed`;
- `Failed` с новым ключом — новая попытка на том же `Payment`, второй
  `Payment` не создаётся;
- `Refunded` терминален; повторный confirm не меняет платёж.

## 5. Возврат

`POST /api/payments/{paymentId}/refund` выполняет административный возврат
средств.

Доступ:

- только admin (`X-Test-User: admin`);
- только Development;
- требуется `Idempotency-Key`;
- `If-Match` не требуется.

Запрос содержит:

- `amount` — положительный `decimal`, не превышающий
  `Payment.Amount - Payment.RefundedAmount`;
- `reason` — необязательная строка.

Правила:

- mock-провайдер выполняет `Confirmed → Refunded` при полном возврате;
- при частичном возврате `Payment` остаётся `Confirmed`;
- для каждой операции создаётся `Refund`;
- `Payment.RefundedAmount` увеличивается на `amount`;
- `Payment.RefundedAtUtc` устанавливается только при полном возврате;
- refund не изменяет заказ и запас;
- admin endpoint является отдельным инструментом ручного возврата.

Refund-сервис, используемый внутри cancel/return, выполняется в одной
транзакции с изменением заказа и запаса. Admin endpoint — отдельная
Development-обёртка; он не выполняет переход заказа и не возвращает запас.

При cancel/return:

- `Paid → Cancelled` выполняется через cancel с refund;
- `Delivered → Returned` выполняется через return с refund;
- refund, изменение запаса и переход статуса фиксируются атомарно;
- при ошибке refund вся транзакция откатывается.

Сумма refund при отмене рассчитывается по снимкам `OrderItem.UnitPrice` и
отменяемому количеству. При полной отмене или полном возврате refund равен
оставшейся сумме; при частичной отмене — пропорционально отменяемой части.

Повторный refund после полного возврата (`Status = Refunded`) возвращает
`409 refund_not_allowed`. Refund больше остатка также возвращает
`409 refund_not_allowed`.

## 6. IPaymentProvider

Интерфейс Application:

```text
CreatePaymentAsync(orderId, amount, idempotencyKey)
ConfirmAsync(paymentId)
RefundAsync(paymentId, amount)
```

`IPaymentProvider` определяется в Application; реализация находится в
Infrastructure. В MVP+ используется mock-реализация.

Mock:

- создаёт `Pending`-платёж;
- подтверждает `Pending → Confirmed`;
- возвращает `Failed` при настроенном сценарии неудачи;
- выполняет refund и сообщает результат;
- не обращается к внешней сети.

Реальный провайдер, Stripe, вебхуки, reconciliation и хранение платёжных
секретов не вводятся (ADR-008).

## 7. Идемпотентность

Confirm и refund используют `PaymentAttempt`, отдельную от `CheckoutAttempt`.

`PaymentAttempt`:

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

Уникальность: `(UserId, IdempotencyKey)`. `PaymentAttempt` покрывает и confirm,
и refund; `CheckoutAttempt` не обобщается.

Fingerprint не включает `Idempotency-Key`: ключ уже является заголовком и
частью области уникальности.

- confirm:
  `SHA-256("POST /api/payments/{id}/confirm\n{id}")` — метод, путь и
  `paymentId`;
- refund:
  `SHA-256("POST /api/payments/{id}/refund\n{id}\n{amount}\n{reason}")` —
  метод, путь, `paymentId` и нормализованные `amount`/`reason`.

Правила:

- завершённая попытка воспроизводит сохранённые статус и тело;
- тот же ключ с другим fingerprint возвращает `409 idempotency_key_reused`;
- `InProgress` использует bounded wait и lease;
- если результат не становится доступным вовремя, возвращается
  `503 idempotency_in_progress`;
- восстановление выполняется по требованию, как в ADR-002;
- фоновый cleanup не вводится;
- окончательный `Failed` для confirm допускает retry с тем же ключом и
  fingerprint;
- для refund `Failed` не применяется, `Refunded` терминален;
- окончательные бизнес-ошибки сохраняются для воспроизведения;
- неожиданные инфраструктурные ошибки не сохраняются как окончательные
  результаты.

Точная схема и процедура восстановления — `data-model.md`, §4.10, ADR-008 и
`api.md`, §6.2.

## 8. Взаимодействие с OrderStatus

Допустимые связи платежа и заказа:

| Операция | Payment | OrderStatus | Запас |
|---|---|---|---|
| Checkout | создаётся `Pending` | `Created` | `Available → Reserved` |
| Confirm | `Pending → Confirmed` | `Created → Paid` | `Reserved` consumed |
| Cancel до оплаты | без refund | `Created → Cancelled` | `Reserved → Available` |
| Cancel после оплаты | refund | `Paid → Cancelled` | `Available +=` |
| Partial cancel после оплаты | частичный refund | статус не меняется | `Available +=` |
| Admin ship | без изменений | `Paid → Shipped` | без изменений |
| Admin deliver | без изменений | `Shipped → Delivered` | без изменений |
| Admin return | refund | `Delivered → Returned` | `Available +=` |

`Created → Paid` возможен только через подтверждение платежа.
`Paid → Cancelled` выполняется через cancel с refund;
`Delivered → Returned` — через return с refund. `Cancelled` и `Returned`
терминальны. `Delivered → Cancelled` не разрешён. См. ADR-004.

Частичная отмена после оплаты не меняет `OrderStatus`; она создаёт
`OrderItemCancellation` и выполняет частичный refund. Частичный возврат после
доставки не поддерживается.

## 9. Ошибки и коды

Ошибки используют `ProblemDetails` со стабильным `code`. Полный перечень и
HTTP-статусы определены в `api.md`, §4.1.

Платёжные и связанные коды:

- `payment_not_found` (`404`);
- `payment_already_confirmed` (`409`);
- `payment_failed` (`409`);
- `refund_not_allowed` (`409`);
- `invalid_order_transition` (`409`);
- `order_already_cancelled` (`409`);
- `idempotency_key_reused` (`409`);
- `idempotency_in_progress` (`503`);
- `forbidden` (`403`) для admin refund;
- `validation_error` (`400`) для суммы, `reason` или тела запроса.

`503` является временным и не означает, что confirm или refund окончательно
завершились неудачно. Он не должен сохраняться как окончательный
бизнес-результат.

## 10. Ограничения

В MVP+:

- нет Stripe и реального провайдера;
- нет вебхуков;
- нет reconciliation;
- нет частичных платежей; `Payment.Amount` равен сумме заказа;
- нет мультивалютности; все суммы — USD;
- нет статуса `PartiallyRefunded`;
- нет частичного возврата после доставки;
- admin refund доступен только в Development;
- нет admin UI;
- нет outbox и гарантированной доставки событий;
- нет TTL и cleanup платежей;
- нет реальной JWT/OAuth-аутентификации;
- нет хранения платёжных секретов и реальных платёжных данных.

Эти ограничения являются осознанными для MVP+. Их расширение требует отдельного
ADR.
