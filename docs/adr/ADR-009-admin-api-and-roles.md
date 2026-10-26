# ADR-009: Admin API и роли

- **Статус:** Принято
- **Дата:** 2026-10-26

## Контекст

Нужны административные операции над товарами и заказами: деактивация товара,
изменение доступного запаса, отгрузка, доставка и возврат. Реальная
аутентификация не входит в MVP+, поэтому роль admin должна работать через
существующий Development-механизм `X-Test-User`.

Нужно определить:

- как представлена роль admin;
- какие endpoints доступны;
- какие переходы статусов и изменения запаса они выполняют;
- как admin API изолирован от Production;
- какие события и audit-записи порождаются.

## Решение

### Роль admin

`X-Test-User: admin` — третий предварительно созданный пользователь с ролью
admin.

- Механизм доступен только в Development.
- В Production admin-аутентификация и admin endpoints не регистрируются.
- Запрос от `alice` или `bob` к admin endpoint возвращает `403 Forbidden` с
  кодом `admin_forbidden`.
- Неизвестный `X-Test-User` не аутентифицирует запрос.
- Seed admin-пользователя должен быть идемпотентным и не создавать дублей.

### Endpoints

Все admin endpoints доступны только в Development и требуют роль admin:

```http
POST /api/admin/products/{id}/deactivate
POST /api/admin/products/{id}/stock
POST /api/admin/orders/{id}/ship
POST /api/admin/orders/{id}/deliver
POST /api/admin/orders/{id}/return
```

### Деактивация товара

`POST /api/admin/products/{id}/deactivate` устанавливает `Product.IsActive =
false`.

- Операция идемпотентна: повторная деактивация не меняет результат.
- Деактивация разрешена при наличии активных заказов.
- Снимки названия и цены в существующих заказах не меняются.
- Деактивированный товар нельзя добавить в корзину или оформить; существующие
  позиции корзины остаются видимыми как недоступные и могут быть удалены.
- Публикуется событие `ProductDeactivated`.

### Admin stock

`POST /api/admin/products/{id}/stock` устанавливает абсолютное значение
`Product.Available`.

- `Available >= 0`.
- `Reserved` не изменяется.
- Уменьшение `Available` ниже нуля возвращает `409 Conflict` с кодом
  `insufficient_reserved`.
- Операция должна быть атомарной и не терять конкурентные изменения
  `Available`.
- Для optimistic concurrency используется `Product.RowVersion`; при конфликте
  допускается ограниченное число повторных попыток. Точный код ответа при
  исчерпании повторов определяется в `api.md`.
- Публикуется событие `StockAdjusted`.

### Ship

`POST /api/admin/orders/{id}/ship` выполняет `Paid → Shipped` через `Ship()`
(ADR-004).

- Если заказ не в `Paid`, возвращается `409 Conflict` с кодом
  `order_already_shipped`.
- Публикуется событие `OrderShipped`.

### Deliver

`POST /api/admin/orders/{id}/deliver` выполняет `Shipped → Delivered` через
`Deliver()` (ADR-004).

- Если заказ не в `Shipped`, возвращается `409 Conflict` с кодом
  `invalid_order_transition`.
- Публикуется событие `OrderDelivered`.

### Return

`POST /api/admin/orders/{id}/return` выполняет `Delivered → Returned` через
`Return()` (ADR-004).

- Возвращается весь заказ; частичный возврат после доставки в MVP+ не
  поддерживается.
- Refund выполняется по ADR-008: `Payment.Confirmed → Refunded`.
- Запас возвращается в `Available` на количества заказа (ADR-005).
- Refund, изменение запаса и переход статуса выполняются в одной транзакции.
- Если refund падает, вся операция откатывается.
- Если заказ не в `Delivered`, возвращается `409 Conflict` с кодом
  `invalid_order_transition`.
- Если refund невозможен, возвращается `409 Conflict` с кодом
  `refund_not_allowed`.
- Публикуются события `OrderReturned` и `PaymentRefunded`.

### Аудит и события

- `ProductDeactivated`, `StockAdjusted`, `OrderShipped`, `OrderDelivered`,
  `OrderReturned`, `PaymentRefunded` публикуются после commit по ADR-007.
- AuditLog заполняется через handlers по ADR-010.
- Admin API не изменяет статусы, кроме ship/deliver/return.
- Admin UI не входит в MVP+.

## Последствия

### Преимущества

- Появляется минимальный admin API без реальной auth-инфраструктуры.
- Роль admin изолирована в Development.
- Ship/deliver/return используют доменные методы и события.
- Admin stock не смешивается с резервами.
- Деактивация не ломает исторические заказы.

### Компромиссы

- `X-Test-User: admin` — учебный механизм, не production auth.
- Admin endpoints недоступны в Production.
- Admin stock требует optimistic concurrency и обработки конфликтов.
- Возврат после доставки — только полный.
- Admin UI отсутствует.

### Что не делаем

- Не вводим JWT/OAuth и реальные роли.
- Не добавляем admin UI.
- Не разрешаем admin менять произвольные статусы.
- Не добавляем частичный возврат после доставки.
- Не добавляем управление пользователями или ролями.

## Требования к реализации

- Добавить предварительно созданного пользователя `admin` и роль admin;
  seed должен быть идемпотентным.
- Зарегистрировать `X-Test-User` с `admin` только в Development.
- Не регистрировать admin endpoints в Production.
- Проверять роль admin и возвращать `admin_forbidden` для не-admin.
- Реализовать deactivate, stock, ship, deliver, return с указанными
  guard'ами и событиями.
- Для admin stock использовать абсолютное `Available`, не трогать `Reserved`,
  обеспечить optimistic concurrency и отсутствие потерянных обновлений.
- Для return выполнять refund, возврат запаса и смену статуса в одной
  транзакции; при ошибке откатывать всё.
- Публиковать события после commit.
- Добавить тесты на:
  - доступ admin и `403` для alice/bob;
  - недоступность admin endpoints в Production;
  - deactivate и идемпотентность;
  - admin stock и отрицательный `Available`;
  - ship/deliver/return и недопустимые статусы;
  - return с refund и откат при ошибке;
  - события и audit-записи.
