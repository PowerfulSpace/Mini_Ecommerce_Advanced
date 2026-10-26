# Mini E-commerce

Учебный full-stack проект интернет-магазина, предназначенный для практического изучения backend-разработки на .NET, работы с реляционной базой данных, транзакций, конкурентного доступа и согласованности данных.

## Project Status

**Текущий этап:** Backend (Phase 1–8), frontend (Phase 9–10) и сквозная верификация MVP (Phase 11) завершены.

Backend реализован по Clean Architecture (`PS.MiniEcommerce.Domain`, `.Application`, `.Infrastructure`, `.API`): каталог, корзина, оформление заказа с идемпотентностью и восстановлением, история заказов и отмена. Frontend на Vue 3 + TypeScript реализует каталог, карточку товара, корзину с `ETag`/`If-Match`, checkout с переиспользованием ключа идемпотентности, историю, детали и отмену заказа. Модульные, компонентные и интеграционные тесты покрывают бизнес-инварианты, транзакции, конкурентный доступ и идемпотентность. Последовательность этапов — в `docs/en/ROADMAP.md`.

## Project Goals

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

## Technology Stack

### Backend

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

### Frontend

- Vue 3
- TypeScript
- Vite
- Pinia

### Database

Microsoft SQL Server. Локальная разработка выполняется с использованием установленного SQL Server, без обязательного использования Docker.

## Project Structure

```text
MiniEcommerce/
├── AGENTS.md
├── README.md
├── docs/
│   ├── en/
│   │   ├── README.md
│   │   ├── PROJECT.md
│   │   ├── SPEC.md
│   │   ├── DOMAIN.md
│   │   ├── DECISIONS.md
│   │   ├── FRONTEND.md
│   │   ├── ROADMAP.md
│   │   ├── architecture/
│   │   │   ├── architecture.md
│   │   │   ├── data-model.md
│   │   │   └── api.md
│   │   └── adr/
│   │       ├── ADR-001-cart-concurrency.md
│   │       └── ADR-002-checkout-idempotency.md
│   └── ru/
│       ├── README.md
│       ├── PROJECT.md
│       ├── SPEC.md
│       ├── DOMAIN.md
│       ├── DECISIONS.md
│       ├── FRONTEND.md
│       ├── ROADMAP.md
│       ├── architecture/
│       │   ├── architecture.md
│       │   ├── data-model.md
│       │   └── api.md
│       └── adr/
│           ├── ADR-001-cart-concurrency.md
│           └── ADR-002-checkout-idempotency.md
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

Это фактическая структура репозитория. Solution остаётся вложенным в каталог backend-проекта (`src/Backend/PS.MiniEcommerce/PS.MiniEcommerce.slnx`, формат `.slnx`); префикс `PS.` намеренный и сохраняется во всех именах проектов, namespace и путях. Проект `Common` в MVP не вводится. Наполнение solution и остальные настройки выполняются по этапам roadmap.

## Documentation

Проектная документация хранится в каталогах `docs/en/` (источник истины) и `docs/ru/` (перевод-компаньон).

| Document (EN) | Purpose | RU companion |
|---|---|---|
| `docs/en/PROJECT.md` | Цели, границы и общая концепция проекта | `docs/ru/PROJECT.md` (заглушка) |
| `docs/en/SPEC.md` | Функциональные требования и критерии приёмки | `docs/ru/SPEC.md` (заглушка) |
| `docs/en/DOMAIN.md` | Предметная область, бизнес-понятия и инварианты | `docs/ru/DOMAIN.md` (заглушка) |
| `docs/en/DECISIONS.md` | Реестр принятых технических и архитектурных решений | `docs/ru/DECISIONS.md` (заглушка) |
| `docs/en/FRONTEND.md` | Требования и принципы организации frontend | `docs/ru/FRONTEND.md` (заглушка) |
| `docs/en/ROADMAP.md` | Этапы и последовательность реализации | `docs/ru/ROADMAP.md` (заглушка) |
| `docs/en/architecture/architecture.md` | Архитектура backend и ответственность слоёв | `docs/ru/architecture/architecture.md` (заглушка) |
| `docs/en/architecture/data-model.md` | Модель данных и ограничения базы | `docs/ru/architecture/data-model.md` (заглушка) |
| `docs/en/architecture/api.md` | HTTP API и контракты взаимодействия | `docs/ru/architecture/api.md` (заглушка) |
| `docs/en/adr/ADR-001-cart-concurrency.md` | Решения по конкурентному доступу к корзине | `docs/ru/adr/ADR-001-cart-concurrency.md` (заглушка) |
| `docs/en/adr/ADR-002-checkout-idempotency.md` | Решения по идемпотентности checkout | `docs/ru/adr/ADR-002-checkout-idempotency.md` (заглушка) |

`docs/en/` — источник истины для всей проектной документации; `docs/ru/` — перевод-компаньон. При расхождении английской и русской версий верна английская. Русские файлы сейчас являются заглушками; статус перевода указан внутри каждого файла. Корневые `README.md` и `AGENTS.md` находятся вне политики двуязычности.

`AGENTS.md` содержит инструкции для AI-агентов, работающих с репозиторием.

## MVP Scope

В минимальную версию входят:

- Просмотр каталога товаров.
- Работа с пользовательской корзиной.
- Оформление заказа.
- Просмотр истории заказов.
- Отмена заказа с восстановлением товарных остатков.

В проект не входят платежи, доставка, купоны, микросервисная архитектура, брокеры сообщений и сложная система авторизации.

## Development Principles

- Следовать согласованной архитектуре и спецификации.
- Не добавлять функции за пределами согласованного MVP без предварительного обсуждения.
- Не дублировать бизнес-правила между слоями.
- Проверять важные сценарии модульными и интеграционными тестами.
- Учитывать конкурентный доступ и целостность данных.
- Не хранить секреты и чувствительные настройки в исходном коде.
- Обновлять документацию при согласованном изменении требований.

## Getting Started

Порядок работ определяет `docs/en/ROADMAP.md`. Ниже — как собрать, запустить и протестировать проект локально.

### Prerequisites

- **.NET 10 SDK**.
- **Node.js** `^22.18.0 || >=24.12.0` и **npm**.
- **SQL Server** (локальный экземпляр; используется Windows-аутентификация).
- **EF Core CLI** (для применения миграций): `dotnet tool install --global dotnet-ef`.

### Backend Setup

1. Скопировать `src/Backend/PS.MiniEcommerce/PS.MiniEcommerce.API/appsettings.Development.example.json` в `appsettings.Development.json` в том же каталоге и указать строку подключения в `ConnectionStrings:Default`. Файл `appsettings.Development.json` не коммитится.
2. Создать/обновить схему БД по миграциям, передав строку подключения через переменную окружения `MINIECOMMERCE_DB`:

   ```powershell
   $env:MINIECOMMERCE_DB = "Server=localhost;Database=MiniEcommerce;Trusted_Connection=True;TrustServerCertificate=True;"
   dotnet ef database update --project src/Backend/PS.MiniEcommerce/PS.MiniEcommerce.Infrastructure --startup-project src/Backend/PS.MiniEcommerce/PS.MiniEcommerce.API
   ```

3. Seed-данные (пользователи Alice и Bob, начальный каталог) применяются автоматически при старте API в окружении Development и идемпотентны.

### Backend Run

```powershell
dotnet run --project src/Backend/PS.MiniEcommerce/PS.MiniEcommerce.API --urls http://localhost:5269
```

- API: `http://localhost:5269` (`GET /health` — публичная проверка доступности БД).
- Swagger UI доступен только в Development.

