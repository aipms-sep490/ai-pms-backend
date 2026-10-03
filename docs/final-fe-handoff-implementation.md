# Final FE-BE Sync Handoff

Backend implementation status for FE frozen branch `chore/final-fe-be-sync-v3` at `474e742e`.

## Delivered contracts

| Block | Contract | Status | Notes |
|---|---|---|---|
| BE-AW-001 | `GET /api/v1/projects/{id}/execution-actions`, task and milestone variants | DELIVERED | Uses persisted project, leader, supervisor, assignment and mentor predicates. |
| BE-AW-002 | Existing evidence ledger routes | DELIVERED | Source ownership, project state, assignment and major checks are enforced server-side. |
| BE-AW-003 | Mentor resource scope | DELIVERED | Discipline mentor scope is assignment/major based; no global mentor role is accepted. |
| BE-AW-005 | `GET /api/v1/projects/{id}/governance` | DELIVERED | Returns departments, assignments, final package state, readiness, blockers and actor actions. |
| BE-AW-006 | `GET /api/v1/evaluation-assignments/{id}` | DELIVERED | Evaluator or scoped staff/admin only. |
| BE-AW-007 | `GET /api/v1/evaluation-assignments/{id}/evidence` | DELIVERED | Read-only final package projection; unknown legacy scope is read-only. |
| BE-AW-009 | `PATCH /api/v1/users/{id}/academic-profile` | DELIVERED | Department/major validation, persisted scope and optimistic concurrency token. |
| BE-AW-010 | Role metadata and assignment guard | DELIVERED | Only ADMIN, DEPARTMENT_STAFF, LECTURER and STUDENT are global roles. |
| BE-AW-011 | `GET /api/v1/calendar` | DELIVERED | Bounded actor-scoped date range with stable opaque cursor. |

## Migration

`db/changes/20261003_add_user_row_version.sql` adds an additive SQL Server `rowversion` to `users`. It is listed in `db/e2e/migrations.json` and appended to `db/schema.sql`. Run the migration against an isolated database first, rerun it to verify idempotency, then apply it to `AI_PMS` after review/merge.

## Calendar semantics

Task uses `dueAt`; milestone uses `dueDate`; meeting uses `startAt`; deliverable uses its persisted `dueAt`; final submission uses its authoritative deadline. Admin receives platform-only calendar facts and does not receive all academic project events by default.

## Final state

The backend is `READY_FOR_FINAL_SYNC` after CI, security/integration tests and the migration verification pass. Frontend files are intentionally unchanged.
