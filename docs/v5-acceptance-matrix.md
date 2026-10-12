# v5 backend acceptance A-F

Status: local Release/1,055 unit/1,249 integration tests passed with zero skips;
CI/merge/shared rollout pending, 2026-10-12. This matrix records executed
backend HTTP/SQL paths and limits. Shared database checks, mocks, migration parity
and browser integration are different evidence categories.

## Current scenario evidence

Generated redacted exchanges: `docs/validation/v5-acceptance-2026-10-12/`.
Each file records synthetic/isolated fixture labels, method/path/status, selected
response fields, correlation ID and asserted database/audit readback. Passwords,
JWTs, concurrency/confirmation tokens, file bytes, free text and prompts are not
recorded. The trace is allowlisted, not a raw replayable request dump.

| Scenario | Actual API path exercised | Readback and limits |
|---|---|---|
| A IT | Qualification reject/resubmit/verify; team invite/accept; register/review/approve; Primary accept/replace; real file upload/final lock; scheme/assignment/save/finalize; student/project publish; archive; CSV/XLSX/PDF export | Persisted lifecycle history and archive audit. Academic policy input is explicitly synthetic regression approval, not approval for a real institution. `A-it-single-major.json` |
| B Marketing demo | Same qualification/registration/supervision/final-upload/scoring path with Marketing scope, followed by demo preview and refused publication | Score 8, no ProjectResult, project remains FINAL_SUBMISSION, finalization audit. Demo cannot complete through official publication. `B-marketing-demo.json` |
| C IT + Marketing + Design | Three departments/majors/students created as inputs, API team registration creates frozen snapshot and three department decisions; supervision, upload/lock, COMMON scoring, all three student results, publication/archive/export | Three frozen departments, lifecycle history, archive audit. This journey uses COMMON-only synthetic approved scheme; E separately exercises all common/major/individual components. `C-three-department.json` |
| D security | Mentor major-only AI input, no whole-project dashboard card, foreign-actor history 404, reclassified source redacted, ended assignment 403 | Real SQL assignments/classification changes, explicit capture-provider test double. `D-ai-scope-security.json`. Broader authorization/concurrency coverage listed below |
| E golden scoring | API create/publish demo scheme; seven assigned evaluations, real draft/save/finalize; all student and project previews; official publication blocked | 8.10/7.90/7.40 and 7.67, seven finalizations/audits, no student results. Three-major locked-package inputs are synthetic and do not replace C's registration/upload evidence. `E-demo-golden.json` |
| F AI fallback | Provider failure -> persisted FALLBACK; remove evidence -> INSUFFICIENT_EVIDENCE without provider call; history reads | Two runs/completion audits, safe error/status, project unchanged. Failure provider is an explicit test double; real local Grounded provider is exercised separately by AiHistoryTests. `F-ai-fallback.json` |

## Additional required feature/security coverage

Run the whole suite; these are independent isolated fixtures, not one production
journey. Exact file/version, concurrency and audit assertions run against SQL.

| Area | Tests / evidence |
|---|---|
| Frozen scope, lead vs participant, Primary vs Mentor, Admin denial, D17 | V5AcademicCapabilityTests; supervisor/evaluator concurrency tests; InterdisciplinaryWorkflowTests |
| Exact locked major/student file, revoked assignment, replacement after lock | LockedEvidenceSecurityTests; final-submission upload acceptance; EvaluationAssignmentEvidenceEndpointTests (separate legacy fixtures identified in code) |
| COLD actual upload -> lock -> COMMITTEE draft/finalize/progress | ColdAcceptanceJourneyTests, ColdSubmissionEndpointTests, ColdEvaluationEndpointTests |
| Weekly rubric/version/Primary replacement/no official result mutation | WeeklyAssessmentEndpointTests |
| Append-only evidence review, audit rollback, scoped committee and overlapping schedule | AcademicTrackingEndpointTests |
| Calendar bounds/ties/cursor/scope, RSVP/recurrence/reminders/notes lock | CalendarV5EndpointTests and meeting collaboration suite |
| Sprint baseline/estimate/status events, unfinished carryover, concurrency and scope | SprintEndpointTests |
| Attachments/recall/membership, reactions/mute, retry/audit rollback, real SignalR | ChatCollaborationTests and SignalR integration suite |
| AI scope changes during generation, own history, report IDOR, inactive department, audit rollback | AiHistoryTests; production retriever/store with SQL, local adapter or explicitly named injected failure/capture adapter |
| SQL fresh/upgrade/rerun and constraint/index/FK parity | SchemaParityTests; test-e2e-bootstrap.ps1; test-schema-readiness.ps1 |

## Reproduce without touching AI_PMS

Set `AIPMS_TEST_SQL_CONNECTION` locally from secrets. Fixture code uses it only to
create owned random databases, and refuses shared-name destructive operations.
Set `AIPMS_ACCEPTANCE_OUTPUT` to a local output directory to capture the selected
scenario traces. Then run Release warnings-as-errors and the full `dotnet test`
suite. Do not put the secret in a committed script or test artifact.

Tests use a fixed academic clock, synthetic accounts/policies, test JWTs and
in-memory file storage for lifecycle journeys. They execute the API/application/
database services and actual authorization/transactions. They do not verify real
Google mailboxes, external AI, SMTP, LiveKit Cloud, deployed storage or browser UI.
Previously recorded provider staging tests remain separate from this v5 run.

## Completion gates

Backend readiness requires final Release/unit/integration/SQL success, reviewed
OpenAPI, green PR CI/merge and verified additive shared migration. The tracker
must list the corresponding SHA/PR and rollout report before changing its status.
Academic decisions D4/D5/D6/D13-D16 remain independent; synthetic/demo scoring
does not approve them. Integrated readiness additionally requires Tin's browser
acceptance and exact FE/BE deployment SHAs. Do not label it complete from this file.
