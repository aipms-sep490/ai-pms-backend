# Baseline v3: execution concurrency and meeting governance (PR1)

This branch contains only PR1: task, milestone, progress report and meeting
concurrency, plus meeting decisions and action items. It builds on `develop`.
Project requirements/history (PR2) and discipline/evaluation work (PR3/PR4) are
separate deliveries. No frontend changes are included.

## Migration and rollout

Apply `db/changes/20260927_add_governance_baseline_v3.sql` before deploying this
backend. The additive, rerunnable script adds a non-null GUID concurrency token
to existing execution rows and creates meeting decision/action tables, constraints
and indexes. `db/schema.sql` includes the same foundation for new databases.
No shared `AI_PMS` database was modified during PR validation.

Set `ExecutionConcurrency:RequireTokens=true` in test/staging. The default is false
for existing clients. Enable it in production only after clients send tokens.
A supplied malformed or stale token always returns 409, even in compatibility
mode. Mutation, token rotation, audit and database notifications share a transaction.
Clients must reload after 409, rather than retrying with the same token.

Detail/list DTOs expose `concurrencyToken` as a GUID string. Resource creation
does not require a token. Existing execution mutations accept tokens as follows:

| Mutation | Token location |
| --- | --- |
| Task update/status/add dependency | Request body |
| Task delete/assignees/remove dependency | Query string |
| Milestone update | Request body |
| Milestone delete | Query string |
| Milestone reorder | Every item in the request array |
| Progress report update | Request body |
| Progress report submit/feedback | Query string |
| Meeting update/notes | Request body |
| Meeting cancel/complete/delete/participants/feedback | Query string |

Existing actor authorization and execution-window checks still apply. Mutations
require an ACTIVE project. Team/project locks precede child writes in a consistent
order. A stale item rolls back the entire milestone reorder batch. Compatibility
writes still rotate tokens so token-aware clients detect intervening changes.

## Meeting endpoints

| Method and route | Contract |
| --- | --- |
| GET `/api/v1/meetings/{meetingId}/decisions` | Paged decision history |
| POST `/api/v1/meetings/{meetingId}/decisions` | `content`, meeting `concurrencyToken` |
| GET `/api/v1/meetings/{meetingId}/action-items` | Paged action items |
| POST `/api/v1/meetings/{meetingId}/action-items` | Action input with meeting token |
| PUT/PATCH `/api/v1/meetings/{meetingId}/action-items/{id}` | Full action input with action token |

Reads use `page=1&pageSize=20`; pageSize is limited to 100. They require project
access. Writes require an active account that is the organizer and still a team
member, the current team leader, or an assigned supervisor. Cancelled meetings
are read-only. Decisions are append-only and require a COMPLETED meeting.

Action input includes `title` (1..500 characters), `description` (up to 4000),
optional `assigneeUserId`, optional `dueAt`, `status`, and `concurrencyToken`.
New actions start OPEN. OPEN/IN_PROGRESS may transition to DONE/CANCELLED;
terminal actions cannot reopen. Assignment requires an active member or assigned
supervisor of the same project; out-of-scope assignment returns 403.
Meeting/action tokens rotate after successful writes. Reload the meeting before
creating another decision/action. Audit failures roll back all writes.

## Validation

`GovernanceBaselineEndpointTests` covers concurrent writers for all four execution
resources, strict/compatibility rollout, stale reorder rollback, participant token
rotation, meeting decisions/actions, out-of-scope denial, audit rollback and
Swagger routes/DTOs. Domain tests cover meeting states and terminal action states.

SQL tests use `AIPMS_TEST_SQL_CONNECTION` supplied through the process environment.
The fixture creates and deletes only its own `AI_PMS_TEST_<guid>` databases; the
supplied catalog is not a mutation or cleanup target. Without that setting it uses
Testcontainers. Bootstrap also replays the additive migration to check rerunnability.

Issue #74 and PR #76 are now merged. PR2 submit/revision/resubmit integration is
documented in [Project requirements and review history](project-requirements-review-history.md).
The validation numbers below describe PR1 at its original merge date.

Validation on 2026-09-28: solution build with warnings as errors passed with
0 warnings/errors; 790 unit tests and 842 integration tests passed, none skipped.
The integration suite used owned SQL LocalDB databases and included the Swagger
contract checks. Shared production data was not used for mutation tests.
