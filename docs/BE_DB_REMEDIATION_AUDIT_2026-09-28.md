# AI-PMS — Canonical Backend and Database Remediation Audit

**Audit date:** 2026-09-28
**Backend baseline:** `develop` / `3749b8b082d38f3f6da89fffd4071245b4ed0a73`
**Audience:** Backend, Database, QA and release owners
**Replaces:** `BE_DB_IMPLEMENTATION_BACKLOG.md`, `PHASE6_PHASE7_BE_DB_AUDIT_2026-09-28.md`, `PHASE7_EVALUATOR_ASSIGNMENTS_BE_DB_AUDIT.md`, and the obsolete FE copy `BE_DATABASE_GAPS_AUDIT_2026-09-27.md`.

## 1. Executive decision

The current backend has a substantial implementation base, but it is **not end-to-end ready for mock/acceptance flows that cross Final Submission, Evaluation, Result, Archive, or concurrent execution edits**.

| Area | Code/schema status | Operational status | Decision |
| --- | --- | --- | --- |
| Registration to ACTIVE and normal execution | Implemented contracts exist. | Existing live data can support selected read/execution checks. | `PARTIAL` |
| Phase 6 — Final Submission | Tables and migrations exist. | No live Phase 6 rows, no active final window, no `FINAL_SUBMISSION` project. | `PARTIAL / NOT_E2E_READY` |
| Phase 7 — Evaluator, result and publication | Narrow lecturer/supervisor flow exists. | No evaluation window, assignment, draft state, finalization, policy or result. | `PARTIAL / NOT_E2E_READY` |
| Archive | Command and project history query exist. | Backend workflow-actions contract does not expose archive eligibility. | `FE_DONE_BE_PENDING` |
| AI/Risk advisory | Project analysis, report summary and one-turn assistant endpoints exist. | Requires authorized live actor/data/429 verification. | `PARTIAL` |
| Lost-update protection | Project has a concurrency token. | Task, milestone, progress report and meeting contracts do not expose a token. | `BLOCKED` for trustworthy concurrent-edit acceptance |
| Full evaluation governance | Not implemented by the current narrow assignment model. | No scheme/scope/student-result model. | `BLOCKED` pending approved domain design |

`200 /api/v1/system`, a successful build, a table existing, or a frontend screen rendering are **not** E2E evidence. A slice is `DONE` only after a protected API path persists against an isolated SQL test database and demonstrates the relevant success, `403`, and `409` behavior.

## 2. Scope, method and safety boundary

This audit inspected the current controllers, DTOs, handlers, infrastructure authorization, migration scripts and tests at the baseline above. It also consolidates the approved **SELECT-only** live SQL observations taken on 2026-09-28 with `ApplicationIntent=ReadOnly`; no migration, seed, update or delete was run.

Do not store connection strings, passwords or test credentials in this file, source control, fixture output or logs. Use User Secrets or the approved release-secret mechanism. The target production/shared database must never be used for destructive mock/E2E data setup.

## 3. Facts already verified

### 3.1 Current Phase 6/7 schema and live-data snapshot

The target database was `ONLINE` and `READ_WRITE`; the audit connection itself was read-only.

| Verified item | Observation | Consequence |
| --- | --- | --- |
| Phase 6 tables | `final_submission_drafts`, draft items, requirements, requirement items, `final_submissions`, submission items all exist; all had **0 rows**. | Schema presence does not prove a final-submission journey. |
| Phase 7/result tables | `evaluation_assignments`, draft states, finalizations, result policies/items, results and result evaluations all exist; all had **0 rows**. | No evaluator/result journey is operationally demonstrated. |
| Referential integrity | Checked Phase 6/7 foreign keys: **0 disabled, 0 untrusted**. | Existing relationships are deployable foundations. |
| Assignment integrity | Active `(project, evaluator, evaluation type)` uniqueness exists; type is restricted to `LECTURER`/`SUPERVISOR`. | Current model is deliberately narrow. |
| Project states | 1 `ACTIVE`, 2 `APPROVED`, 0 `FINAL_SUBMISSION`. | Phase 6/7 cannot begin through valid state prerequisites. |
| Calendar | Final submission was `UPCOMING` (2026-12-11 to 2026-12-31); no `EVALUATION` period existed. | Both write windows are closed/unavailable. |
| Final-package prerequisite | 0 final-submission-ready projects and no locked package. | Assignment/scoring must remain blocked. |
| Legacy evaluations | 3 legacy evaluations (draft/submitted/finalized) and 10 detail rows; none map to `evaluation_draft_states`. | Do not infer or silently backfill managed assignment links. |
| Role counts | 2 ADMIN, 2 DEPARTMENT_STAFF, 4 LECTURER, 11 STUDENT role holders. | Counts are not proof of department/supervisor eligibility. |

