# Mini E-commerce

Учебный full-stack проект интернет-магазина, предназначенный для практического изучения backend-разработки на .NET, работы с реляционной базой данных, транзакций, конкурентного доступа и согласованности данных.

## Статус проекта

**Текущий этап:** Backend (Phase 1–8), frontend (Phase 9–10) и сквозная верификация MVP (Phase 11) завершены.

Backend реализован по Clean Architecture (`PS.MiniEcommerce.Domain`, `.Application`, `.Infrastructure`, `.API`): каталог, корзина, оформление заказа с идемпотентностью и восстановлением, история заказов и отмена. Frontend на Vue 3 + TypeScript реализует каталог, карточку товара, корзину с `ETag`/`If-Match`, checkout с переиспользованием ключа идемпотентности, историю, детали и отмену заказа. Модульные, компонентные и интеграционные тесты покрывают бизнес-инварианты, транзакции, конкурентный доступ и идемпотентность. Последовательность этапов — в `docs/ROADMAP.md`.

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
│   │   └── api.md
│   └── adr/
│       ├── ADR-001-cart-concurrency.md
│       └── ADR-002-checkout-idempotency.md
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
| `docs/architecture/architecture.md` | Архитектура backend и ответственность слоёв |
| `docs/architecture/data-model.md` | Модель данных и ограничения базы |
| `docs/architecture/api.md` | HTTP API и контракты взаимодействия |
| `docs/adr/ADR-001-cart-concurrency.md` | Решения по конкурентному доступу к корзине |
| `docs/adr/ADR-002-checkout-idempotency.md` | Решения по идемпотентности checkout |

`AGENTS.md` содержит инструкции для AI-агентов, работающих с репозиторием.

## Объём MVP

В минимальную версию входят:

- Просмотр каталога товаров.
- Работа с пользовательской корзиной.
- Оформление заказа.
- Просмотр истории заказов.
- Отмена заказа с восстановлением товарных остатков.

В проект не входят платежи, доставка, купоны, микросервисная архитектура, брокеры сообщений и сложная система авторизации.

## Принципы разработки

- Следовать согласованной архитектуре и спецификации.
- Не добавлять функции за пределами согласованного MVP без предварительного обсуждения.
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
2. Создать/обновить схему БД по миграциям, передав строку подключения через переменную окружения `MINIECOMMERCE_DB`:

   ```powershell
   $env:MINIECOMMERCE_DB = "Server=localhost;Database=MiniEcommerce;Trusted_Connection=True;TrustServerCertificate=True;"
   dotnet ef database update --project src/Backend/PS.MiniEcommerce/PS.MiniEcommerce.Infrastructure --startup-project src/Backend/PS.MiniEcommerce/PS.MiniEcommerce.API
   ```

3. Seed-данные (пользователи Alice и Bob, начальный каталог) применяются автоматически при старте API в окружении Development и идемпотентны.

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
$env:MINIECOMMERCE_TEST_DB = "Server=localhost;Database=MiniEcommerce_Test;Trusted_Connection=True;TrustServerCertificate=True;"
dotnet test
```

Frontend:

```powershell
cd src/Frontend/PS.MiniEcommerce
npm run test       # Vitest
npm run lint       # oxlint + eslint
```

### Аутентификация в Development

В окружении Development аутентификация выполняется заголовком `X-Test-User` со значениями `alice` или `bob`. Механизм зарегистрирован только в Development и недоступен в Production; неизвестные значения не аутентифицируют запрос. Во frontend identity выбирается в селекторе "dev identity" в шапке (`Header.vue`), который присутствует только в dev-сборке (`import.meta.env.DEV`). Подробности — в `docs/architecture/api.md` §2.1 и `docs/FRONTEND.md` §4.

### Известные проблемы и отложенные решения

Осознанно принятые и отложенные за пределами MVP отклонения от контракта перечислены в `docs/DECISIONS.md` §13.

## Лицензия

Лицензия проекта: MIT. Текст лицензии находится в файле `LICENSE`.
