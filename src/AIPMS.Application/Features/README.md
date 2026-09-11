# Feature modules

Each module owns its commands, queries, DTOs, validators and application services. A module should expose the smallest surface needed by the API and other modules.

Use these folders consistently, creating only the ones the feature needs:

| Folder | Contents |
| --- | --- |
| `Commands` | Mutation requests and their MediatR handlers. |
| `Queries` | Read requests and their MediatR handlers. |
| `Validators` | FluentValidation validators and feature validation helpers. |
| `DTOs` | API request/response contracts and DTO mappers. |
| `Models` | Internal application projections and repository snapshots. |
| `Abstractions` | Feature-specific repository, policy-provider and guard interfaces. |
| `Services` | Workflows, access checks and application orchestration. |

Namespaces follow the folder: `AIPMS.Application.Features.<Feature>.<Folder>`.
Place requests directly in `Commands` or `Queries`; do not add another directory
per operation. Name single-request files after the request (`CreateTeamCommand.cs`,
`GetTeamQuery.cs`). A request and handler may share a file; related operations may
also share a clearly named feature file. Validators always belong in `Validators`.

Keep pure business rules in `AIPMS.Domain`, not a `Domain` subfolder inside
Application. Cross-feature contracts stay in `Application/Abstractions`; shared
behaviors, exceptions, paging and role constants stay in `Application/Common`.

See [the backend structure guide](../../../docs/BACKEND_STRUCTURE.md) for
Infrastructure, tests and the mapping from the previous layout.
