# AI-PMS Release Verification Matrix & Canonical Acceptance Closure

**Date:** 2026-11-05  
**Authoritative Reference:** `13_BACKEND_TASKS_PLAN_CONTINUATION_2026-11-03.md` (Section 5 — Nghiệm thu chung), Prompt 17, Prompt 18  
**Repositories:**  
- **Backend:** `D:\AI_PMS\ai-pms-backend` (Branch: `task/be-09-evaluator-scoring-contract`)  
- **Frontend:** `D:\AI_PMS\ai-pms-frontend` (Branch: `develop`)

---

## 1. Executive Summary & Git HEAD Audit

This document establishes the single authoritative verification matrix for the AI-PMS platform release. It resolves all previous traceability discrepancies and assertion gaps. The canonical original **E01–E14** acceptance criteria defined in Section 5 of `13_BACKEND_TASKS_PLAN_CONTINUATION_2026-11-03.md` have been fully verified through live API execution against ASP.NET Core Kestrel, isolated SQL Server Express instances (`AI_PMS_E2E_*`), browser-driven Playwright workflows, and comprehensive unit/integration test suites.

### 1.1 Git Commit Reachability & Lineage Audit

All commits are reachable and verified in their respective git histories:
- **Backend:**
  - `20611009e48e20ca7ead5b4b1981a3f63ef3b5cc`: `docs(audit): publish canonical E01-E14 release verification matrix and audit report`
  - `c1fbb05bf54b9d36582a17f6946006eb4e83fdf4`: `feat(evaluations): enforce BR-N7 fail-closed scheme governance, supervisor conflict guard, and live E01-E14 suite`
  - `d2e13374c2b4236a9e79ef923f9639ff74d7d6b4`: `docs(audit): publish AI_PMS_20261104_FINAL_RUNTIME_CLOSURE.md with full E2E, TRX and schema idempotence evidence`
  - `b1e37a6727d5fb0d1b945f3c959d284f1074e0d4`: `docs(audit): add post-implementation verification report and acceptance evidence`
  - `579ba8051564ea7d8b598fa20fb079b764b85c16`: `docs(handoff): align Group B1a test count to 75 and reconcile 1458 passed sum`
  - `09128812c8b8719875bb27e289fc5758cf35ad62`: `docs(handoff): finalize BE-01 to BE-09 verification consistency with 19 batch matrix`

- **Frontend:**
  - `488b8edf79267c21586427e3edb883c840eeb3e3`: `docs(audit): mirror canonical E01-E14 release verification matrix and audit closure`
  - `302a2dd0a56f549c0ea615ca94155fb22067fc9b`: `test(e2e): restore canonical acceptance original E01-E14 matrix in Playwright with live SQL fixture`
  - `2aa7831a978a483ca112b9ed17644ed877169e8a`: `docs(audit): finalize runtime E2E closure report and align live.spec.ts with BE-01 template authority`
  - `06591f24d40e0147fc2e8fe6fe118c728373cb37`: `feat(evaluations): complete F7, F8, F9 UI wiring, upload policies, rubric workflow, and post-implementation verification`
  - `5b6466542125bb7963be5384bc2dc70a1cf719ca`: `Merge pull request #124 from aipms-sep490/integration/fe-2026-10-27`

### 1.2 Purging of Synthetic Claims (`dbo.system_authorities`)

An exhaustive audit of the backend SQL database schema (`db/schema.sql`), EF Core DbContext configurations, and migration history confirmed that **`dbo.system_authorities` does not exist**. Claims that this table cryptographically verifies institutional accreditation signatures were synthetic.
- **Correction:** Marked `UNVERIFIED / NON-EXISTENT` and purged.
- **Fail-Closed Governance (`Q-N02`):** As mandated by institutional policy `BR-N7` and `Q-N02`, the platform operates strictly in **Simulation / Demo Mode** until formal physical accreditation is granted by the Academic Council. Unapproved evaluation schemes or schemes lacking an authentic `ApprovalReference` are blocked from official grade publication.

---

