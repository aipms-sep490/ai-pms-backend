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

Copy `src/AIPMS.Api/appsettings.example.json` to `src/AIPMS.Api/appsettings.json`, then adjust the local connection string. Environment-specific examples are provided for Development, Staging and Production. Real `appsettings*.json` files are intentionally ignored by Git.

```powershell
dotnet restore AIPMS.sln
dotnet run --project src/AIPMS.Api
```

Swagger is available at `http://localhost:5080/swagger`. The initial endpoints are:

- `GET /api/system`
- `GET /api/projects/lifecycle`
- `POST /api/ai/insights/progress`

To start SQL Server and Redis, copy `.env.example` to `.env`, change the local password, then run `docker compose up -d`.

## Database First workflow

The SQL Server schema is the source of truth. EF Core reverse engineering writes only to `Infrastructure/Persistence/Generated`; do not use migrations to create or update the shared database.

Generated models stay inside Infrastructure. Repositories use them for persistence and mappers convert them to Domain entities or Application projections. Never add business logic to a generated file.

Set `ConnectionStrings:DefaultConnection` with User Secrets or the `ConnectionStrings__DefaultConnection` environment variable, then run:

```powershell
dotnet tool restore
./scripts/scaffold-database.ps1 -Force
```

`-Force` replaces the bootstrap context and overwrites files generated for the current schema. If a table was removed or renamed, delete its stale generated model after reviewing the diff. The repository pins `dotnet-ef` to the same EF Core 8 patch used by Infrastructure so all team members generate consistent output.

## Configuration and logging

- CORS and observability settings are bound to strongly typed options and validated when the application starts.
- Invalid or missing CORS origins stop startup instead of failing during a request.
- Serilog writes human-readable console events and rolling compact-JSON files under `logs/` by default.
- Override nested settings with environment variables such as `Cors__AllowedOrigins__0` and `Observability__MinimumLevel`.

## Error responses

The global exception middleware returns RFC-compatible `ProblemDetails` with a trace id. Application exceptions map consistently: validation to 400, forbidden to 403, not found to 404, conflict to 409, domain-rule violations to 422 and unexpected failures to 500.

## Validation and dependency injection

- Commands and queries run through MediatR; FluentValidation validators execute automatically before their handlers.
- Each composition-aware layer exposes one registration method: `AddApplication()`, `AddInfrastructure()`, `AddAI()` and `AddApi()`.
- MediatR handlers, validators and stateless AI services are transient. EF Core `DbContext` remains scoped. Options and CORS configuration use framework-managed singleton registrations.
- Domain intentionally has no dependency-injection registration because it contains pure business code and references no framework.

## Rules for feature work

1. Put commands, queries, DTOs and validators under the owning feature.
2. Put business invariants and state transitions in Domain.
3. Put SQL, identity, storage, email and provider details in Infrastructure or AI.
4. Add an interface only for a real boundary or multiple meaningful implementations.
5. Every state-changing endpoint needs authorization, validation and a test before it is complete.
