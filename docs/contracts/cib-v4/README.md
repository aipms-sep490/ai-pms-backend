# AI-PMS CIB v4 Contract Package & Acceptance Foundation

- **Contract Version**: `1.0.0-cib.v4`
- **As of Date**: 2026-10-10
- **Authoritative Backend Baseline**: `09abf9595193cbb721130f5db8dd84d67cd13116` (`origin/develop`)
- **Authoritative Frontend Baseline**: `eef31c2e3f733e4bc3c25f2fd5b790dcbf40a741` (`origin/develop`)
- **Parent Specifications**: AI-PMS CIB v4 (Document ID `AI-PMS-CIB-004`, 09/10/2026)

---

## 1. Scope & Ticket Status Summary

| Ticket | Release Selected? | Contract Status | Implementation Status | Notes |
|---|---|---|---|---|
| **BE-FE-04** | **YES** | **DELIVERED** | **DELIVERED** | Versioned contract package, scenario manifest, acceptance fixtures, runbook, and reset/readback validation. |
| **BE-FE-01** | **YES** | **DECISION_REQUIRED** | **PARTIAL** | Task discipline filter. Query parameter naming (`majorId` vs `discipline`) is unresolved. Repository filtering not yet wired. |
| **BE-FE-02** | **YES** | **DECISION_REQUIRED** | **BLOCKED** | Assignment evidence read model. Lightweight metadata DTO exists; detailed file/item projection depth is unresolved. |
| **BE-FE-03** | **NO** | **NOT_SELECTED** | **NOT_SELECTED** | Typed breakdown DTO for `StudentResult` deferred. Authoritative `SnapshotJson` preserved (redacted as `{}` for owner students). |
| **DBX-01..13** | **NO** | **NOT_SELECTED** | **NOT_SELECTED** | Proposed DB schema extensions remain unapproved. Core schema reuses existing tables only. |

---

## 2. Package Structure

- [`openapi.json`](./openapi.json): Authoritative OpenAPI 3.0 specification generated from the ASP.NET Core Swashbuckle runtime covering all active API routes, schemas, operations, and status codes.
- [`contract.json`](./contract.json): Machine-readable CIB metadata and decision manifest. Defines release tickets (BE-FE-01..04, DBX-01..13), parameter decisions, gate statuses, and synthetic scenario configurations. **Note: `contract.json` is a metadata companion and does NOT masquerade as an OpenAPI document.**
- [`permission-matrix.md`](./permission-matrix.md): Exhaustive authority matrix across 8 actors and 12 protected operations with negative assertion cases.
- [`error-semantics.md`](./error-semantics.md): Error status codes, RFC 7807 `ProblemDetails` models, optimistic concurrency tokens, and confirmation token lifecycles.
- [`scenario-manifest.json`](./scenario-manifest.json): Machine-readable manifest of testable integration scenarios covering `SINGLE_MAJOR` and `INTERDISCIPLINARY` modes, mapped directly to traceable integration tests.
- [`examples/`](./examples/): Request/response payload examples with synthetic identifiers across all current surfaces.
- [`acceptance/README.md`](./acceptance/README.md): Operational runbook for executing acceptance tests against isolated LocalDB without touching shared `AI_PMS`.

---

## 3. Current Documented API Surfaces

### A. Qualification Certificate Upload
- **Route**: `POST /api/v1/student-qualifications/me/certificate`
- **Method / Content-Type**: `POST`, `multipart/form-data`
- **Payload & File Size Limits (Two Distinct Boundaries)**:
  - **HTTP Request Transport Body Ceiling**: Max 22 MB (23,068,672 bytes) enforced via controller `[RequestSizeLimit(22 * 1024 * 1024)]` and `[RequestFormLimits(MultipartBodyLengthLimit = 22 * 1024 * 1024)]`. Any request body or multipart stream exceeding 22 MB is rejected by Kestrel / ASP.NET Core with `413 Payload Too Large`.
  - **Accepted Certificate File Size (Business Validation)**: `<= 20 MiB` (20,971,520 bytes) enforced by `UploadValidator.MaxBytes`. If an uploaded certificate file exceeds 20 MiB, the domain service rejects it with `400 Bad Request` (`detail: "File name or size is invalid (maximum 20 MiB)."`).
  - *Boundary Rule*: An uploaded certificate file of 21–22 MiB will pass HTTP transport limits but will fail business validation with `400 Bad Request`. API clients must never assume a certificate > 20 MiB is valid.
- **Form Fields**:
  - `file`: Required `IFormFile` (size `<= 20 MiB`). Allowed MIME types: PDF, PNG, JPEG.
  - `qualificationType`: String, default `"CAPSTONE_READINESS"`.
  - `trainingStatus`: String, default `"TRAINING_COMPLETED"`.
  - `certificateNumber`: Optional string.
  - `issuedAt`: Optional UTC ISO-8601 string.
  - `expiresAt`: Optional UTC ISO-8601 string.
- **Authorization**: `STUDENT` global role, active account.
- **Responses**:
  - `200 OK`: `StudentQualificationDto` (file `<= 20 MiB`, valid metadata).
  - `400 Bad Request`: Validation failure (empty file, file size `> 20 MiB`, invalid dates, unknown type).
  - `401 Unauthorized`: Missing or invalid bearer token.
  - `403 Forbidden`: Authenticated user is not a student or account is inactive.
  - `413 Payload Too Large`: Overall HTTP multipart request body exceeds 22 MB (23,068,672 bytes).
  - `422 Unprocessable Entity`: Business rejection (e.g. invalid certificate state).