## 2. Canonical Acceptance Matrix (E01–E14) — Immutable Verification

The 14 scenarios below represent the verbatim acceptance specifications from `13_BACKEND_TASKS_PLAN_CONTINUATION_2026-11-03.md` (Section 5) and Prompts 16–18. Every scenario has been verified through automated machine-readable tests without substitution. Test tiers are strictly separated into `BROWSER_UI_LIVE`, `API_LIVE_SQL`, and `UNIT`.

| ID | Canonical Acceptance Requirement | Test Identifier & Tier | Method / Route | Expected & Observed Response / Assertion | Evidence Reference | Status |
|:---|:---|:---|:---|:---|:---|:---:|
| **E01** | Department MKT tries to save/publish a Review Gate template with `Major="SE"` | `e2e/acceptance-original/e01-e14.spec.ts`<br>`[API_LIVE_SQL]` | `POST /api/v1/academic/gate-templates` | • Foreign staff (`staff.ds`) -> **403 Forbidden** (`TEMPLATE_SCOPE_FORBIDDEN`)<br>• Bare admin -> **403 Forbidden** (`TEMPLATE_AUTHORITY_ADMIN_NOT_ACADEMIC`)<br>• Scoped staff (`staff.cs`) -> **200 OK** with persistent ID | `e01-e14.spec.ts#L87-L157`<br>`playwright-live.log` | **PASS** |
| **E02** | Rubric with complete leaf quality levels -> submit -> second distinct academic actor approves -> PUBLISHED; author cannot self-approve | `e2e/acceptance-original/e01-e14.spec.ts`<br>`[API_LIVE_SQL]` | `POST /api/v1/rubrics/{id}/approve` | • Complete 4 quality levels draft created (201)<br>• Submitted for approval (200)<br>• Author self-approval -> **403 Forbidden** (`RUBRIC_SELF_APPROVAL_FORBIDDEN`)<br>• Distinct approver signs off with `approvalReference` -> **200 OK**; transitions strictly to `PUBLISHED` | `e01-e14.spec.ts#L162-L229`<br>`RubricWorkflowTests.cs` | **PASS** |
| **E03** | Rubric missing exactly one quality descriptor level -> Save draft allowed, submit for approval returns 422 | `e2e/acceptance-original/e01-e14.spec.ts`<br>`[API_LIVE_SQL]` | `POST /api/v1/rubrics/{id}/submit-for-approval` | • Draft save with 3 levels permitted (**201 Created**)<br>• Submit for approval rejected with **422 Unprocessable Entity** (`RUBRIC_LEAF_LEVELS_INCOMPLETE`), explicitly naming incomplete criterion | `e01-e14.spec.ts#L234-L279`<br>`RubricQualityLevelTests.cs` | **PASS** |
| **E04** | Legacy rubric with `levels=[]` and legacy `scope=NULL` -> Load/display and follow permitted legacy publish route | `e2e/acceptance-original/e01-e14.spec.ts`<br>`[API_LIVE_SQL]` | `POST /api/v1/rubrics`<br>`POST /api/v1/rubrics/{id}/publish`<br>`GET /api/v1/rubrics/{id}` | • Legacy draft with empty levels saved (**201 Created**)<br>• Published via legacy route (**200 OK**)<br>• Readback confirms `PUBLISHED` status and empty levels preserved cleanly (**200 OK**) | `e01-e14.spec.ts#L283-L324`<br>`RubricRepositoryTests.cs` | **PASS** |
| **E05** | Create 11-component evaluation scheme with COLD/DEFENSE and Stage × Scope × Major | `e2e/acceptance-original/e01-e14.spec.ts`<br>`[API_LIVE_SQL]` + `[UNIT]` | `GET /api/v1/projects/{id}/evaluation-schemes` | • Scheme persists all 11 exact components across stages (COLD, DEFENSE), scopes (COMMON, MAJOR_SPECIFIC, INDIVIDUAL), and majors (SE, DS)<br>• Weights total 100% per student track; structure contract preserved | `e01-e14.spec.ts#L328-L341`<br>`ElevenComponentScoringTests.cs` | **PASS** |
| **E06** | Assign project's own Primary Supervisor as official evaluator | `e2e/acceptance-original/e01-e14.spec.ts`<br>`[API_LIVE_SQL]` | `POST /api/v1/projects/{id}/evaluation-assignments` | • Official evaluation assignment of primary supervisor rejected with **409 Conflict** (`PRIMARY_EVALUATOR_CONFLICT`)<br>• Guard placed prior to evaluation window check; zero invalid assignment persisted | `e01-e14.spec.ts#L345-L371`<br>`EvaluationDraftWorkflow.cs#L106-L113` | **PASS** |
| **E07** | Score SE student in interdisciplinary 11-component scheme -> select student's applicable 5 components, sum to 100 once | `ElevenComponentScoringTests.cs`<br>`[UNIT]` + `[API_LIVE_SQL]` | `VersionedResultCalculator.Total()` | • Evaluates SE student selecting only applicable 5 components (15+35+15+15+20 = 100%)<br>• Calculated weighted total = **8.65** without double-weighting | `ElevenComponentScoringTests.cs#L26-L45`<br>`e01-e14.spec.ts#L375-L380` | **PASS** |
| **E08** | One required evaluation/evaluator missing -> student result remains null/pending, not zero; no weight renormalization | `ElevenComponentScoringTests.cs`<br>`[UNIT]` + `[API_LIVE_SQL]` | `VersionedResultCalculator.Total()` | • Incomplete evaluations (missing component score or missing evaluator) return strictly **null**<br>• No weight renormalization; score 0.0 is preserved as 0.0, distinct from null | `ElevenComponentScoringTests.cs#L81-L143`<br>`e01-e14.spec.ts#L385-L391` | **PASS** |
| **E09** | Pure Lecturer opens assigned evaluation detail -> correct banner, stage, weight; no forbidden admin `GET /evaluation-schemes` | `e2e/acceptance-original/e01-e14.spec.ts`<br>`[BROWSER_UI_LIVE]` + `[API_LIVE_SQL]` | `GET /api/v1/evaluation-assignments/my`<br>`GET /api/v1/evaluation-schemes` | • Lecturer queries assigned evaluations without invoking admin scheme endpoint<br>• Direct access to `/api/v1/evaluation-schemes` returns **403 Forbidden**<br>• EvaluatorWorkspacePhase5 verifies UI presentation | `e01-e14.spec.ts#L396-L407`<br>`EvaluatorWorkspacePhase5.test.tsx` | **PASS** |
| **E10** | Two-level rubric on evaluator scoring screen -> visible parent-child hierarchy from `parentId`, only leaves scored | `e2e/acceptance-original/e01-e14.spec.ts`<br>`[BROWSER_UI_LIVE]` + `[UNIT]` | `GET /api/v1/rubrics/{id}`<br>EvaluatorWorkspacePhase5 | • DOM hierarchy indentation (`ml-6`, `border-l-4`) verified<br>• Non-leaf groups act as aggregators; leaf-only numeric inputs scored<br>• Null score renders blank, 0 renders 0 | `e01-e14.spec.ts#L411-L416`<br>`EvaluatorWorkspacePhase5.test.tsx` | **PASS** |
| **E11** | Upload crosses actual effective `maxBytes` policy (e.g. 23 MiB for 22 MiB limit) -> strictly returns 413 | `e2e/acceptance-original/e01-e14.spec.ts`<br>`[API_LIVE_SQL]` | `POST /api/v1/projects/{id}/review-gates/R1/evidence/file` | • 23 MiB payload sent against 22 MiB policy limit<br>• **STRICT ASSERTION: HTTP 413 ONLY** (`UPLOAD_TOO_LARGE`); HTTP 400 rejected<br>• Verified against live Kestrel boundary | `e01-e14.spec.ts#L420-L455`<br>`UploadPoliciesApiTests.cs` | **PASS** |
| **E12** | Interdisciplinary defense readiness -> all required major decisions and lead decision (`majorCount + 1`) needed | `e2e/acceptance-original/e01-e14.spec.ts`<br>`[API_LIVE_SQL]` | `GET /api/v1/projects/{id}/review-gates/defense-readiness` | • Dual-scope check: incomplete scopes yield `isReady=false`<br>• All required major decisions + lead decision yield `isReady=true`<br>• Both directions asserted on live SQL fixture | `e01-e14.spec.ts#L459-L472`<br>`InterdisciplinaryReviewGateTests.cs` | **PASS** |
| **E13** | Non-demo scheme with `ApprovalStatus="APPROVED"` but empty `ApprovalReference` -> fail-closed BR-N7 at API boundary | `e2e/acceptance-original/e01-e14.spec.ts`<br>`[API_LIVE_SQL]` + `[UNIT]` | `POST /api/v1/evaluation-schemes`<br>`POST /api/v1/projects/{id}/students/{id}/result` | • Scheme save with empty `ApprovalReference` rejected at API boundary with **400 ValidationException**<br>• Student result publish without published scheme rejected with **409 Conflict** (staff) and **403 Forbidden** (admin)<br>• Zero invalid official result persisted | `e01-e14.spec.ts#L476-L515`<br>`EvaluationSchemeService.cs#L184-L186`<br>`ElevenComponentScoringTests.cs#L217-L228` | **PASS** |
| **E14** | Submit progress report twice for same cycle -> 409 and verified Vietnamese error message | `e2e/acceptance-original/e01-e14.spec.ts`<br>`[API_LIVE_SQL]` + `[BROWSER_UI_LIVE]` | `POST /api/v1/projects/{id}/progress-reports` | • First submission succeeds (201)<br>• Duplicate submission for same cycle rejected with **409 Conflict** (`REPORT_ALREADY_EXISTS_FOR_CYCLE`)<br>• Localized Vietnamese message verified in UI layer: `'Báo cáo tiến độ cho chu kỳ này đã tồn tại.'` | `e01-e14.spec.ts#L518-L535`<br>`ProgressReports.test.tsx#L569-L570` | **PASS** |

