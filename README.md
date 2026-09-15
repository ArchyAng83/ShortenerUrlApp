# 🔗 ShortenerUrlApp

Полнофункциональный сервис сокращения URL с аналитикой, QR-кодами и JWT-авторизацией.

## ✨ Возможности

- **Сокращение URL** — генерация коротких 7-символьных кодов
- **Редирект** — мгновенный HTTP 302 redirect по короткому коду
- **Кастомные alias** — пользовательские короткие коды
- **Срок годности** — автоудаление истёкших ссылок
- **Аналитика кликов** — история переходов, геолокация, браузер, графики
- **QR-коды** — генерация QR-кода для каждой ссылки
- **JWT-авторизация** — регистрация, вход, управление ссылками
- **Redis кэширование** — быстрый read-through кэш с TTL
- **Write-behind** — счётчики кликов в Redis, синхронизация в БД каждую минуту

## 🛠 Стек

| Компонент | Технология |
|---|---|
| Backend | ASP.NET Core Web API (.NET 10.0) |
| Frontend | Blazor WebAssembly + MudBlazor |
| Database | PostgreSQL (EF Core) |
| Cache | Redis |
| Auth | ASP.NET Identity + JWT |
| Tests | xUnit + Moq + FluentAssertions |
| Container | Docker + Docker Compose |
| CI/CD | GitHub Actions |

## 🚀 Быстрый старт

### Предварительные требования

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- [Git](https://git-scm.com/)

### Запуск через Docker

```bash
# Клонировать репозиторий
git clone https://github.com/YOUR_USERNAME/ShortenerUrlApp.git
cd ShortenerUrlApp

# Запустить всё
docker-compose up -d

# Приложение доступно:
# API:  http://localhost:5153
# UI:   http://localhost:5209
# Docs: http://localhost:5153/scalar/v1
```

### Запуск локально (без Docker)

```bash
# Запустить PostgreSQL и Redis
docker-compose up -d postgres redis

# Применить миграции
cd ShortenerUrl.WebApi
dotnet ef database update

# Запустить API
dotnet run --launch-profile https

# В новом терминале — запустить UI
cd ShortenerUrlApp.WebUI
dotnet run --launch-profile https
```

### Запуск тестов

```bash
dotnet test
```

## 📁 Структура проекта

```
ShortenerUrlApp/
├── ShortenerUrl.WebApi/          # REST API + Auth + Background Workers
│   ├── Controllers/              # API-контроллеры
│   ├── Data/                     # DbContext + Migrations
│   ├── Entities/                 # Доменные сущности
│   ├── Services/                 # Бизнес-логика
│   └── Program.cs                # Точка входа
│
├── ShortenerUrlApp.Shared/       # Общие DTO и интерфейсы
│   ├── DTOs/
│   └── Interfaces/
│
├── ShortenerUrlApp.WebUI/        # Blazor WASM клиент
│   ├── Pages/
│   ├── Components/
│   └── Services/
│
├── ShortenerUrlApp.Tests/        # Unit + Integration тесты
│   ├── Unit/
│   └── Integration/
│
├── docker-compose.yml
├── .env.example
└── AGENTS.md
```

## 🔌 API Endpoints

### Публичные

| Метод | Путь | Описание |
|---|---|---|
| GET | `/{code}` | Редирект по короткому коду |
| POST | `/api/v1/auth/register` | Регистрация |
| POST | `/api/v1/auth/login` | Вход (JWT) |

### Защищённые (JWT)

| Метод | Путь | Описание |
|---|---|---|
| GET | `/api/v1/urls` | Все URL пользователя |
| POST | `/api/v1/urls` | Создать короткий URL |
| PUT | `/api/v1/urls/{id}` | Обновить URL |
| DELETE | `/api/v1/urls/{id}` | Удалить URL |
| GET | `/api/v1/urls/{id}/analytics` | Аналитика кликов |
| GET | `/api/v1/urls/{id}/qrcode` | QR-код |

## ⚙️ Конфигурация

Переменные окружения (`.env`):

```env
POSTGRES_USER=postgres
POSTGRES_PASSWORD=your_password
POSTGRES_DB=UrlShortenerDb
REDIS_URL=redis:6379
JWT_SECRET=your-super-secret-key-min-32-chars
JWT_ISSUER=ShortenerUrlApp
JWT_AUDIENCE=ShortenerUrlApp
```

## 📄 Лицензия

MIT

## 👤 Автор

[Your Name] — [your@email.com]
