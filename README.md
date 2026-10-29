# Mini E-commerce

Учебный full-stack проект интернет-магазина в рамках MVP+ (M1–M14), предназначенный для практического изучения backend-разработки на .NET, работы с реляционной базой данных, транзакций, конкурентного доступа и согласованности данных.

## Статус проекта

**Статус:** MVP (этапы 0–11) завершён, MVP+ (M1–M8, M12, M13; этапы 12–19) реализован; код соответствует MVP+. Границы объёма и осознанные упрощения — в `RATIONALE.md` и `docs/DECISIONS.md` §13.

MVP+ (M1–M8, M12, M13; этапы 12–19) реализован и проверен (build, unit, integration, frontend, миграции, check-doc-links); M9, M10, M11, M14 — вне объёма. Известные ограничения — docs/DECISIONS.md §13.

Backend реализован по Clean Architecture (`PS.MiniEcommerce.Domain`, `.Application`, `.Infrastructure`, `.API`). Код соответствует MVP+ (M1–M8, M12, M13). Frontend на Vue 3 + TypeScript реализует каталог, карточку товара, корзину с `ETag`/`If-Match`, checkout с переиспользованием ключа идемпотентности, историю, детали и отмену заказа. Модульные, компонентные и интеграционные тесты покрывают бизнес-инварианты, транзакции, конкурентный доступ и идемпотентность. Последовательность этапов — в `docs/ROADMAP.md`.

## Цели проекта

Проект предназначен для практики следующих инженерных задач:

- Построение backend-приложения на ASP.NET Core с использованием Clean Architecture.
- Разделение бизнес-логики, прикладных сценариев и инфраструктуры.
- Использование MediatR для обработки команд и запросов.
- Работа с SQL Server и Entity Framework Core.
- Реализация транзакционных операций.
- Обработка конкурентного доступа к корзине и товарным остаткам.
- Обеспечение идемпотентности оформления заказа.
- Написание модульных и интеграционных тестов.
- Создание frontend-приложения с использованием Vue 3 и TypeScript.

## Функциональные возможности

- Каталог товаров и пользовательская корзина.
- Выбор позиций корзины через `IsSelected` и частичный checkout только выбранных товаров.
- Запас в виде `Available` + `Reserved`; `Reserved` не отдаётся публично.
- Шесть статусов заказа: `Created`, `Paid`, `Shipped`, `Delivered`, `Cancelled`, `Returned`.
- Mock-платежи: подтверждение, идемпотентность и refund.
- Полная и частичная отмена с восстановлением запаса и refund.
- Admin API: deactivate, stock, ship, deliver, return.
- Доменные события и аудит at-most-once.
- Идемпотентность оформления заказа и платежей.

## Технологический стек

### Бэкенд

- .NET 10
- ASP.NET Core Web API — Controllers
- Clean Architecture
- MediatR
- Entity Framework Core
- Microsoft SQL Server
- FluentValidation
- xUnit
- NSubstitute
- Microsoft.AspNetCore.Mvc.Testing

### Фронтенд

- Vue 3
- TypeScript
- Vite
- Pinia

### База данных

Microsoft SQL Server. Локальная разработка выполняется с использованием установленного SQL Server, без обязательного использования Docker.

## Структура проекта

```text
MiniEcommerce/
├── AGENTS.md
├── README.md
├── docs/
│   ├── README.md
│   ├── PROJECT.md
│   ├── SPEC.md
│   ├── DOMAIN.md
│   ├── DECISIONS.md
│   ├── FRONTEND.md
│   ├── ROADMAP.md
│   ├── architecture/
│   │   ├── architecture.md
│   │   ├── data-model.md
│   │   ├── api.md
│   │   ├── payments.md
│   │   └── events.md
│   └── adr/
│       ├── ADR-001-cart-concurrency.md
│       ├── ADR-002-checkout-idempotency.md
│       ├── ADR-003-money-value-object.md
│       ├── ADR-004-order-state-machine.md
│       ├── ADR-005-stock-reservations.md
│       ├── ADR-006-partial-checkout-and-cancellation.md
│       ├── ADR-007-domain-events.md
│       ├── ADR-008-payments-and-idempotency.md
│       ├── ADR-009-admin-api-and-roles.md
│       └── ADR-010-audit.md
├── src/
│   ├── Backend/
│   │   └── PS.MiniEcommerce/
│   │       ├── PS.MiniEcommerce.slnx
│   │       ├── PS.MiniEcommerce.Domain/
│   │       ├── PS.MiniEcommerce.Application/
│   │       ├── PS.MiniEcommerce.Infrastructure/
│   │       └── PS.MiniEcommerce.API/
│   └── Frontend/
└── tests/
    ├── PS.MiniEcommerce.UnitTests/
    └── PS.MiniEcommerce.IntegrationTests/
```