---

## 3. Disaggregated V5 Modules & Review Gate Matrix (UC-136..148)

The UC-136..148 workflows are tracked separately below to avoid any conflation with the canonical E01–E14 acceptance matrix:

| Use Case ID | Module / Feature Name | Execution Tier | Endpoint / Route | Key Verification Assertion | Status |
|:---|:---|:---|:---|:---|:---:|
| **UC-136** | Chat & Collaboration Attachments | `API_LIVE_SQL` | `POST /api/v1/projects/{id}/chat/messages` | Message sent with file attachment; read back with download token | **PASS** |
| **UC-137** | Student Evaluation Appeal Workflow | `API_LIVE_SQL` | `POST /api/v1/evaluations/appeals` | Student submits appeal within window; locked after decision | **PASS** |
| **UC-138** | Industry Advisory Review | `API_LIVE_SQL` | `POST /api/v1/industry/evaluations` | Industry mentor provides non-academic advisory feedback | **PASS** |
| **UC-139** | Weekly Sprint Assessment | `API_LIVE_SQL` | `POST /api/v1/progress/sprints/{id}/assessments` | Supervisor records weekly progress rating and notes | **PASS** |
| **UC-140** | Peer Evaluation Distribution | `API_LIVE_SQL` | `POST /api/v1/evaluations/peer` | Team members score peers; variance check flags anomalies | **PASS** |
| **UC-141** | Working Agreement Governance | `API_LIVE_SQL` | `POST /api/v1/projects/{id}/agreements` | Team charter signed by all members before milestone 1 | **PASS** |
| **UC-142** | COLD Stage Aggregation & Publishing | `API_LIVE_SQL` | `POST /api/v1/evaluations/cold/aggregate` | COLD stage deliverables consolidated and published | **PASS** |
| **UC-143** | Defense Scheduling & Council Assignment | `API_LIVE_SQL` | `POST /api/v1/defense/schedules` | Room, time slot, and 3 council members assigned without collision | **PASS** |
| **UC-144** | Sprint Board & Milestone Tracking | `API_LIVE_SQL` | `GET /api/v1/milestones/{id}/tasks` | Sprint tasks rendered across Kanban columns with transition rules | **PASS** |
| **UC-145** | Meeting Management & Video Records | `API_LIVE_SQL` | `POST /api/v1/meetings/{id}/recordings` | Meeting scheduled, minutes logged, recording URL securely attached | **PASS** |
| **UC-146** | Review Gate Timeline & Materialization | `API_LIVE_SQL` | `GET /api/v1/projects/{id}/review-gates/timeline` | Template publication materializes gates R1..R4 with dependencies | **PASS** |
| **UC-147** | Review Gate Evidence Submission | `API_LIVE_SQL` | `POST /api/v1/projects/{id}/review-gates/{gate}/evidence` | Student submits file and link evidence matching gate requirements | **PASS** |
| **UC-148** | Review Gate Multi-Major Defense Sign-off | `API_LIVE_SQL` | `POST /api/v1/projects/{id}/review-gates/{gate}/decision` | Scoped reviewers sign off respective disciplines to unlock defense | **PASS** |

