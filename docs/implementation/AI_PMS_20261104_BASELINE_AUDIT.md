# AI-PMS Baseline Audit Report (04/11/2026)

## 1. Environment & Paths
- **Backend Path:** `D:\AI_PMS\ai-pms-backend`
- **Frontend Path:** `D:\AI_PMS\ai-pms-frontend`
- **Solution:** `D:\AI_PMS\ai-pms-backend\AIPMS.sln` (.NET 8.0 SDK / C# 12)
- **Local SQL Database:** SQL Server Express (`Server=.\SQLEXPRESS;Integrated Security=true;TrustServerCertificate=true;`)
- **Backend Current Branch:** `task/be-09-evaluator-scoring-contract`
- **Backend Baseline Base:** `develop` @ `01ec826` (origin base `14eecc7` + AI merge `01ec826`)
- **Frontend Current Branch:** `develop` @ `6376648`

---

## 2. Ticket Status Matrix (BE-01 through BE-09)

| Ticket | Scope & Purpose | Implementation Branch | Commit | Verification Status | Tests |
|---|---|---|---|---|---|
| **BE-01** | Review Gate Template Academic Authority Enforcement | `task/be-01-template-authority` | `a107270` | `TESTED_LOCAL` | 339 lines of authority tests |
| **BE-02** | 20 Review Gate Baseline Behavior Tests | `task/be-02-review-gate-behavior-tests` | `03ead61` | `TESTED_LOCAL` | 20/20 Passed (1152 lines) |
| **GATE-02** | Interdisciplinary composite major & timeline characterization | Integrated in BE-02 suite | `03ead61` | `TESTED_LOCAL` | Test 11 documented & verified |
| **BE-03** | 12 Eleven-Component Scoring & Weight Invariant Tests | `task/be-03-eleven-component-scoring-tests` | `78c3214` | `TESTED_LOCAL` | 12/12 Passed (302 lines) |
| **BE-04** | Cold Submission Template `COMMON` Scope | `task/be-04-cold-template-common-scope` | `8059b69` | `TESTED_LOCAL` | 4/4 ColdTemplateScopeTests passed |
| **BE-05** | Centralized Upload Policies API & Limits | `task/be-05-upload-policies-api` | `f56a7d3` | `TESTED_LOCAL` | 11/11 ConsistencyTests passed |
| **BE-06** | Rubric Criterion Quality Levels & Guidance | `task/be-06-rubric-criterion-levels` | `691144a` | `TESTED_LOCAL` | 12/12 RubricLevelTests passed |
| **BE-07** | Rubric Academic Scope & Stage Metadata (7 columns) | `task/be-07-rubric-scope-metadata` | `d21a54d` | `TESTED_LOCAL` | 11/11 RubricScopeTests passed |
| **BE-08** | Rubric Four-Eyes Approval Workflow | `task/be-08-rubric-approval-workflow` | `cf91db2` | `TESTED_LOCAL` | 13/13 ApprovalWorkflowTests passed |
| **BE-09** | Evaluator Assignment Projection & ParentId | `task/be-09-evaluator-scoring-contract` | `cbdec5e` / `a1cb733` | `TESTED_LOCAL` | 5/5 ContractTests passed |

---

## 3. Database Migrations Verified

1. `20261030_cold_template_common_scope.sql` (BE-04) - Nullable major_id, scope column & checks.
2. `20261030_add_rubric_criterion_levels.sql` (BE-06) - `rubric_criterion_levels` table + guidance columns.
3. `20261030_add_rubric_scope_fields.sql` (BE-07) - 7 scope fields on `rubrics`.
4. `20261030_add_rubric_approvals.sql` (BE-08) - `PENDING_APPROVAL` status + `rubric_approvals` audit table.
- All 4 migrations registered in `db/e2e/migrations.json` and verified idempotent.

---

## 4. Upload Policy Endpoints (11 Sites)
Centralized in `UploadPolicies.cs`:
1. `REVIEW_GATE_EVIDENCE` (22 MiB)
2. `DELIVERABLE_VERSION` (22 MiB)
3. `FINAL_SUBMISSION_ARTIFACT` (22 MiB)
4. `PROJECT_REPORT` (22 MiB)
5. `COLD_SUBMISSION_FILE` (22 MiB)
6. `RESULT_APPEAL_EVIDENCE` (22 MiB)
7. `ACADEMIC_KNOWLEDGE_DOCUMENT` (22 MiB)
8. `CHAT_ATTACHMENT` (22 MiB)
9. `STUDENT_QUALIFICATION_CERTIFICATE` (20 MiB effective / 22 MiB envelope)
10. `STUDENT_ACCOUNT_IMPORT` (6 MiB)
11. `STUDENT_CURRICULUM_IMPORT` (9 MiB)

---

## 5. Contract Cards in `docs/contracts/`
- `docs/contracts/files/upload-policies.json` (BE-05)
- `docs/contracts/evaluations/rubric-with-levels.json` (BE-06)
- `docs/contracts/evaluations/rubric-scoped.json` (BE-07)
- `docs/contracts/evaluations/rubric-approval-flow.json` (BE-08)
- `docs/contracts/evaluations/evaluator-assignment-with-levels.json` (BE-09)
- `docs/contracts/evaluations/evaluator-assignment-legacy.json` (BE-09)
- `docs/contracts/evaluations/cold-template-common-scope.json` (BE-04)
