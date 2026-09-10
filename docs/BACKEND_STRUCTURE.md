# Backend folder conventions

The backend keeps its existing dependency direction:

```text
Api -> Application -> Domain
Api -> Infrastructure -> Application / Domain
Api -> AI -> Application
```

## Application

Organize business code under `Features/<Feature>` using `Commands`, `Queries`,
`Validators`, `DTOs`, `Models`, `Abstractions` and `Services`. Create folders only
when they contain working code. Namespaces match the actual directory.

- Keep a command/query and its handler together or in adjacent files in the same
  folder. Use the request type name for a single-request file.
- Keep all validators in the feature's `Validators` directory. Assembly scanning
  in `AddApplication` registers handlers and validators; no manual per-type
  registration is required.
- `DTOs` contains API contracts; `Models` contains internal read models and
  repository projections. A folder move does not rename JSON properties.
  Map internal models and Domain values to response DTOs before returning them
  through the API. Teams uses `TeamMemberDto` and `TeamInvitationDto` for this
  boundary, preserving the existing JSON fields.
  OpenAPI schema names follow the new DTO type names.
- `Abstractions` contains feature-specific interfaces. Interfaces shared across
  unrelated features belong in `Application/Abstractions`.
- `Services` contains orchestration and access checks. Pure business rules live
  in Domain, while database queries remain in Infrastructure.
- `Common` contains cross-feature exceptions, behaviors, paging and security
  constants. It is not a second home for feature-specific code.

Existing grouped files such as `SemesterCommands.cs` remain valid: they group
related operations within one feature role. There is no requirement to split
every record or handler into a separate file.

## Infrastructure

```text
Persistence/
  Configuration/    Database settings
  Generated/        Scaffolded context and entities
  Mappers/          Database-to-domain/application mapping
  Repositories/     All repository implementations, including Auth
Identity/           Password hashing, tokens and account-token validation
Services/
  Auditing/         Audit adapters
  Projects/         Project access and execution guards
  Teams/            Team policy adapters
Email/              Email delivery and settings
```

Do not add another root-level `Repositories` directory. Do not manually reorganize
`Persistence/Generated`: the scaffold configuration owns those paths.

## Domain and tests

Domain groups feature-specific entities, statuses and framework-independent rules
under `Projects`, `Teams` and `Tasks`. For example, `Projects` owns `Project`,
`ProjectStatus` and `ProjectStateMachine`. Shared exceptions remain in `Exceptions`.
Keep dependencies directed toward Domain.

Unit tests follow the tested layer (`Application`, `Domain`, `Infrastructure`,
`AI`, `Architecture`); feature subfolders can group related tests. Integration
tests exercise API and SQL behavior. Database integration tests use isolated
test databases, not the shared development database.

## Mapping from the previous layout

| Previous location | Current location |
| --- | --- |
| `Features/Teams/*Command.cs`, `*Query.cs` | `Features/Teams/Commands`, `Queries` |
| `Features/Teams/TeamContracts.cs` | `Teams/DTOs`, `Models`, `Abstractions` |
| `Features/Teams/TeamWorkflow.cs`, guard implementation | `Teams/Services` |
| `Auth/Commands/<Operation>`, `Auth/Queries/<Operation>` | `Auth/Commands`, `Auth/Queries` |
| `Projects/Queries/GetProjectLifecycle/` | `Projects/Queries` |
| `ProgressReports/Commands/AnalyzeProgress/` | `ProgressReports/Commands`, `Validators` |
| Validators embedded in command/query files | The feature's `Validators` folder |
| `Features/Tasks/Domain` | `AIPMS.Domain/Tasks` |
| Domain `Entities/Project*`, `Enums/ProjectStatus.cs` | `AIPMS.Domain/Projects` |
| `Infrastructure/Identity/AuthRepository.cs` | `Infrastructure/Persistence/Repositories` |
| Project guards in `Infrastructure/Identity` | `Infrastructure/Services/Projects` |
| Audit and team adapters directly under `Infrastructure/Services` | `Services/Auditing`, `Services/Teams` |

This reorganization preserves routes, serialized contracts, validation rules,
transactions and business behavior. The configured team policy adapter remains
configured; connecting it to persisted BE-12 policy is a separate behavior change.

For pending feature branches, follow this convention when resolving imports and
service registrations after rebasing onto the refactor. Supervisors PR #32 is
separate from this change; do not copy its implementation into this branch.