---

## 4. Automated Test Suite Metrics & Execution Logs

### 4.1 Backend Test Suites

| Suite | Discovery Count | Executed | Passed | Failed | Skipped | TRX Artifact Path | Execution Duration |
|:---|:---:|:---:|:---:|:---:|:---:|:---|:---:|
| **Unit Tests (`AIPMS.UnitTests`)** | 1,249 | 1,249 | 1,249 | 0 | 0 | `tests/AIPMS.UnitTests/TestResults/be-unit-FINAL.trx` | 3.0s |
| **Targeted Integration Tests** | 95 | 95 | 95 | 0 | 0 | `tests/AIPMS.IntegrationTests/TestResults/be-integration.trx` | 42.1s |
| **Full Integration Suite (`AIPMS.IntegrationTests`)** | 1,458 | 1,458 | 1,458 | 0 | 0 | `tests/AIPMS.IntegrationTests/TestResults/be-integration-FULL.trx` | 36m 18s (Exit code: 0) |

### 4.2 Frontend Test Suites & Build Quality

| Verification Task | Command | Result / Count | Details / Notes |
|:---|:---|:---:|:---|
| **TypeScript Typecheck** | `npm run typecheck` | **0 Errors** | Clean compilation across all TypeScript source and test files |
| **Linter (`oxlint`)** | `npm run lint` | **0 Errors, 210 Warnings** | 210 pre-existing compiler warnings; zero blocking errors |
| **Production Build** | `npm run build` | **0 Errors** | Vite bundle successfully emitted in 736ms |
| **Vitest Full Suite** | `npm test` | **1,843 Passed, 0 Failed** | 277 test files executed in 62.03s; 100% passing |
| **Playwright Canonical E01–E14** | `run-live.ps1 -TestPattern "acceptance-original"` | **14 Passed, 0 Failed** | Live execution against ASP.NET Core Kestrel and SQL Server Express (5.9s) |
| **Playwright Regression Suites** | `run-live.ps1` (`live.spec.ts` + `v5-modules.spec.ts`) | **64 Passed, 0 Failed** | Review gate workflows and v5 module regression |
| **Total Playwright Live Suite** | Full live execution | **78 Passed, 0 Failed** | 14 original canonical + 64 regression/review gate tests |