### 3.2 Current backend contracts that FE can rely on

| Capability | Current contract / behavior |
| --- | --- |
| Workflow actions | `GET /api/v1/workflow-context/projects/{projectId}/actions` returns backend-derived actions and a project concurrency token. It currently has no `archive_project` action. |
| Final submission | Draft/requirements/locked package flow is persisted by the existing Phase 6 migrations and guarded by window/state/package rules. |
| Evaluator assignment | `POST/GET /api/v1/projects/{projectId}/evaluation-assignments`, `GET /api/v1/evaluation-assignments/my`, `POST /api/v1/evaluation-assignments/{id}/revoke`, then evaluator draft/finalize endpoints. Server derives rubric; request only carries evaluator, period and `LECTURER`/`SUPERVISOR` type. |
| Result/Archive | Project result policy/result persistence and `POST /api/v1/projects/{id}/archive`, `GET /api/v1/projects/{id}/history` exist. Archive is staff/admin only, department scoped, and server rejects a project that is not `COMPLETED`. |
| AI/Risk | `GET /api/v1/projects/{id}/progress-analysis`, report summary, and project-aware one-turn assistant endpoints are authenticated and project-scoped. |
| Project concurrency | Project DTO/workflow action returns a concurrency token. |

Relevant applied scripts already present under `db/changes` are:

1. `20260909_add_project_period_policy_fields.sql`
2. `20260911_add_evaluation_assignments_and_drafts.sql`
3. `20260911_add_rubric_versions.sql`
4. `20260912_add_final_submission_drafts.sql`
5. `20260912_add_locked_final_submissions.sql`
6. `20260912_add_evaluation_finalizations.sql`
7. `20260912_add_project_results.sql`
8. `20260912_add_interdisciplinary_projects.sql`
9. Later additive scripts for contribution, academic verification, milestone templates and task evidence/comments.

The application does not prove that these scripts have been applied in every environment. Release verification must own that proof.

## 4. P0 — work required to make existing flows demonstrable

### P0.1 Isolated E2E database and seed path

**Owner:** DB + BE + QA
**Why:** the shared live database has no valid Phase 6/7 test path. Do not repair this through direct ad-hoc `UPDATE` statements.

Create a dedicated, disposable E2E database and an idempotent seed procedure that uses supported domain commands whenever possible. It must include:

1. Admin, department staff, student leader/member, eligible lecturer evaluator and current primary supervisor.
2. Both `SINGLE_MAJOR` and `INTERDISCIPLINARY` team/project scope, including participating departments and valid major requirements.
3. A coherent calendar: registration, execution, final submission and evaluation periods. Only the period being tested should be active; dates must be deterministic for test clocks.
4. A project progressed through supported workflow rules to `ACTIVE`, then a valid final-submission-ready project with requirements, approved deliverable versions and a non-empty locked package.
5. Published rubric/version, evaluation period, result policy/result publication fixtures, notifications/audit records, and an archived project for read-only checks.
6. Two permitted writers for every aggregate that supports optimistic concurrency.

**Acceptance:** data survives reload; out-of-scope actor gets `403`; all required role/state transitions are server-validated; no seed contains secrets; both academic modes are tested.

### P0.2 Release migration ledger and capability check

**Owner:** DB release owner + BE
**Required change:** use the database-first release process: ordered, rerunnable SQL scripts; deployment record; backup/rollback/forward-fix plan; scaffold generated persistence models after schema acceptance. Do not use application startup as the migration runner and do not hand-edit generated DB-first models.

Add an admin-protected readiness endpoint or release report that states migration/version capabilities and configuration blockers without leaking schema details to anonymous users.

