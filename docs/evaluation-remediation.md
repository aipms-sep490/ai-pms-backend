# Evaluator discovery and archive contract

## Eligible evaluators

`GET /api/v1/projects/{projectId}/eligible-evaluators?periodId=123&page=1&pageSize=20`

Only active persisted Admin or Department Staff in project scope may call this route. Department Staff must also manage the department of the period's rubric. The project must be in FINAL_SUBMISSION with a locked package; the selected evaluation period must be the unique open evaluation window for its semester and must reference a published, valid rubric in that project's academic scope.

Returns the normal `PagedResult` envelope with `items`, `page`, `pageSize`, `totalCount`. Items contain `userId`, `displayName`, `departmentId`, `departmentName`, and `evaluationTypes`. The list contains active lecturers in the rubric department; SUPERVISOR is offered only for the current primary supervisor. Types with an existing active assignment are excluded. An evaluator with no assignable type is omitted. Ordering is `fullName, id`; bounds are page 1..1,000,000 and pageSize 1..100. No email, grades or off-scope identities are returned.

This is advisory discovery. Existing assignment POST revalidates account, department, supervision, period and rubric in a serializable transaction. A changed candidate is refused. Assignment, audit and inbox notification commit together. Repeated POST returns 409 without creating another notification; replayed notification events are deduplicated on the source assignment row. EVALUATOR_ASSIGNED is in-app only and creates no SMTP queue row.

## ProblemDetails codes

The optional `code` extension is additive; existing HTTP mappings and detail text remain compatible. Clients should use status and code, with a generic fallback for older/uncoded errors. OpenAPI describes `code` and `traceId`.

| Code | HTTP | Trigger |
| --- | --- | --- |
| FINAL_PACKAGE_REQUIRED | 409 | Evaluation requires FINAL_SUBMISSION and usable locked evidence |
| EVALUATION_WINDOW_CLOSED | 409 | Missing, closed, ambiguous or wrong-semester evaluation window |
| PUBLISHED_RUBRIC_REQUIRED | 409 | Missing or incompatible rubric/publication state |
| EVALUATOR_INELIGIBLE | 409 | Assignment target is not an active lecturer in managed scope |
| SUPERVISOR_REQUIRED | 409 | SUPERVISOR target is not the current primary supervisor |
| STALE_CONCURRENCY_TOKEN | 409 | Explicit project/evaluation/execution token mismatch |
| FINALIZED_ASSIGNMENT | 409 | Scoring/finalization/revocation attempts to change protected evaluation |
| ARCHIVE_NOT_ALLOWED | 409 | Project is not COMPLETED |

Access denial continues to return 401/403/404 as appropriate before exposing scoped workflow blockers. Validation remains 400. There is no translation of arbitrary English error messages into codes.

## Archive

`GET /api/v1/projects/{id}/actions` includes `archive_project`. It uses the same scope/state rules as POST archive: active persisted role intersected with token role, Admin or staff in an active project department, and COMPLETED status. Visible users without archive authority receive `allowed:false`; outside-scope callers receive 403. POST rechecks the rules and current project token inside the transaction. Audit failure rolls back project status and history.

## Acceptance evidence

SQL tests use unique disposable databases, never the shared AI_PMS catalog. `AcceptanceJourneyTests` uploads actual bytes into an isolated fake storage adapter, creates and locks final submission through HTTP, publishes a rubric, advances the clock to a distinct evaluation phase, discovers/assigns an evaluator, configures result policy BEFORE finalization, scores/finalizes, publishes the project result, archives and reloads through a fresh host/SQL context. It verifies unpublished-result privacy, member/outside-scope denial, locked bytes, historical result persistence and archive audit.

The journey starts at a controlled ACTIVE fixture; it does not claim registration/eligibility acceptance from issue #74. Phase configuration is a controlled SQL fixture, while all project lifecycle transitions from ACTIVE onward use production APIs. No migration or production database mutation is required by this change. The backend remains compatible with existing frontend requests.

Validation on 2026-09-28: warnings-as-errors build (0 warnings/errors), 802 unit tests, 851 integration tests on SQL LocalDB, including OpenAPI route/schema checks. Full regression includes existing dashboard/export, notification, file, AI, team, supervisor and execution suites; it does not replace a frontend acceptance session or real-provider smoke test.