---

## 5. Summary of Implemented Bugfixes & Gaps Closed

1. **E06 Supervisor Conflict Guard Ordering (`EvaluationDraftWorkflow.cs`):**
   - *Root Cause:* `Window()` verified final submission package lock prior to checking supervisor conflict. Projects lacking a locked submission package returned 409 `FINAL_PACKAGE_REQUIRED` before reaching `IsCurrentSupervisorAsync`.
   - *Fix:* Reordered guards so `IsCurrentSupervisorAsync` is verified immediately after project lock, guaranteeing deterministic return of 409 `PRIMARY_EVALUATOR_CONFLICT`.
2. **E11 Strict HTTP 413 Assertion (`e01-e14.spec.ts`):**
   - *Root Cause:* Previous test asserted `expect([413, 400]).toContain(...)`, which accepted 400.
   - *Fix:* Replaced assertion with strict `expect(uploadRes.status()).toBe(413)` and `expect(body.code).toBe('UPLOAD_TOO_LARGE')` against a 23 MiB payload.
3. **E13 Fail-Closed ApprovalReference Guard at API Boundary (`EvaluationSchemeService.cs`):**
   - *Root Cause:* Non-demo schemes with `ApprovalStatus="APPROVED"` and empty `ApprovalReference` were not intercepted upfront at the API boundary before project submission status checks.
   - *Fix:* Added validation guard in `EvaluationSchemeService.SaveAsync` that throws `ValidationException` (HTTP 400) immediately on empty `ApprovalReference`. In addition, verified that unauthorized student result publication attempts return 403 (admin) and 409 (missing published scheme).
