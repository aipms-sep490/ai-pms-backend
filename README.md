# AI-PMS Backend

ASP.NET Core backend for the AI-Powered Academic Project Progress Management System.

## Architecture

The solution follows Clean Architecture with feature-based use cases:

```text
Api -> Application -> Domain
Api -> Infrastructure -> Application / Domain
Api -> AI -> Application
```

Domain has no dependency on another AI-PMS project. Application groups code by business feature instead of global technical folders.

## Projects

- `AIPMS.Api`: REST controllers, middleware, OpenAPI and composition root.
- `AIPMS.Application`: use cases, DTOs and external abstractions grouped by feature.
- `AIPMS.Domain`: entities, state machines, value objects, events and domain exceptions.
- `AIPMS.Infrastructure`: SQL Server persistence and external adapters.
- `AIPMS.AI`: progress analysis, recommendation and model-provider adapters.
- `AIPMS.UnitTests`: domain, application, AI and architecture tests.
- `AIPMS.IntegrationTests`: API-level tests.

## Run locally

Requirements: .NET 8 SDK. SQL Server is only required for endpoints that access persistence.

Copy `src/AIPMS.Api/appsettings.example.json` to `src/AIPMS.Api/appsettings.json`, then adjust the local connection string. Real `appsettings*.json` files are intentionally ignored by Git.

```powershell
dotnet restore AIPMS.sln
dotnet run --project src/AIPMS.Api
```

Swagger is available at `http://localhost:5080/swagger`. The initial endpoints are:

- `GET /api/system`
- `GET /api/projects/lifecycle`
- `POST /api/ai/insights/progress`

To start SQL Server and Redis, copy `.env.example` to `.env`, change the local password, then run `docker compose up -d`.

## Database migrations

```powershell
dotnet ef migrations add InitialCreate --project src/AIPMS.Infrastructure --startup-project src/AIPMS.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/AIPMS.Infrastructure --startup-project src/AIPMS.Api
```

Override `ConnectionStrings__DefaultConnection` for environments that do not use Windows authentication.

## Rules for feature work

1. Put commands, queries, DTOs and validators under the owning feature.
2. Put business invariants and state transitions in Domain.
3. Put SQL, identity, storage, email and provider details in Infrastructure or AI.
4. Add an interface only for a real boundary or multiple meaningful implementations.
5. Every state-changing endpoint needs authorization, validation and a test before it is complete.
