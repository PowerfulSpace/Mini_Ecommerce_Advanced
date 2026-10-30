# Доменные события

## 1. Назначение и границы

Документ описывает доменные события MVP+ Mini E-commerce, момент их публикации
и обработку.

События используются для аудита и уведомлений (этап 21; см. §10). Они не являются
механизмом выполнения платежей, резервов, отмены или возврата. Эти операции
выполняются в основной транзакции и не должны зависеть от асинхронных
handler'ов.

В MVP+ для in-process публикации действует at-most-once:

- события публикуются после успешного commit;
- публикация плоская: событие не порождает новое событие;
- in-process handler выполняется без повторной доставки;
- при сбое handler'а событие или audit-запись может потеряться;
- бизнес-операция не откатывается из-за сбоя handler'а;
- автоматический retry для audit не выполняется.

Outbox введён как этап 20 для внешних подписчиков; см.
[ADR-011: Outbox](../adr/ADR-011-outbox.md). Брокеры сообщений и
гарантированная доставка сверх outbox не входят в MVP+.
Утверждённые решения: [ADR-007: Domain events](../adr/ADR-007-domain-events.md),
[ADR-010: Аудит](../adr/ADR-010-audit.md) и
[ADR-011: Outbox](../adr/ADR-011-outbox.md).

## 2. Где определяются

Domain определяет доменные события как обычные неизменяемые `record`/`class`.
Доменное событие описывает уже произошедший факт и не реализует
`INotification`.

Domain не зависит от MediatR, EF Core и ASP.NET Core. Событие не содержит
инфраструктурных деталей, `DbContext`, HTTP-контекста или MediatR-типов.

Application:

- оборачивает доменное событие в `INotification`-обёртку, например
  `DomainEventNotification<T>`;
- публикует обёртку через MediatR;
- содержит `INotificationHandler` для аудита и будущих уведомлений.

Обёртка и handlers принадлежат Application; Domain содержит только plain-события.
Такое разделение сохраняет независимость Domain от MediatR.

## 3. Когда публикуются

События собираются в рамках операции и публикуются только после успешного
commit транзакции.

Успех — это успешный commit транзакции, а не успешный `SaveChanges` и не
возврат из handler'а. Если commit не состоялся, события не публикуются.

Порядок:

1. Бизнес-операция изменяет состояние и собирает события.
2. Транзакция фиксируется.
3. После commit события передаются в Application-диспетчер.
4. MediatR вызывает `INotificationHandler`.
5. Audit handler пишет `AuditLog` в отдельной транзакции.

Если commit состоялся, но публикация или handler упали, бизнес-операция
остаётся закоммиченной. Событие или аудит может потеряться.

События не публикуются при откате транзакции. События не публикуются до commit.

## 4. Список событий

- `OrderPlaced` — заказ создан в `Created`;
- `OrderPaid` — заказ перешёл в `Paid`;
- `OrderShipped` — заказ перешёл в `Shipped`;
- `OrderDelivered` — заказ перешёл в `Delivered`;
- `OrderCancelled` — заказ отменён полностью или частично;
- `OrderReturned` — заказ перешёл в `Returned`;
- `PaymentConfirmed` — платёж подтверждён;
- `PaymentRefunded` — выполнен refund;
- `StockAdjusted` — admin изменил `Available`;
- `ProductDeactivated` — admin деактивировал товар.

`OrderCancelled` публикуется и при полной, и при частичной отмене. Payload
различает полную и частичную отмену и содержит отменённые позиции или
количества. Отдельное событие `OrderPartiallyCancelled` не вводится.

`PaymentConfirmed` публикуется после успешного confirm. `PaymentRefunded`
публикуется после refund, включая refund внутри cancel/return. `OrderReturned`
и `PaymentRefunded` публикуются после успешного return.

Корзина не порождает доменных событий. Для неё нет событий `CartUpdated`,
`CartItemAdded` и аналогичных.

## 5. Handlers

Handlers используются для:

- аудита по ADR-010;
- будущих уведомлений.

Audit handler создаёт `AuditLog` с `Action`, соответствующим имени события,
`EntityType`, `EntityId`, `ActorUserId`, `OccurredAtUtc`, опциональными
`Payload` и `CorrelationId`.

Handlers не должны:

- порождать новые доменные события;
- вызывать бизнес-команды, которые снова публикуют события;
- выполнять платёж, резерв, отмену или возврат;
- откатывать уже закоммиченную бизнес-операцию;
- полагаться на гарантированную доставку.