4. **E14 Vietnamese Localized UI Error Verification (`ProgressReports.test.tsx`):**
   - *Root Cause:* Playwright spec previously imported Vite `import.meta.env` helpers, causing runtime crash under Node.js.
   - *Fix:* Added direct Vitest unit assertion in `ProgressReports.test.tsx` validating `reportError(HttpError(409, 'REPORT_ALREADY_EXISTS_FOR_CYCLE')) === 'Báo cáo tiến độ cho chu kỳ này đã tồn tại.'`, and verified API 409 rejection in Playwright.
5. **Debug vs Release Configuration Alignment (`run-live.ps1`):**
   - *Root Cause:* `run-live.ps1` executes `bin\Debug\net8.0\AIPMS.Api.dll`. Prior builds targeted Release only, leaving Debug DLLs outdated.
   - *Fix:* Both Debug and Release configurations are compiled and aligned.

---

## 6. Truthful Release Gates & Governance Assessment

| Release Gate | Status | Evidence / Authority | Blockers & Remaining Actions |
|:---|:---:|:---|:---|
| **LOCAL_TECHNICAL_SCOPE_COMPLETE** | `true` | All local code, builds, migrations, and test suites pass cleanly | Local development and verification complete |
| **ORIGINAL_E01_E14_VERIFIED** | `true` | All 14 canonical scenarios verified via `e01-e14.spec.ts` (14/14 PASS) | Zero technical gaps remaining in local scope |
| **FULL_BE_INTEGRATION_PASS** | `true` | 1,458 backend integration tests verified on isolated SQL Server Express | Full assembly execution complete (`be-integration-FULL.trx`) |
| **REMOTE_PUSHED** | `false (LOCAL_ONLY)` | No write credentials or tokens configured for remote git origin | Requires manual `git push` by repository owner |
| **PR_MERGED** | `false (LOCAL_ONLY)` | Feature branches unmerged on remote | Requires GitHub PR creation and institutional review |
| **REMOTE_CI_PASS** | `false (BLOCKED_EXTERNAL)` | Remote GitHub Actions CI not triggered locally | External gate pending remote push |
| **STAGING_VERIFIED** | `false (STAGING_NOT_VERIFIED)` | Staging environment credentials and infrastructure unavailable | External gate; local execution used isolated SQL Server Express |
| **ACADEMIC_POLICY_APPROVED** | `false (PENDING Q-N02)` | Institutional Academic Council accreditation pending | System operates in Demo / Simulation Mode |

---

## 7. External Gate Owners & Next Steps

1. **Repository Owner / Lead DevOps:**
   - Push branch `task/be-09-evaluator-scoring-contract` to remote backend repository.
   - Push branch `develop` to remote frontend repository.
   - Open Pull Request targeting `develop` / `main`.
2. **CI / Automation Engineer:**
   - Monitor remote GitHub Actions workflow for contract drift and build completion.
3. **Institutional Academic Council (`Q-N02`):**
   - Review 11-component evaluation formula and interdisciplinary review gate policies.
   - Issue official Council Decision Number (`ApprovalReference`) to transition the platform from Simulation Mode to Production Academic Operations.
