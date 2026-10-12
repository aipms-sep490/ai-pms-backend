# v5 P0/P1 closure: analysis scope and full database audit

Date: 2026-10-12. Baseline BE d344bc3 (PR #122), FE origin/develop 49bae0a (PR #57).
Backend-only change; Tin owns frontend integration. Local implementation, not deployed.

## Corrected analysis contract

The old progress-analysis handler checked general project access, permitting
platform Admin and partial academic assignments to load whole-project facts.
It now uses scoped AI authority. Other general project readers are unchanged.

- GET `/api/v1/projects/{projectId}/progress-analysis`: transient read, no AI run.
- POST `/api/v1/projects/{projectId}/ai/progress-analysis`: no body; explicitly
  records a run and returns ProjectProgressAnalysisDto with runId.
- Existing project-scoped `/ai/runs` paged list and `/ai/runs/{id}` detail add
  kind PROGRESS_ANALYSIS and nullable typed progressAnalysis. Other kinds retain
  answer/reportSummary. No raw stored JSON is exposed.

Active members, current Primary and frozen lead staff can analyze project facts.
Mentor/participating staff see only tasks in their authorized frozen majors.
Admin/evaluator role alone grants no AI access. Legacy member access follows
existing AI rules, without granting new major/department authority.

Partial scope returns contextScope MAJOR, majorIds and visible task counts. Risk,
confidence, project feature metrics and milestone totals are null; dataStatus is
INSUFFICIENT_DATA. unavailableMetrics lists missing categories. ProgressSummaryDto
TotalMilestones/CompletedMilestones are now nullable. Hidden data is never zero.
Whole scope returns PROJECT_WITH_INVITED_MEETINGS; meetings require participation.

Each source query takes at most 2001 rows. More than 2000 visible records per
source type returns 400 AI_ANALYSIS_SOURCE_LIMIT, never a silently truncated total.
Reads are serializable. Completion rechecks scope/evidence; reclassification or
revoked invitation redacts history with AI_HISTORY_SCOPE_CHANGED. Reads are caller
scoped and never create additional runs.

This uses the existing deterministic RULE_BASED engine; actual rule version is
stored as configVersion. It is not an external LLM. Completion/result/evidence/
audit are atomic. Audit failure leaves a RUNNING reservation with no result.
Analysis does not change academic state. 401 unauthenticated; 403 invalid/revoked
scope; 404 missing project/another actor's history; 400 invalid input/source bound;
POST uses existing AI 429 rate limiting. No general-access fallback on denial.

## Shared audit observations

Read-only comparison: 2178 expected signatures versus 2185 shared signatures;
22 missing occurrences and 29 extras, not 51 independent bugs. All 524 existing
FK/check scans found zero violations. Inventory includes column types/nullability,
defaults, computed/identity properties, indexes, constraints/trust and SQL modules.

| Difference | Decision |
| --- | --- |
| Two absent meeting video defaults | Add defaults, preserve existing rows |
| Two absent assignment-type checks | Add WITH CHECK; existing slot checks already restrict these values |
| ai_runs kind differs | Expected unapplied new PROGRESS_ANALYSIS migration |
| Eight expected FK occurrences absent | Bootstrap/replay duplicates eligibility FKs; shared DB has one of each, no missing referential protection; do not add duplicates |
| Nine module definitions differ | Bodies equal after removing only leading whitespace/comments |
| sysdiagrams and helper objects | SQL Server design tooling; preserve outside business schema |
| Extra task_comments index | Additional nonunique access index; preserve |

Business probes: zero duplicate active Primary/Mentor and zero active Primary/
evaluator conflicts. Three non-draft projects lack registration snapshots; one
latest snapshot lacks lead/major mapping. Preserve as legacy provenance exceptions;
do not reconstruct immutable history from current profiles. Exact-file-reference
and unknown-target probes returned zero, which does not verify physical file bytes.

Audit uses read-committed observations; concurrent app activity remains possible.
No shared data was changed. Sanitized evidence with module hashes and aggregate
counts: `validation/v5-full-schema-audit-2026-10-12.json`. Raw schemaMatch stays
false; documented exceptions are not suppressed to manufacture a pass.

## Migrations and reproduction

- `20261012_extend_ai_run_progress_analysis.sql` expands the trusted kind check;
  no new table, no history backfill.
- `20261012_reconcile_v5_schema_defaults.sql` adds two missing defaults and two
  missing type checks. Both scripts are rerunnable with QUOTED_IDENTIFIER ON.
- Bootstrap/manifest match; no model change needed for existing Kind/JSON fields.

SchemaParityTests uses `db/e2e/schema-inventory.sql`: fresh bootstrap, baseline
upgrade, replay/rerun, snapshot preservation, four simulated shared gaps repaired
in an owned test DB and read-only business probes. To export expected metadata,
set AIPMS_SCHEMA_INVENTORY_OUTPUT to a local ignored JSON path.

Run `scripts/audit-database-readonly.ps1 -ExpectedInventory <expected.json>
-OutputPath <report.json>` with AIPMS_AUDIT_SQL_CONNECTION supplied privately.
It requires VIEW DEFINITION, compares multisets and scans FK/check predicates.
It never applies DDL or repairs rows. Keep raw inventories local because module
definitions may contain environment details. Integration tests receive
AIPMS_TEST_SQL_CONNECTION and create/drop only their own AI_PMS_TEST databases.

## Verification and outstanding gates

ScopedProgressAnalysisTests covers major scope, unknown versus zero, Admin/foreign/
anonymous denial, typed history/audit, invited meetings, revoked assignment,
reclassification, source limits and audit rollback. Existing AI history and SQL
progress tests remain. Stubbed endpoint tests are explicitly contract-only.

Full local integration run: 1259 passed, zero skipped. Unit run: 1050 passed,
zero skipped. The old handler's seven tests became two delegation tests; actual
authorization/calculation moved to SQL-tested infrastructure. Final Release build
passed with zero warnings/errors. The final focused run passed 25 tests including
migration rerun/gap repair. OpenAPI route and nullability checks passed. Evidence:
`validation/v5-analysis-closure-tests-2026-10-12.json`.

Interactive foundation also passed: isolated fixture create/reconnect/rerun,
stable credentials/aliases and unsafe-name/ownership/checksum refusal. The new
test-v5-api-login.ps1 starts the real API and verifies password/JWT login for four
synthetic actors, dashboard authorization and cleanup. CI now runs this smoke.
Evidence: `validation/v5-isolated-login-2026-10-12.json`. External delivery is off;
no Google mailbox, SMTP or LiveKit acceptance is claimed.
The login host uses an empty content root and explicit Staging configuration,
including CORS, so neither local appsettings nor User Secrets can mask missing
CI settings. The original CI smoke exposed this omission; it was corrected and
rerun locally with the independent configuration.

Tin's concrete adapter checklist is in
[v5-fe-contract-delta-2026-10-12.md](v5-fe-contract-delta-2026-10-12.md).

This does not declare P0_P1_INTEGRATED_READY. Pending: Tin's AI/sprint/chat and
academic adapters, unified three-department full-component/COLD browser journey,
real-provider acceptance, reviewed deployment/shared migration rollout. Technical
tests do not approve demo academic policy. No container/feature flag was changed.