Это фактическая структура репозитория. Solution остаётся вложенным в каталог backend-проекта (`src/Backend/PS.MiniEcommerce/PS.MiniEcommerce.slnx`, формат `.slnx`); префикс `PS.` намеренный и сохраняется во всех именах проектов, namespace и путях. Проект `Common` в MVP+ не вводится. Наполнение solution и остальные настройки выполняются по этапам roadmap.

## Документация

Проектная документация хранится в каталоге `docs/`.

| Документ | Назначение |
|---|---|
| `docs/PROJECT.md` | Цели, границы и общая концепция проекта |
| `docs/SPEC.md` | Функциональные требования и критерии приёмки |
| `docs/DOMAIN.md` | Предметная область, бизнес-понятия и инварианты |
| `docs/DECISIONS.md` | Реестр принятых технических и архитектурных решений |
| `docs/FRONTEND.md` | Требования и принципы организации frontend |
| `docs/ROADMAP.md` | Этапы и последовательность реализации |
| `RATIONALE.md` | Границы объёма и осознанные упрощения |
| `docs/architecture/architecture.md` | Архитектура backend и ответственность слоёв |
| `docs/architecture/data-model.md` | Модель данных и ограничения базы |
| `docs/architecture/api.md` | HTTP API и контракты взаимодействия |
| `docs/architecture/payments.md` | Платёжная модель, confirm и refund |
| `docs/architecture/events.md` | Доменные события и аудит |
| `docs/adr/ADR-001-cart-concurrency.md` | Решения по конкурентному доступу к корзине |
| `docs/adr/ADR-002-checkout-idempotency.md` | Решения по идемпотентности checkout |
| `docs/adr/ADR-003-money-value-object.md` | Денежный value object `Money` |
| `docs/adr/ADR-004-order-state-machine.md` | Шесть статусов и переходы заказа |
| `docs/adr/ADR-005-stock-reservations.md` | Резервы запаса `Available`/`Reserved` |
| `docs/adr/ADR-006-partial-checkout-and-cancellation.md` | Частичный checkout и отмена |
| `docs/adr/ADR-007-domain-events.md` | Доменные события и их публикация |
| `docs/adr/ADR-008-payments-and-idempotency.md` | Платежи и идемпотентность |
| `docs/adr/ADR-009-admin-api-and-roles.md` | Admin API и роли |
| `docs/adr/ADR-010-audit.md` | Аудит |

`AGENTS.md` содержит инструкции для AI-агентов, работающих с репозиторием.

## Объём MVP+ (M1–M14)

MVP+ включает M1–M8, M12 и M13:

- Каталог товаров и пользовательская корзина.
- Выбор позиций и частичный checkout только выбранных товаров.
- Резервы запаса `Available`/`Reserved`.
- Шесть статусов заказа.
- Mock-платежи, подтверждение и refund.
- Полная и частичная отмена.
- Admin API: deactivate, stock, ship, deliver, return.
- Доменные события и аудит at-most-once.
- Идемпотентность checkout и платежей.

Исключено: M9 (уведомления), M10 (промо/налоги), M11 (реальная JWT/OAuth), M14 (reconciliation), Stripe, вебхуки, outbox, TTL резервов, admin UI, мультивалютность.

## Принципы разработки

- Следовать согласованной архитектуре и спецификации.
- Не добавлять функции за пределами согласованного MVP+ без предварительного обсуждения.
- Не дублировать бизнес-правила между слоями.
- Проверять важные сценарии модульными и интеграционными тестами.
- Учитывать конкурентный доступ и целостность данных.
- Не хранить секреты и чувствительные настройки в исходном коде.
- Обновлять документацию при согласованном изменении требований.

