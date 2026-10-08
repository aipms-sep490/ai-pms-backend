# DEPART backend and database handoff

Baseline: `develop` at `94a6fcb0dac789d1fa7bcea7389144d803b508ae`.
Branch: `feature/depart-be-completion`. Backend/database/docs only; no frontend changes.
Implementation status: **DEPART_DONE for backend code and isolated local acceptance**. PR review/CI, merge and deployment are separate gates; this branch has not been deployed. FE/browser acceptance is not claimed.

## Review stack

| Order | PR | Branch / implementation commit |
| --- | --- | --- |
| 1 | [#102](https://github.com/aipms-sep490/ai-pms-backend/pull/102) | `feature/depart-governance-certificate` / `9de7deb` |
| 2 | [#103](https://github.com/aipms-sep490/ai-pms-backend/pull/103) | `feature/depart-assignment-capabilities` / `36a2cd1` |
| 3 | [#104](https://github.com/aipms-sep490/ai-pms-backend/pull/104) | `feature/depart-authorized-export` / `dd1fcfe` |
| 4 | [#105](https://github.com/aipms-sep490/ai-pms-backend/pull/105) | `feature/depart-be-completion` / tested package `085138b` |

The PRs are stacked so each diff shows only its own changes. After the preceding PR merges, retarget the next PR to `develop` and require its CI/review again. Local acceptance does not constitute merge or deployment approval.

## Implementation and acceptance map

| Ticket | Contract / implementation | Test evidence | State |
| --- | --- | --- | --- |
| D01 | Governance, project detail and review actions use validated frozen lead/major scope. Result readiness calls the result preview service. | `Depart_D01_frozen_lead_is_not_lowest_id_and_live_team_changes_do_not_rewrite_scope`; `Depart_D01_result_readiness_agrees_with_scoring_before_and_after_publication`; legacy review denial | DELIVERED |
| D02 | Qualification certificate metadata, authenticated streaming and upload without a project; owner/uploader checks, dedicated file types, no public storage URL | `Depart_D02_certificate_is_owner_scoped_streamed_and_resolves_current_file_only`; `Depart_D02_upload_without_project_is_atomic_and_replacement_invalidates_review` | DELIVERED |
| D03 | Assignment capabilities and replacement candidates; frozen lead/major authority; transaction rechecks | `Depart_D03_replacement_preview_excludes_current_supervisor_and_mutation_rechecks_availability`; `Depart_D03_closed_projects_release_capacity_without_allowing_assignment_mutation`; supervisor governance tests | DELIVERED |
| D04 | Qualification token in DTO; verify/reject compare reviewed token and audit both versions inside transaction | `Depart_D04_resubmitted_evidence_rejects_old_token_and_audits_reviewed_version`; concurrent verify/reject and audit rollback | DELIVERED |
| D05 | Semester/period mutations and qualification policy remain AdminOnly | `Depart_D05_staff_cannot_mutate_structure`; lifecycle policy denial; OpenAPI | DELIVERED: decision preserved |
| D06 | Admin required for cross-department publication even when all project-weight rubrics are in the lead department | Single/interdisciplinary lifecycle; policy evaluation and result tests | DELIVERED: authority hardened |
| D07 | CSV/XLSX/paginated Unicode PDF; server scope, bounded query, audit and correlation header | `Depart_D07_exports_use_server_scope_valid_formats_and_audit_every_download`; `DepartExportTests`; dashboard tests | DELIVERED |
| D08 | Per-run SQL databases, two full API lifecycles, migration rerun and ownership protection | `Depart_D08_qualification_to_archive_uses_real_api_transitions(false/true)`; `scripts/test-depart.ps1` | DELIVERED |

DELIVERED describes code and local backend acceptance, not deployment or FE acceptance. Review order: governance/certificate -> assignment -> export -> acceptance/handoff.

## Validation evidence (2026-10-08)

- Release solution build with warnings as errors: 0 warnings, 0 errors; PR1 and PR2 also built independently from their split commits.
- All 994 unit tests passed.
- 539 distinct integration tests passed: 478 in the sequential regression, 29 candidate tests, and 32 additional eligibility tests. Twenty final contract checks and the certificate regression were rerun and overlap these counts.
- Both single-major and interdisciplinary API lifecycle cases passed, including real mutations, audit/history and publication authority.
- Isolated bootstrap/migration rerun passed, including seed stability after reconnect, protected-name/ownership refusal and checksum validation.
- Shared AI_PMS schema: 45/45 read-only checks; no DDL/data mutation required or performed.

Machine-readable sanitized evidence: [validation/depart-2026-10-08.json](validation/depart-2026-10-08.json). GitHub CI runs the full solution test suite separately on each PR; use the current PR checks for that result, not the local focused-suite count. CI exposed missing frozen mappings in older test fixtures and a PR-splitting dependency; both were corrected without relaxing production authorization.

## Governance and publication

`GET /api/v1/projects/{projectId}/governance`, project detail and `/actions` use frozen registration evidence for submitted projects. `academicScopeProvenance` is `FROZEN_REGISTRATION_SNAPSHOT`, `CURRENT_CONFIGURATION` (draft/revision), or `UNKNOWN`. Missing/malformed/incomplete scope disables academic decisions. Never choose the lowest department ID, use today's team scope to overwrite execution history, or backfill historic scope from current data.

`canPublishResult` delegates to the same locked package, published scheme, required evaluations, scores and policy used by publication. Staff can preview only inside existing rubric/student read scope. A permitted preview may contain `ADMIN_REQUIRED_FOR_CROSS_DEPARTMENT_PUBLICATION`. Publishing rechecks authority and confirmation token. Student results remain restricted to the frozen student's department; cross-department project publication requires Admin.

Final submission writes `ACTIVE -> FINAL_SUBMISSION` history transactionally. Publication writes `FINAL_SUBMISSION -> COMPLETED` history and `completedAt`; audit failure rolls back both. Archive remains a separate authorized transition.

## Certificate and qualification API

| Method / route | Response |
| --- | --- |
| `GET /api/v1/student-qualifications/{qualificationId}/certificate` | Safe qualification/file ID, filename, MIME, size and available SHA-256 |
| `GET /api/v1/student-qualifications/{qualificationId}/certificate/download` | Authenticated stream; no-store, nosniff, safe attachment filename |
| `POST /api/v1/student-qualifications/me/certificate` | Multipart upload plus evidence submission; returns `StudentQualificationDto` with file ID, new token and PENDING_VERIFICATION |
| `POST /api/v1/student-qualifications/me/evidence` | Existing JSON contract retained for certificate number/owned file reference |
| `POST /api/v1/student-qualifications/{qualificationId}/verify` | `{ "expectedConcurrencyToken": "<reviewed token>" }` |
| `POST /api/v1/student-qualifications/{qualificationId}/reject` | `{ "reason": "...", "expectedConcurrencyToken": "<reviewed token>" }` |

Upload form: `file`, `qualificationType` (default CAPSTONE_READINESS), `trainingStatus` (default TRAINING_COMPLETED), optional `certificateNumber`, `issuedAt`, `expiresAt`. No team/project is required. The upload itself submits/resubmits evidence; no second JSON submission is needed. Existing JSON callers are unaffected.

Only PDF/PNG/JPEG, with matching extension/MIME/signature and the existing 20 MiB file limit. Invalid content/name/size returns 400, unsupported certificate MIME 422; the request-body limit can return 413. File metadata, qualification and audit commit together. Failed pre-commit work deletes the new storage object. Ambiguous commit/failed physical cleanup logs the opaque object key for reconciliation: check database references before deleting it. Replaced files remain private and cannot be downloaded by old file ID through project routes; the qualification route always resolves the current certificate.

Reader authority: owner student, active staff in the qualification department, platform Admin. Foreign/inactive staff receive 403; missing certificate/content receives 404. No storage path or permanent public URL is returned. Certificate files are rejected by project-file endpoints.

Compatibility: missing `expectedConcurrencyToken` is temporarily accepted and compares the current database version inside the transaction. It cannot detect changes since the browser displayed the evidence. New FE callers must send the displayed token. On 409, reload and require a new human decision; never silently replay verification. Reject requires a reason. No automatic mandatory-token switch is introduced in this release.

## Assignment API and authority

Unknown frozen academic scope is read-only for owner lecturers and Admin as well as staff. Legacy history remains readable with `academicScopeProvenance=UNKNOWN`; invalid evidence can be null. The original snapshot and its department decisions are retained, and are never replaced by current team data.

Assignment detail/list/end/replacement DTOs include `allowedActions` (`REPLACE`, `END`, each with `allowed`) and `reasons`. Capabilities are advisory; mutations recheck persisted state/authority in a transaction.

Replacement candidates expose `expertiseMatch=MATCHED` for eligible discipline mentors and `NOT_REQUIRED` for primary supervisors under the current policy. This field does not invent an expertise requirement for PRIMARY assignments.

`GET /api/v1/supervisor-assignments/{assignmentId}/replacement-candidates?page=1&pageSize=20&search=...` returns paged candidate identity, expertise, capacity counts/limits, assignment type, major, responsible department, eligibility/reasons and total count. Page size is 1-100. The outgoing supervisor and ineligible candidates are excluded. POST replacement payload is unchanged.

| Operation | Authority / state |
| --- | --- |
| Replace PRIMARY | Frozen lead department staff; unended assignment; ACTIVE project |
| Replace DISCIPLINE_MENTOR | Frozen major department staff; matching major/expertise; ACTIVE project |
| End | Existing owner lecturer or platform Admin authority; staff must match the assignment's responsible department; ACTIVE project only |
| COMPLETED/ARCHIVED | Assignment actions read-only; these projects no longer consume selection/replacement capacity; history remains intact |

Admin has no automatic academic replacement authority. Project read access does not grant replacement rights. Replacement locks outgoing/incoming profiles in stable order, rechecks capacity, preserves the replacement chain and audits atomically.

## Export

`GET /api/v1/dashboards/portfolio/export?format=csv|xlsx|pdf` retains semester/major/status/search filters; CSV remains default. Only Admin can additionally filter department. Staff scope is resolved on the server. Exactly 10,000 projects are permitted; larger filtered portfolios return 422. No export job/table is added.

XLSX uses text cells to prevent formula injection. PDF embeds licensed DejaVu Sans and paginates every row, including Vietnamese text. Server facts determine totals. Filename/content type match the format. `X-Correlation-Id` matches the successful `DASHBOARD_EXPORTED` audit containing actor role, effective department, filters, format, row count and result.

## Database decision and rollout

**NO_DB_CHANGE**. Reuse snapshot/decision tables, qualification concurrency/certificate FK, private files, supervisor assignment constraints, scoped evaluation/result tables. No EF migration or speculative historical backfill. No generated model change is needed without DDL.

Read-only inspection of shared `AI_PMS` on 2026-10-08 passed all 45 checks in `scripts/test-schema-readiness.ps1`. This proves schema capabilities, not deployment. E2E verification now checks the current scoped/legacy evaluation indexes instead of the retired index.

Before staging deployment, run isolated acceptance, review OpenAPI and rerun read-only target readiness. Deploy the reviewed backend commit. No SQL mutation is required on AI_PMS for this change. Do not run destructive lifecycle tests on that database.

## Acceptance runbook

Provide `AIPMS_TEST_SQL_CONNECTION` through the process environment from User Secrets/CI secrets. Never commit credentials. Fixtures create/drop their own `AI_PMS_TEST_<guid>` database, not the supplied catalog. Bootstrap rerun uses an owned `AI_PMS_E2E_<guid>` database.

```powershell
dotnet build AIPMS.sln -c Release -p:TreatWarningsAsErrors=true
dotnet test tests/AIPMS.UnitTests/AIPMS.UnitTests.csproj -c Release --no-build
./scripts/test-depart.ps1 -ResultsDirectory "$env:TEMP/aipms-depart-acceptance"
```

The runner executes SQL suites sequentially and saves logs/TRX, migration-rerun output and `report.json`. A green report requires nonempty successful suites, rerun success, and both full D08 cases present and passed. Missing lifecycle cases never count as acceptance.

Fixture setup seeds actors, academic structure, periods, supervisor profiles and templates. HTTP then performs qualification submit/reject/resubmit/verify, team/invitation, proposal submission/review/department decisions, primary acceptance/replacement, mentor acceptance, deliverable upload, final package lock, rubric/scheme publication, evaluator assignment/scoring/finalization, student/project results, completion, archive and all export formats.

Assertions cover 401/403/404/409/422, history/audit, wrong mentor department, duplicate submission, cross-department publication and archived writes. Companion suites cover concurrent decisions/replacement, audit rollback, stale tokens, storage failure, certificate/export IDOR. Tests use real SQL/HTTP with authenticated test actors and fake external storage/notifications; they do not establish browser or external-provider acceptance.

Performance: candidates count/filter/page in SQL; list capabilities share one project scope read; portfolio facts are batched for the bounded project set, not fetched once per project. Result readiness reuses the existing scorer and frozen inputs.

FE adapter/browser acceptance is a separate handoff step. Before merge/deployment, require current PR checks and review, then repeat target schema readiness and the deployment smoke checks.
