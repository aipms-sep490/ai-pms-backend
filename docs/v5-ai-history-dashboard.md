# v5 phase 12: scoped AI history and actor dashboard

Status: implemented, final validation/merge pending. BE-15 and BE-N10 are covered
by HTTP/SQL tests and the [A-F acceptance matrix](v5-acceptance-matrix.md).
Do not infer P0_P1_INTEGRATED_READY from backend endpoints. No frontend changes.

## AI contract

Existing project ask/report-summary routes remain. Responses add `runId` and
`generationStatus` (COMPLETED, FALLBACK, INSUFFICIENT_EVIDENCE). New routes:

- GET `/api/v1/projects/{projectId}/ai/runs?page=1&pageSize=20`
- GET `/api/v1/projects/{projectId}/ai/runs/{id}`

History is the caller's own runs only, paged 1..100, newest ID first. DTO includes
kind, status, modelVersion, configVersion, UTC started/completed times, safe error,
evidence references and typed answer/report summary. Never expose raw result JSON,
query/prompt, credentials or provider exceptions. Failed/interrupted runs have no
result; an uncompleted RUNNING row represents an interrupted attempt, not success.

The current provider remains the existing **local deterministic Grounded adapter**,
identified as GROUNDED_LOCAL_V1. This does not claim an external LLM deployment.
Provider metadata describes the actual adapter/version/config, not a fabricated
model name. Any future provider must supply its own metadata and pass scope tests.

Source authorization occurs before retrieval/provider invocation, on completion,
and when history is read. Active project members and active Primary get existing
whole-project read scope; frozen lead staff gets whole-project scope. Mentor and
participating staff get only tasks classified in their assigned/frozen majors.
Unclassified reports/milestones and project totals are not passed to their provider.
Meetings require explicit participant membership, even in whole-project AI context.
Admin/evaluator role alone does not grant project AI access. Legacy project members
retain their existing read-only project context; missing provenance cannot grant a
new major/department scope or academic mutation.

Input is bounded in SQL: 30 tasks (blocked first), 10 milestones, 5 reports and 5
invited meetings; each text field max 350 characters. The source list stores every
input reference, including evidence not cited in the final answer. Query strings
are not persisted. Histories bind account/project/membership/assignment/scope;
changed assignment or leave/rejoin cannot reopen old wider histories. If a source
is reclassified/removed or meeting invitation revoked, history returns no answer
or evidence with AI_HISTORY_SCOPE_CHANGED. Reclassification while generation runs
rejects the response. History visibility is batched per page (no per-run query).

Provider failure/timeout/malformed response uses the deterministic fallback;
insufficient project/report evidence skips generation. Safe statuses are recorded
with source references, model/config metadata, result and audit. AI never mutates
scores, assignments, lifecycle or academic results. History completion/audit share
a transaction. Unexpected persistence errors fail this optional AI request, without
preventing independent academic workflows. No error text containing provider
credentials is logged or returned.

401 missing login; 403 inactive/unauthorized/revoked scope; 404 missing/foreign
report or another actor's run; 400 invalid query/page; 429 existing AI rate limit.
Revoked scope during generation returns 403 and records a safe failed attempt.

## Dashboard contract

GET `/api/v1/dashboards/me?semesterId=` returns authoritative counts:
projectCount, openTasks, overdueTasks, pendingEvaluations, unreadNotifications,
optional activeAccounts, scope and complete. No client page summation is needed.

Discovery is bounded to 1000 matching projects; larger sets return 400 and require
a semester filter. Frozen snapshots are batch-read, parsed and checked for active
departments/majors. Scope predicates precede aggregate queries. Student/Primary/
lead can count their project tasks; Mentor/participating staff only classified
major tasks. Evaluators receive their own pending scoped assignment count, no
project task totals. Admin-only receives platform active-account/unread facts,
with no academic totals. There is no synthetic deadline or inferred score.

The older supervisor/department cards contain whole-project analysis and therefore
now restrict to current Primary / lead department respectively for frozen projects.
Mentors/participating departments use dashboards/me, calendar and scoped task board.
Legacy draft department previews retain their existing current-configuration scope.
Existing administrative portfolio/export APIs retain their explicitly authorized
platform inventory contract; the new actor dashboard grants no extra access.

## Database, tests and remaining acceptance

Additive/rerunnable migration `20261012_add_ai_runs.sql`: one table, four checks,
three FKs, indexed actor/project history. Bootstrap/manifest/model/readiness update
together. No historical AI result is fabricated or backfilled. Shared rollout only
after isolated replay/rerun, green CI and merge.

AiHistoryTests uses HTTP plus isolated SQL to verify persisted history/audit,
cross-actor/Admin denial, mentor input capture, reclassification redaction,
revocation during provider call, provider fallback, insufficient evidence and
server dashboard totals. Capture/failure adapters are explicit test doubles;
separate SQL tests use the real local Grounded adapter. Legacy repository-stub
endpoint tests remain labelled as contract tests, not lifecycle acceptance.

New API journeys now cover IT, Marketing demo (official publication refused), and
IT/Marketing/Design across three departments from qualification through final
package/scoring. Synthetic approved regression policy permits A/C to exercise
publication/archive; it is not real academic approval. Golden API scoring asserts
8.10/7.90/7.40 and 7.67 from seven finalized evaluations. The matrix identifies
seeded locked inputs separately from real upload paths. Redacted evidence includes
correlation IDs and asserted SQL/audit readback. See the [Tin handoff](v5-final-fe-handoff.md).

AI tests additionally cover report IDOR, inactive scope and audit rollback. A failed
audit leaves the committed reservation RUNNING without result/evidence; it does not
pretend success. This is intentionally different from a safely recorded FAILED run.
Final CI/merge/shared schema verification remain required before backend readiness.

Local validation 2026-10-12: Release warnings-as-errors, 1,055 unit and 1,249
integration tests passed, zero skips. Isolated bootstrap/reconnect/rerun and
ownership/checksum guards passed. OpenAPI regenerated from an offline API host.