## Начало работы

Порядок работ определяет `docs/ROADMAP.md`. Ниже — как собрать, запустить и протестировать проект локально.

### Предварительные требования

- **.NET 10 SDK**.
- **Node.js** `^22.18.0 || >=24.12.0` и **npm**.
- **SQL Server** (локальный экземпляр; используется Windows-аутентификация).
- **EF Core CLI** (для применения миграций): `dotnet tool install --global dotnet-ef`.

### Настройка backend

1. Скопировать `src/Backend/PS.MiniEcommerce/PS.MiniEcommerce.API/appsettings.Development.example.json` в `appsettings.Development.json` в том же каталоге и указать строку подключения в `ConnectionStrings:Default`. Файл `appsettings.Development.json` не коммитится.
2. Создать/обновить схему БД по всем миграциям MVP+, передав строку подключения через переменную окружения `MINIECOMMERCE_DB`:

   ```powershell
   $env:MINIECOMMERCE_DB = "Server=localhost;Database=MiniEcommerce_Advanced;Trusted_Connection=True;TrustServerCertificate=True;"
   dotnet ef database update --project src/Backend/PS.MiniEcommerce/PS.MiniEcommerce.Infrastructure --startup-project src/Backend/PS.MiniEcommerce/PS.MiniEcommerce.API
   ```

3. Seed-данные (пользователи Alice, Bob и admin, начальный каталог) применяются автоматически при старте API в окружении Development и идемпотентны.

### Запуск backend

```powershell
dotnet run --project src/Backend/PS.MiniEcommerce/PS.MiniEcommerce.API --urls http://localhost:5269
```

- API: `http://localhost:5269` (`GET /health` — публичная проверка доступности БД).
- Swagger UI доступен только в Development.

### Настройка, запуск и сборка frontend

```powershell
cd src/Frontend/PS.MiniEcommerce
npm install
npm run dev        # http://localhost:5173 (Vite dev server + proxy /api -> http://localhost:5269)
npm run build      # type-check + production build
npm run preview    # предпросмотр production-сборки
```

Порт dev-сервера фиксирован (`strictPort`), так как backend CORS-политика привязана к `http://localhost:5173`. В production базовый URL API берётся из `VITE_API_URL` (по умолчанию `/api`).

### Тесты

Backend (нужна отдельная тестовая БД; имя должно содержать `_Test`):

```powershell
$env:MINIECOMMERCE_TEST_DB = "Server=localhost;Database=MiniEcommerce_Advanced_Test;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet test
```

Frontend:

```powershell
cd src/Frontend/PS.MiniEcommerce
npm run test       # Vitest
npm run lint       # oxlint + eslint
```

### Аутентификация в Development

В окружении Development аутентификация выполняется заголовком `X-Test-User` со значениями `alice`, `bob` или `admin`. Механизм зарегистрирован только в Development и недоступен в Production; неизвестные значения не аутентифицируют запрос. `admin` получает роль admin только для Development admin endpoints; admin UI отсутствует. Во frontend identity выбирается в селекторе "dev identity" в шапке (`Header.vue`), который присутствует только в dev-сборке (`import.meta.env.DEV`). Подробности — в `docs/architecture/api.md` §2.1, `docs/FRONTEND.md` §4 и `RATIONALE.md`.

### Известные проблемы и зафиксированные отклонения

Известные проблемы (отложенные) и зафиксированные отклонения — см. `docs/DECISIONS.md` §13.

- Платежи — mock; Stripe, реальные вебхуки и reconciliation отсутствуют.
- Аутентификация и admin endpoints доступны только в Development; реальная JWT/OAuth-аутентификация отсутствует.
- Outbox и гарантированная доставка событий отсутствуют; аудит работает at-most-once.
- TTL, cleanup и фоновое освобождение резервов отсутствуют.
- Admin UI, уведомления, промо и мультивалютность не реализованы.

## Лицензия

Лицензия проекта: MIT. Текст лицензии находится в файле `LICENSE`.