**Acceptance:** target environments prove exact script version/order, required tables/indexes/FKs, and a protected smoke test. A health endpoint alone is insufficient.

### P0.3 Evaluator candidate discovery and stable errors

**Owner:** BE
**Missing contract:**

`GET /api/v1/projects/{projectId}/eligible-evaluators`

Return only evaluators the current staff/admin can use for that project, e.g.:

```json
{
  "items": [
    {
      "userId": 123,
      "displayName": "...",
      "departmentId": 7,
      "departmentName": "...",
      "isActive": true,
      "isCurrentSupervisor": false,
      "eligible": true,
      "ineligibilityReasons": []
    }
  ]
}
```

The create endpoint must still revalidate every rule transactionally. Add stable, documented `ProblemDetails` codes (do not ask FE to parse English messages): `FINAL_PACKAGE_REQUIRED`, `EVALUATION_WINDOW_CLOSED`, `PUBLISHED_RUBRIC_REQUIRED`, `EVALUATOR_INELIGIBLE`, `SUPERVISOR_REQUIRED`, `STALE_CONCURRENCY_TOKEN`, `FINALIZED_ASSIGNMENT`, and `ARCHIVE_NOT_ALLOWED`.

**Acceptance:** inactive/cross-department lecturers never appear; direct POST still rejects forged IDs; wrong scope is `403`; state/window/rubric/package failure is actionable and stable.

### P0.4 Archive eligibility must be backend-derived

**Owner:** BE
**Gap:** `GET /workflow-context/projects/{id}/actions` currently lists registration/review/supervisor actions but no archive action. FE therefore cannot satisfy the requirement “show archive only when Backend policy allows it” without guessing from `COMPLETED`.

Add an `archive_project` action with `allowed` and reason codes. Its evaluation must match `ArchiveProjectCommand`: authenticated admin or scoped department staff, valid project visibility, and `COMPLETED` status. The POST remains the final authorization and concurrency authority.

**Acceptance:** authorized completed project returns `allowed: true`; all other state/scope combinations return `allowed: false` with a reason; a stale or changed project cannot be archived after action retrieval.

### P0.5 Prove the narrow Phase 6/7 path

Run this protected API journey against the isolated E2E database:

1. Open a final-submission period and transition a valid project through supported commands.
2. Create final requirements/draft and lock a non-empty package.
3. Open an evaluation period in the same semester with a published matching rubric.
4. Scoped staff discovers and assigns an eligible evaluator.
5. Evaluator reads the permitted package, creates/saves/finalizes a draft.
6. Staff configures policy/publishes result, then archives only through backend-approved action/state.
7. Capture negative evidence for no package, closed/no window, absent rubric, inactive/cross-scope evaluator, non-supervisor `SUPERVISOR`, stale token, revoke-after-finalization and unauthorized archive.

## 5. P1 — integrity and review gaps

### P1.1 Optimistic concurrency for execution aggregates

`TaskDto`, `MilestoneDto`, `ProgressReportDto`/detail and `MeetingDto`/detail currently expose no concurrency token; their update command/request shapes likewise lack one. This permits lost updates even where FE refreshes after an unrelated `409`.

**DB change:** add `row_version rowversion NOT NULL` or a suitable immutable concurrency token to `tasks`, `milestones`, `progress_reports` and `meetings`. Include appropriate migrations, indexes where necessary, and rollback/forward plan.

**BE change:** return `concurrencyToken` on detail/read DTOs; require it for task update/status/assignee/dependency/order, milestone update/status/order, report update/submit/review and meeting update/complete/cancel/notes/participant mutations. Compare inside the same transaction before writes. A stale request must return `409` with no audit, notification or partial side effect; success returns a fresh token.

**Acceptance:** two-writer SQL/integration test proves first write wins, second gets `409`, persistence contains only the first value and no duplicate side effects.

### P1.2 Meeting decisions and action items

No `meeting_decisions` or `meeting_action_items` table/contract is present.

**DB model:**

- `meeting_decisions(id, meeting_id, content, decided_by, decided_at, created_at)`;
- `meeting_action_items(id, meeting_id, title, description, assignee_user_id, due_at, status, concurrency_token, created_by, created_at, updated_at)`;
- FK to meeting/users, indexes for meeting, assignee and status, validation for permitted status and valid project participant.

