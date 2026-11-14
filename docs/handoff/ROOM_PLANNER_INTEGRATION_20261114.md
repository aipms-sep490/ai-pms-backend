# Gate 3/4 room scheduling integration — 14/11/2026

## Scope and authority

The BE-16 implementation was integrated into `feat/be-access-audit-20261113`, alongside the access-audit and academic-directory scope work. It has **not** been merged into `develop` or deployed. The FE remains on `feat/fe-fresearch-reference-20261113`. The room planner stays behind `VITE_ENABLE_ROOM_PLANNER` until the deployed BE, schema and authenticated role checks are ready.

Preserved rules: Gate 1/2 online; offline Gate 3 follows Gate 2 `PASSED`; Gate 4 is the existing `DefenseSession` and follows Gate 3 `PASSED`. Preview is read-only. Apply rechecks the revision, room and people conflicts, academic windows and working days before publishing. Admin may coordinate the period; staff may act only for the lead department. Room/slot exclusions apply only to the current preview/apply request. No `DEPARTMENT_HEAD` role is introduced.

## Complete local configuration templates

- BE: `src/AIPMS.Api/appsettings.Development.room-planner.example.json` contains connection, CORS, JWT, local storage, mail, notifications, chat, video, AI and rate-limit settings. Copy it to ignored `src/AIPMS.Api/appsettings.Development.json` only if that local file does not already exist; otherwise merge the required values manually. Set a unique local `Jwt__SigningKey` and `ConnectionStrings__DefaultConnection` via environment variables or user secrets. The placeholder database `AI_PMS_LOCAL` is **not** created by copying the file.
- FE: `F:/AI-PMS/ai-pms-frontend-fresearch-20261113/.env.room-planner-local.example` contains all FE flags from `.env.example`, API mode, local proxy `http://localhost:5080`, `VITE_ENABLE_DEFENSE=true`, and `VITE_ENABLE_ROOM_PLANNER=true`. Copy to ignored `.env.local` only for a local BE whose seven BE-16 migrations have been applied. Vite does not load `.example` files directly.
- The local BE template disables scheduled notifications, email delivery, realtime delivery, meeting reminders, chat and other optional outbound integrations. It is for isolated integration verification. To test notification delivery, configure a dedicated local sink and explicitly enable the relevant workers; never point this profile at shared staging.

PowerShell startup after an isolated local database and secrets are prepared:

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
$env:Jwt__SigningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$env:ConnectionStrings__DefaultConnection = '<connection string for an owned local test database>'
dotnet run --project src/AIPMS.Api/AIPMS.Api.csproj --launch-profile http
```

Start FE separately from its worktree with `pnpm dev` after reviewing `.env.local`. Do not use the shared `AI_PMS` database for automated scheduling tests. The existing integration tests use a uniquely named `AI_PMS_TEST_*` database in a SQL Server Testcontainer and clean it up.

## Schema and release gate

The seven ordered BE-16 migrations are registered in `db/e2e/migrations.json`: period calendar, rooms, change requests, review-gate override, gate session scripts, withdrawn appeals, and Gate 3 publication fields. The existing `20261112_reconcile_project_evidence_status.sql` migration is also registered after them; without it, the upgrade path differs from the fresh schema. Verify a fresh bootstrap and upgrade/replay parity on isolated SQL first. On an approved deployment, back up and inventory the target DB, apply migrations through the established release process, then read back schema and representative existing defense records. Deploy BE before setting `VITE_ENABLE_ROOM_PLANNER=true` in the FE build environment.

FE apply now reads `GET /project-periods/{id}/review-gates/r3/schedule` or `GET /project-periods/{id}/defense-sessions` before claiming that a schedule was published. If readback fails or differs from allocations, FE clears the preview to avoid an accidental duplicate apply and asks the operator to reload the published list. Existing per-project defense screens and API routes stay available.

BE apply accepts any valid nonempty subset of preview allocations in one transaction. FE requires the operator to explicitly confirm a partial publication when the preview still has unscheduled projects, then reports the remaining count after readback. The selected subset remains subject to the same BE revision and conflict checks.

## Role flows completed on the working branches

| Role | Entry and actions | Backend authority and readback |
| --- | --- | --- |
| Admin / lead-department staff | Period room planner: configure working days/holidays, view/edit room catalog, preview and publish Gate 3/4, change a future Gate 3 room/slot, review Gate 4 change requests | BE checks role, active department/organization, frozen lead-department scope, gate/window/room conflicts and concurrency tokens. FE reloads the calendar, published schedule, room catalog or request decision before success. |
| Assigned Gate 3 chair/secretary | Review Gate queue and session script; receives Gate 3 schedule notifications | Only the BE-issued assignment authorizes the script. A room/time change is available to Admin/lead staff on the period planner, not from the script. |
| Assigned Gate 4 chair/secretary | Notification opens `/lecturer/defense-sessions/{sessionId}`; view session, choose a catalog room/official slot, submit or withdraw a change request | `GET /api/v1/defense-sessions/{sessionId}/change-requests` and `GET /api/v1/defense-sessions/{sessionId}/change-rooms` require an active chair/secretary of that session. Room options are active and meet group + committee + 5-seat capacity. POST/withdraw still enforce all existing BE checks. |
| Other authorized lecturer / student | Lecturer notification opens the session in read-only mode if BE grants session read; student uses the existing project defense page | The session-officer GETs return 403 for a non-chair/non-secretary; FE does not expose a change form. |

The FE room presentation uses the catalog's building, floor, code, capacity and status. It does not assert physical room coordinates, area or campus geometry that the BE does not store. All 6 slot labels mirror `DefenseSchedulingRules.Slot`; BE remains authoritative for valid time, working days and capacity.

The new session-scoped GET endpoints are in the offline-generated `docs/contracts/cib-v4/openapi.json`. Controlled browser tests are at `e2e/room-planner/room-planner.spec.ts`; they check publish/readback, factual room labels, 403/retry and 390px overflow. They use intercepted fixture responses, not authenticated staging data.

The FE branch vendors the 48 JSON contract examples from this BE branch and updates `contract-lock.json`. The current FE CI workflow checks examples from BE `develop`; its contract-drift job will remain a dependency gate until these BE contracts reach `develop` through the separately approved merge sequence. No CI check is bypassed or weakened on the working branches.

Local verification on 14/11/2026: BE Release build 0 warnings/errors; 1,312 unit tests passed; 43 focused SQL integration tests including fresh/upgrade/replay schema parity passed on Testcontainers; the updated chair-request case passed again after the final authorization/capacity assertions. FE full Vitest suite passed 2,157/2,157 (324 files), later floor-view adjustments passed 10/10 focused tests, FE production build passed, and the controlled Playwright browser cases passed 2/2. These numbers do not imply the 12 role-based staging UAT scenarios were completed.

Controlled SQL and FE fixture tests are evidence for implementation, not authenticated staging UAT. Final UAT must cover Admin, lead-department staff, Gate 3 reviewers, Gate 4 chair/secretary and students; unauthorized cross-department reads, two concurrent applies, room/people overlaps, window/holiday rules, notification delivery, and published readback. Keep the planner flag off until those checks pass on the approved deployment.
