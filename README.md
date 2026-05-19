# AkademVault-API

Бекенд для студентського застосунку **AkademVault** — спільний простір академічної групи: розклад, дедлайни, матеріали лекцій, чат, запрошення та AI-помічники. Дипломний проєкт.

## Стек

- **.NET 10** / ASP.NET Core (Web API + SignalR + GraphQL через HotChocolate)
- **PostgreSQL** (Neon) через EF Core 10 + Npgsql
- **Cloudflare R2** (S3-сумісне сховище, AWS SDK)
- **OpenRouter** як шлюз до LLM (модель за замовчуванням `anthropic/claude-haiku-4-5`) — для дайджестів та парсингу розкладу
- Cookie-автентифікація (HttpOnly) + CSRF через antiforgery
- BCrypt для паролів, Scalar/Swagger для OpenAPI

## Можливості

- **Auth** — реєстрація, логін, профіль, зміна пароля (cookie + antiforgery)
- **Groups** — створення груп, членство, kick/leave, короткі коди приєднання
- **Invitations** — особисті запрошення + інвайт-лінки за токеном
- **Requests** — заявки на приєднання до групи з approve/reject
- **Schedule** — CRUD розкладу + AI-парсинг файлу розкладу (`parse` → `confirm`)
- **Planner** — завдання/дедлайни групи + тижневий перегляд
- **Storage** — завантаження лекційних матеріалів у R2 + коментарі (з тредами)
- **Messenger** — груповий чат через SignalR (`/hubs/chat`), позначки прочитаного
- **Notifications** — push через SignalR (`/hubs/notifications`)
- **Digest** — AI-резюме активності групи
- **GraphQL** — read-only ендпоінт `/graphql` для важких екранів (dashboard, матеріал з деревом коментарів)

## Структура

```
Controllers/   REST-ендпоінти (Auth, Group, Invitation, Request,
               Schedule, Planner, Storage, Messenger, Notification, Digest)
Models/        EF-сутності
Data/          AppDbContext
DTOs/          контракти запитів/відповідей
Services/      R2StorageService, OpenRouterClient, ScheduleParser,
               NotificationService, ShortCodeGenerator, antiforgery filter
Hubs/          ChatHub, NotificationHub (SignalR)
GraphQL/       HotChocolate Query + типи
Migrations/    EF Core міграції
Tests/         xUnit-тести (окремий csproj)
```

## Запуск

1. Скопіюй `.env.example` у `.env` та заповни значення (БД, R2, OpenRouter, CORS).
2. Застосуй міграції:
   ```bash
   dotnet ef database update
   ```
3. Запуск:
   ```bash
   dotnet run
   ```
   Swagger UI доступний у Development-режимі. Скалярні ендпоінти SignalR:
   `/hubs/chat`, `/hubs/notifications`. GraphQL: `/graphql`.

## Конфігурація

Усі секрети читаються з `.env` через `DotNetEnv`. Перелік змінних — у `.env.example`:

- `DB_*` — підключення до PostgreSQL (Neon, `SSL Mode=require`)
- `R2_*` — Cloudflare R2 (account id, ключі, бакет, endpoint)
- `OPENROUTER_API_KEY`, `OPENROUTER_MODEL`, `OPENROUTER_BASE_URL`
- `APP_BASE_URL` — публічний URL цього API
- `CORS_ALLOWED_ORIGINS` — кома-розділений список (Angular SPA, Capacitor iOS/Android)

## Безпека

- Cookie `AkademVault.Auth` — HttpOnly, SameSite=Lax; на unauth повертається `401 JSON` (без редіректу — для SPA).
- Antiforgery: HttpOnly-cookie + заголовок `X-XSRF-TOKEN`. `JsonAntiforgeryFilter` застосовується до MVC; для `/graphql` CSRF-перевірка дублюється у middleware.
- Глобальний exception handler віддає структуроване JSON-тіло 500.

## Тести

```bash
dotnet test Tests/Tests.csproj
```

Тести покривають Auth, Groups, Invitations, JoinRequests, Schedule, Planner, Storage, Messenger, Notifications, Digest, формат помилок, smoke-тести R2/OpenRouter/ScheduleParser та SignalR.