Цепочки «событие → событие» запрещены. Один handler не должен превращать
событие в новую бизнес-операцию.

## 6. Порядок и failure policy

События публикуются в порядке их возникновения в рамках операции. Отдельных
гарантий порядка между разными handler'ами нет.

Failure policy:

- at-most-once;
- handler выполняется один раз в рамках попытки публикации;
- при сбое handler'а событие или audit-запись теряется;
- автоматический retry не выполняется;
- бизнес-операция не откатывается;
- потеря аудита документируется как допустимое ограничение MVP+;
- `AuditLog` не используется как источник истины для восстановления заказа,
  платежа или запаса.

Сбой одного handler'а не должен отменять другие уже закоммиченные изменения.
Повторная публикация события не выполняется.

## 7. Транзакции

Бизнес-операция и публикация событий разделены транзакционно:

- checkout, confirm, cancel, return, partial cancel и admin stock фиксируют
  бизнес-состояние в основной транзакции;
- события публикуются после commit;
- audit handler выполняется после commit в отдельной транзакции;
- ошибка audit handler'а не откатывает бизнес-операцию;
- audit-запись не участвует в транзакции заказа, платежа или запаса;
- outbox не используется для audit.

Таким образом, бизнес-состояние и аудит не являются атомарными. Это осознанный
at-most-once. При сбое audit handler'а аудит может отсутствовать, но заказ,
платёж и запас остаются в закоммиченном состоянии.

## 8. Ограничения и outbox

В MVP+:

- outbox введён как этап 20 для внешних подписчиков; см.
  [ADR-011: Outbox](../adr/ADR-011-outbox.md);
- нет брокеров сообщений;
- нет гарантированной доставки сверх outbox;
- нет автоматического retry для audit;
- нет cleanup обработанных и Failed outbox-записей;
- нет фонового восстановления потерянных audit-записей;
- нет цепочек событий;
- нет API чтения событий;
- уведомления реализованы как этап 21 (см. §10);
- аудит работает at-most-once.

Outbox введён как этап 20; см. [ADR-011: Outbox](../adr/ADR-011-outbox.md).
Брокеры и иные механизмы гарантированной доставки вводятся только после
отдельного утверждения и ADR.

## 9. Outbox

С этапа 20 введён outbox: события записываются в `OutboxMessages` в той же
транзакции, что и бизнес-операция; `OutboxDispatcher` доставляет их внешним
подписчикам (`IOutboxSubscriber`) at-least-once. In-process `Publish`/audit
(ADR-010) не меняются — audit остаётся at-most-once. Порядок не гарантирован;
подписчики идемпотентны. См. [ADR-011: Outbox](../adr/ADR-011-outbox.md).

Запись выполняется в девяти точках: checkout, cancel, return, ship, deliver,
confirm, refund, admin stock и admin deactivate. `IOutboxSubscriber` не является
`INotificationHandler`; dispatcher резолвит подписчиков из DI и не вызывает
audit handler. `OutboxDispatcher` опрашивает очередь каждую секунду, берёт до 20
сообщений и делает до 5 попыток с backoff 1/2/4/8/16 секунд. При
`Attempts >= 5` сообщение помечается Failed через `ProcessedAtUtc`; cleanup
обработанных и Failed записей отсутствует.

## 10. Уведомления

С этапа 21 `NotificationSubscriber` (`IOutboxSubscriber`) — первый внешний
подписчик outbox. Уведомления: `INotificationSender` — mock (лог) по умолчанию,
SMTP при `SmtpOptions.Enabled = true`; `SendAsync → TryAdd`; at-least-once,
dedupe best-effort; SMTP failure → retry 5 → Failed; реальный SMTP не
проверялся (нет сервера). См. `DECISIONS.md` §13.

События: `OrderPlaced`, `OrderShipped`, `OrderDelivered`, `OrderCancelled`,
`OrderReturned`, `PaymentConfirmed`, `PaymentRefunded`; `OrderPaid`,
`StockAdjusted`, `ProductDeactivated` — не уведомляются. `InMemoryNotificationStore`
— только дедупликация по (`EventType`, `EntityId`, `OccurredAtUtc`); email
резолвится через `IUserRepository.GetEmailAsync`, при отсутствии — `LogWarning`
и пропуск.