### B. Tasks Listing
- **Route**: `GET /api/v1/tasks/project/{projectId}`
- **Currently Supported Parameters**:
  - `milestoneId`: `long?`
  - `status`: `string?` (`TODO`, `IN_PROGRESS`, `DONE`, `BLOCKED`, `CANCELLED`)
  - `priority`: `string?` (`LOW`, `MEDIUM`, `HIGH`, `CRITICAL`)
  - `assigneeUserId`: `long?`
  - `search`: `string?` (matched case-insensitively against title and description)
  - `dueFrom`: `DateTime?`
  - `dueTo`: `DateTime?`
  - `isOverdue`: `bool?`
  - `isBlocked`: `bool?`
  - `page`: `int`, default `1`
  - `pageSize`: `int`, default `10`
- **Discipline Filter Status**:
  - ⚠️ **NOT YET AVAILABLE**.
  - **BE-FE-01 CONTRACT DECISION REQUIRED**: The query parameter name (`majorId` vs `discipline`) has not been frozen between Frontend and Backend teams. Do not assume or rely on any parameter name until finalized.

### C. Evaluation Assignments & Lifecycle
- **Eligible Evaluators**: `GET /api/v1/projects/{projectId}/eligible-evaluators?periodId={periodId}&page=1&pageSize=20`
- **Create Assignment**: `POST /api/v1/projects/{projectId}/evaluation-assignments`
  - Body: `{ evaluatorId, periodId, evaluationType, scope, majorId?, studentId?, componentId? }`
  - Returns: `201 Created` with `EvaluationAssignmentDto`
- **List Project Assignments**: `GET /api/v1/projects/{projectId}/evaluation-assignments?status=ACTIVE`
- **List My Active Assignments**: `GET /api/v1/evaluation-assignments/my?page=1&pageSize=20`
- **Revoke Assignment**: `POST /api/v1/evaluation-assignments/{id}/revoke`
  - Body: `{ concurrencyToken, reason }`
  - Returns: `200 OK` with revoked `EvaluationAssignmentDto`
- **Assignment Detail**: `GET /api/v1/evaluation-assignments/{id}`
  - Returns: `EvaluationAssignmentDetailDto` (`assignment`, `canScore`, `legacyReadOnly`, `denialReason`).
- **Assignment Evidence**: `GET /api/v1/evaluation-assignments/{id}/evidence`
  - Returns: `EvaluationAssignmentEvidenceDto`.
- **Create Evaluation Draft**: `POST /api/v1/evaluation-assignments/{id}/evaluation` (returns `201 Created` with `EvaluationDraftDto`).
- **Finalize Evaluation**: `POST /api/v1/evaluations/{id}/finalize` (returns `200 OK`).

### D. Assignment Evidence Scope
- **Route**: `GET /api/v1/evaluation-assignments/{id}/evidence`
- **Current authoritatively implemented DTO**:
  ```json
  {
    "assignmentId": 101,
    "projectId": 42,
    "scope": "MAJOR_SPECIFIC",
    "majorId": 2,
    "studentId": null,
    "finalSubmissionId": 501,
    "submittedAt": "2026-09-30T10:00:00Z",
    "itemCount": 4,
    "isReadOnly": true
  }
  ```
- **Projection Depth Decision**:
  - ⚠️ **BE-FE-02 DECISION REQUIRED**: Whether this release provides detailed individual evidence item/file records with protected download URLs or retains lightweight metadata is pending approval. No file projection route has been added.

### E. Locked Final Submission
- **Requirements**: `GET /api/v1/projects/{projectId}/final-submission/requirements`
- **Checklist**: `GET /api/v1/projects/{projectId}/final-submission/checklist`
- **Submit**: `POST /api/v1/projects/{projectId}/final-submission`
  - Requires: Active `FINAL_SUBMISSION` project period, valid `requirementsConcurrencyToken` and `draftConcurrencyToken`.
  - Effect: Final package is locked and permanently immutable. Returns `201 Created` with `FinalSubmissionDto`.

### F. Project Results
- **Preview**: `GET /api/v1/projects/{projectId}/result/preview`
  - Calculates aggregated score across configured components and evaluators.
  - Returns `ProjectResultPreviewDto` containing `confirmationToken`.
- **Publish**: `POST /api/v1/projects/{projectId}/result`
  - Body: `{ "confirmationToken": "..." }`
  - Enforces single-use optimistic confirmation token; returns `201 Created` with `ProjectResultDto`.
- **Read**: `GET /api/v1/projects/{projectId}/result` (returns `ProjectResultDto`).

### G. Student Results
- **Preview**: `GET /api/v1/projects/{projectId}/students/{studentId}/result/preview` (returns `ProjectResultPreviewDto`).
- **Publish**: `POST /api/v1/projects/{projectId}/students/{studentId}/result`
  - Body: `{ "confirmationToken": "..." }`
  - Returns `StudentResultDto`.
- **Read**: `GET /api/v1/projects/{projectId}/students/{studentId}/result`
  - **Privacy Semantics**: When queried by the owner student, `snapshotJson` is redacted to `"{}"`. Full snapshot JSON is restricted to department staff and administrators.
  - **No Typed Breakdown**: BE-FE-03 was NOT selected for this release.

---

## 4. Contract Gates

```ini
mockContractStatus = DONE
dbReadinessStatus = PARTIAL
dbIntegrityStatus = PARTIAL
realLifecycleStatus = FE_DONE_BE_PENDING
```