### Frontend Setup / Run / Build

```powershell
cd src/Frontend/PS.MiniEcommerce
npm install
npm run dev        # http://localhost:5173 (Vite dev server + proxy /api -> http://localhost:5269)
npm run build      # type-check + production build
npm run preview    # предпросмотр production-сборки
```

Порт dev-сервера фиксирован (`strictPort`), так как backend CORS-политика привязана к `http://localhost:5173`. В production базовый URL API берётся из `VITE_API_URL` (по умолчанию `/api`).

### Tests

Backend (нужна отдельная тестовая БД; имя должно содержать `_Test`):

```powershell
$env:MINIECOMMERCE_TEST_DB = "Server=localhost;Database=MiniEcommerce_Test;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet test
```

Frontend:

```powershell
cd src/Frontend/PS.MiniEcommerce
npm run test       # Vitest
npm run lint       # oxlint + eslint
```

### Development Authentication

В окружении Development аутентификация выполняется заголовком `X-Test-User` со значениями `alice` или `bob`. Механизм зарегистрирован только в Development и недоступен в Production; неизвестные значения не аутентифицируют запрос. Во frontend identity выбирается в селекторе "dev identity" в шапке (`Header.vue`), который присутствует только в dev-сборке (`import.meta.env.DEV`). Подробности — в `docs/en/architecture/api.md` §2.1 и `docs/en/FRONTEND.md` §4.

### Known Issues / Deferred

Осознанно принятые и отложенные за пределами MVP отклонения от контракта перечислены в `docs/en/DECISIONS.md` §13.

## License

Лицензия проекта: MIT. Текст лицензии находится в файле `LICENSE`.