**BE contract:** paged `GET/POST /api/v1/meetings/{meetingId}/decisions`, paged `GET/POST /api/v1/meetings/{meetingId}/action-items`, and token-protected update/status action routes. Backend determines whether organizer, leader or assigned supervisor can write; canceled meetings are read-only.

### P1.3 Historical registration/review and project-major requirements

Existing registration snapshots and department decisions must not be overwritten during resubmission. Add a scoped, paged read contract:

`GET /api/v1/projects/{projectId}/review-snapshots?page=1&pageSize=20`

Each record needs submission number, project period, submitter/time, immutable proposal and academic-scope snapshot, and decisions. Old snapshot decisions or stale snapshot tokens must be `409`; unauthorized scope must be `403`.

Project-level major quotas/responsibilities are not a current persisted aggregate. When approved, add `project_major_requirements(project_id, major_id, min_members, max_members, responsibility, concurrency_token, created_at, updated_at)`, unique `(project_id, major_id)`, feasibility validation and immutable submission snapshot linkage. Then provide token-protected `GET/PUT /projects/{id}/major-requirements`.

### P1.4 Versioned period policy

Current policy fields live directly on `project_periods`; no immutable version/snapshot read model proves which rules governed historical registration, supervisor selection, final submission or publication.

Design an additive versioned policy/window aggregate and an effective-policy read contract, for example:

- `GET /api/v1/project-periods/{id}/effective-policy`
- token/version-protected policy mutation before server-defined locking;
- persisted policy-version reference/snapshot on affected workflow artifacts.

Never mutate historic policy meaning in place. Locked/obsolete policy returns `409`; cross-scope returns `403`.

## 6. P2 — approved future Governance Baseline, not a client-side workaround

The live database has no `evaluation_schemes`, `evaluation_assignment_targets` or `student_results`. Do not fake these in frontend dropdowns, derive individual grades from contribution, or silently map legacy evaluations.

After domain approval, add:

| Capability | Required server model/behavior |
| --- | --- |
| Evaluation scheme | Immutable/versioned scheme with `COMMON`, `MAJOR_SPECIFIC`, `INDIVIDUAL` scope, rubric and semester linkage. |
| Scoped assignment target | Explicit project department/major or active student target, scope-aware uniqueness and replacement policy. |
| Student result | One published result per project/student/scope, linked to active membership, policy/rubric/finalization snapshots; server-calculated outcome/score, publisher and timestamps. |
| Event history | Append-only assignment/eligibility/revoke events with actor, reason, UTC time and lookup indexes. |
| Student-safe read | `GET /api/v1/projects/{projectId}/students/{studentId}/result` only after publication and only to authorized actor. |

Legacy evaluations need an explicit product decision: retain as immutable historical records or run a separately reviewed migration with audit evidence. Never auto-backfill assignment IDs.

## 7. Required engineering and QA handoff

For each delivery, DB and BE must hand over all of the following together:

1. Reviewed, idempotent SQL script and deployment/rollback/forward-fix notes for the isolated E2E DB.
2. DB-first scaffold evidence after applying the schema; no manual edits to generated persistence classes.
3. OpenAPI plus request/response examples, required role/scope/state rules, stable error codes and concurrency behavior.
4. Integration tests against SQL Server (not only repository mocks) for happy path, `400/422`, `401`, `403`, `404`, `409`, persistence reload, and no-side-effect stale failures.
5. Seed command and test accounts supplied through secure secret storage, never in git.
6. Audit/notification behavior only where the business operation requires it; logs must avoid sensitive contents.

## 8. Definition of done and release gate

Do not mark a slice `DONE` until all apply:

- The required migration/version is recorded on the target environment.
- The protected API journey completes on an isolated SQL database using server-derived authorization/state.
- Reload proves persistence; unauthorized and stale callers prove `403`/`409` boundaries.
- FE uses only contracts that exist in OpenAPI; it does not synthesize roles, allowed transitions, evaluation scope, grades or an archive policy.
- A reviewer can replay the seed and integration suite without knowing a production/shared database secret.

Until those conditions are met, use `PARTIAL`, `FE_DONE_BE_PENDING`, or `BLOCKED` as shown in Section 1 — never “complete” based only on UI, health or table presence.
