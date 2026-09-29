# Baseline v3 PR3: discipline responsibility and evidence ledger

This backend-only change adds structured major responsibilities, task discipline
classification, and a project evidence ledger. It does not replace the existing
task file evidence or file download routes.

## Routes

| Method | Route | Purpose |
| --- | --- | --- |
| GET/PUT | `/api/v1/teams/{teamId}/major-requirements/{majorId}/responsibilities` | Read or atomically replace live team responsibilities |
| GET | `/api/v1/projects/{projectId}/major-requirements/{majorId}/responsibilities` | Read live draft/revision data or immutable submission snapshot |
| GET/PUT | `/api/v1/tasks/{taskId}/disciplines` | Read or replace task major roles |
| GET/POST | `/api/v1/projects/{projectId}/evidence` | List or add references to project sources |

Responsibility replacement and task discipline replacement require the aggregate
concurrency token returned by GET. A stale token returns `409` and has no audit
or partial write. Evidence creation is idempotent for the same project/source/
major key; a retry with different notes returns `409`.

## Data rules

- A responsibility is owned by an existing `team_major_requirements` row. It is
  trimmed, nonempty, at most 2,000 characters, and has a distinct nonnegative
  order. Clearing the list is supported while the team/project is editable.
- A task discipline has one major and role `PRIMARY` or `SUPPORTING`. A task
  has at most one primary. New interdisciplinary tasks require one primary;
  legacy unclassified tasks remain readable. A major must be in the project's
  authoritative requirements/snapshot.
- Evidence uses one source type: `TASK`, `DELIVERABLE`, `MEETING`,
  `PROGRESS_REPORT`, or `FILE`. The server resolves the source and project;
  client-supplied actor, timestamp, and verification status are ignored. Optional
  majorId is validated against project requirements and, for TASK or a task file,
  against its task disciplines. New rows are `PENDING` and the ledger stores references,
  never copied binary content.
- Source and project state are checked in the same serializable transaction.
  Active projects are writable; sources with CANCELLED, CLOSED, LOCKED or
  ARCHIVED status cannot receive new evidence. Submitted/completed work can be
  referenced without editing its contents. FKs prevent hard deletion of a
  referenced source; source status changes do not remove ledger history.

## Scope matrix

| Operation | Allowed actor |
| --- | --- |
| Read | Active project member, assigned lecturer/mentor, scoped staff, or Admin |
| Edit team responsibility | Active leader, or an active primary/major-matched mentor while project is in revision |
| Edit task discipline | Active leader, primary supervisor, scoped mentor for all touched majors, or member assigned to the task |
| Add evidence | Same writer rules as task discipline; a member must be assigned to the source task |

Responsibility writes require a non-LOCKED/non-DISBANDED team, with no submitted
or executing project. Only DRAFT/REVISION_REQUIRED project data is touched; frozen
history for rejected/archived projects is retained. A scoped supervisor must have
an active assignment to an editable project. The project read route has no PUT;
it cannot override the team source or frozen registration snapshot.

Example responsibility PUT:

```json
{"concurrencyToken":"guid-from-get","items":[{"content":"Design the API","sortOrder":0}]}
```

Example task discipline PUT (also pass the same items as `disciplines` in task POST):

```json
{"concurrencyToken":"task-guid-from-get","items":[{"majorId":10,"role":"PRIMARY"},{"majorId":20,"role":"SUPPORTING"}]}
```

Example evidence POST:

```json
{"sourceType":"TASK","sourceId":123,"majorId":10,"notes":"Implementation evidence"}
```

POST returns 200 both for creation and an identical retry. Notes are trimmed,
optional, up to 2,000 characters. GET supports `sourceType`, `majorId`,
`verificationStatus=PENDING|UNKNOWN`, `page` and `pageSize` (1-100), sorted by
`submittedAt DESC, id DESC`. A null major returns `classification=UNCLASSIFIED`.
SourceType is the evidence type in this version; no independent unvalidated type
taxonomy or verification endpoint is introduced. There is no fabricated verified
actor/time. Later task reclassification does not rewrite ledger history.

401 means missing authentication; 403 denied role/scope; 404 missing resource;
400 malformed data/filter; 409 stale token, locked state, out-of-scope major or
conflicting duplicate. Audit failures fail the operation and roll back data/tokens.
Audit actions are `TEAM_MAJOR_RESPONSIBILITIES_REPLACED`,
`TEAM_ELIGIBILITY_INVALIDATED`, `TASK_DISCIPLINES_REPLACED`, and
`PROJECT_EVIDENCE_CREATED`. An identical evidence retry writes no new audit.

Role claims are checked against active persisted users, assignments, and team
membership. An ended assignment or left member immediately loses access. Outside
projects return `403`/`404` according to the existing project/file access
convention; source resolution never discloses another project's metadata.

## Snapshot and migration behavior

When a project is submitted, responsibilities are copied into the existing
immutable registration snapshot. A later revision captures a new list; old
review history is never overwritten. Changing live responsibilities invalidates
the existing eligibility check through the responsibility version/hash.
Legacy snapshot fields remain null (`isAvailable=false`), while new empty
collections are explicitly available. Live task rows without discipline are
UNCLASSIFIED; no backfill invents evidence or verification. File reads/downloads
still use their existing authorization checks; a ledger row never grants access.

`db/changes/20260929_add_discipline_evidence.sql` is additive and rerunnable.
It creates `team_major_responsibilities`, `task_disciplines`, and
`project_evidence`, adds the responsibility version column, and deliberately
does not guess major or verification values for legacy rows. The same DDL is in
`db/schema.sql`, the isolated SQL bootstrap, and the E2E migration manifest.
Apply the change to an isolated database first; do not run mutation tests on
the shared `AI_PMS` database.

Entity classes are scaffolded from the isolated SQL database; partial DbContext
mapping retains existing project/user/source types and adds SQL defaults,
unique indexes, foreign keys and token metadata. No EF migration is used.
Rollback keeps the new tables/snapshots. Stop classification/registration writes
before deploying an older API, which cannot enforce the new discipline rules.
The production migration has not been applied as part of this implementation.

## Verification

```powershell
dotnet build src/AIPMS.Api/AIPMS.Api.csproj -c Release -warnaserror
dotnet test tests/AIPMS.UnitTests -c Release --no-build
dotnet test tests/AIPMS.IntegrationTests -c Release --no-build
```

The integration fixture owns a disposable `AI_PMS_TEST_<guid>` database. It
covers source/project scope, active membership, mentor major scope, stale
tokens, audit rollback, duplicate evidence, stable pagination, migration rerun,
Swagger routes, and compatibility for single-major legacy tasks.

Validation on 2026-09-29: Release solution build with warnings as errors passed
with 0 warnings/errors; all 875 unit tests and 951 integration tests passed.
The complete SQL run used two parallel xUnit collections on LocalDB; each
concurrency test still runs its competing requests in parallel. E2E bootstrap
creation, rerun, schema readiness, stable aliases/password hashes, ownership and
checksum guards passed. Test processes disabled notification/scheduled workers;
no external email/Drive provider or production database was used.

Dependency: PR2 (#86) supplies project requirement and review history contracts.
Merge PR2, then PR3, then the policy/evaluation work in #85. PR3 belongs to Khai
(#84); Dong owns PR4. Before PR2 is merged, this PR targets its feature branch so
that the review diff contains only PR3; retarget to develop after the prerequisite.
