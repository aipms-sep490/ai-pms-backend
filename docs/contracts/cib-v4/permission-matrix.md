# CIB v4 Authority, Role & Resource Permission Matrix

> **Core Rule**: Authorization in AI-PMS is evaluated against **Role + Persisted Project State + Resource Assignment Scope**. A global system role alone never grants access to project-scoped or assignment-scoped resources without matching persisted ownership or assignment predicates.

---

## 1. Actor Taxonomy

1. **Student Leader (`STUDENT_LEADER`)**: User with system role `STUDENT`, active membership in the team, and `IsLeader = true`.
2. **Student Member (`STUDENT_MEMBER`)**: User with system role `STUDENT`, active membership in the team, `IsLeader = false`.
3. **Department Staff (Lead) (`LEAD_STAFF`)**: User with system role `DEPARTMENT_STAFF` matching the project's lead department (`LeadDepartmentId`).
4. **Participating Department Staff (`PARTICIPATING_STAFF`)**: User with system role `DEPARTMENT_STAFF` belonging to a participating major's department in an `INTERDISCIPLINARY` project, but not the lead department.
5. **Supervisor (`SUPERVISOR`)**: User with system role `LECTURER` holding an active primary assignment in `supervisor_assignments` (`IsPrimary = true`, `EndedAt IS NULL`).
6. **Mentor (`MENTOR`)**: User with system role `LECTURER` holding an active major-scoped or co-supervisor mentor assignment.
7. **Evaluator (`EVALUATOR`)**: User with system role `LECTURER` possessing an active assignment row in `evaluation_assignments` (`Status = 'ACTIVE'`, matching `DepartmentId`).
8. **Administrator (`ADMIN`)**: User with system role `ADMIN`. Persisted in database; mutations must verify database role rather than trusting stale JWT claims alone.

---

## 2. Protected Operation Authority Matrix

| Operation | Actor Scope | Required Resource Assignment | Required Entity State | Allowed? | Denied HTTP Status | Backend Source Authority |
|---|---|---|---|---|---|---|
| **Upload Qualification Certificate** | `STUDENT` | Must be authenticated student owner | Account `ACTIVE` | ✅ Yes | `401 Unauthorized`<br>`403 Forbidden` (if not student) | `StudentQualificationsController`<br>`UploadStudentQualificationCertificateCommand` |
| **List Project Tasks** | Any active member, assigned supervisor, scoped staff, or admin | Project membership or staff/admin scope | Any project state | ✅ Yes | `403 Forbidden` (wrong project / foreign actor) | `TasksController`<br>`ProjectAccessService.CanAccessAsync` |
| **Filter Tasks by Discipline** | Active member, supervisor, staff, admin | Project scope | Any project state | ⚠️ *BE-FE-01 Pending* | Parameter not yet wired | `TaskRepository.GetTasksAsync` (needs wiring) |
| **Submit Final Package** | `STUDENT_LEADER` only | Active team leader | Project state = `FINAL_SUBMISSION`; active submission period | ✅ Yes | `403 Forbidden` (member or foreign actor)<br>`409 Conflict` (stale token or wrong state) | `FinalSubmissionsController`<br>`SubmitFinalSubmissionCommand` |
| **Assign Evaluator** | `LEAD_STAFF` or `ADMIN` | Staff in lead department or admin | Project period = `EVALUATION`; project in final state | ✅ Yes | `403 Forbidden` (participating staff without lead role, lecturer, student)<br>`409 Conflict` (duplicate assignment) | `EvaluationDraftsController`<br>`AssignEvaluatorCommand` |
| **Revoke Evaluator Assignment** | `LEAD_STAFF` or `ADMIN` | Staff in same department or admin | Assignment status = `ACTIVE`; evaluation not finalized | ✅ Yes | `403 Forbidden` (foreign department staff)<br>`409 Conflict` (stale token or evaluation already `FINALIZED`) | `EvaluationDraftsController`<br>`RevokeEvaluatorCommand` |
| **View Assignment Detail** | Assigned `EVALUATOR`, managing `STAFF`, or `ADMIN` | Assigned `EvaluatorId == ActorId` (ACTIVE, same department) OR manager | Assignment exists | ✅ Yes | `403 Forbidden` (wrong lecturer, revoked assignment, or wrong department) | `EvaluationDraftsController.AssignmentDetail`<br>`EvaluationAssignmentAccessService.GetAsync` |
| **View Assignment Evidence** | Assigned `EVALUATOR`, managing `STAFF`, or `ADMIN` | Assigned `EvaluatorId == ActorId` (ACTIVE) OR manager | Assignment exists | ✅ Yes | `403 Forbidden` (outside assignment scope, revoked evaluator) | `EvaluationDraftsController.AssignmentEvidence`<br>`EvaluationAssignmentAccessService.EvidenceAsync` |
| **Create Evaluation Draft** | Assigned `EVALUATOR` | Must be assigned lecturer in `evaluation_assignments` | Assignment status = `ACTIVE`; project period active | ✅ Yes | `403 Forbidden` (not assigned or revoked)<br>`409 Conflict` (draft already exists) | `EvaluationDraftsController.Create`<br>`CreateEvaluationDraftCommand` |
| **Save Evaluation Draft** | Assigned `EVALUATOR` | Owner of the evaluation draft | Evaluation status = `DRAFT` | ✅ Yes | `403 Forbidden` (different evaluator)<br>`409 Conflict` (stale concurrency token or already finalized) | `EvaluationDraftsController.Save`<br>`SaveEvaluationDraftCommand` |
| **Finalize Evaluation** | Assigned `EVALUATOR` | Owner of the evaluation draft | Evaluation status = `DRAFT`; all required criteria scored | ✅ Yes | `400 Bad Request` (missing required criteria)<br>`409 Conflict` (already finalized or stale token) | `EvaluationDraftsController.FinalizeEvaluation`<br>`FinalizeEvaluationCommand` |
| **Preview Project Result** | `LEAD_STAFF`, managing `STAFF`, or `ADMIN` | Lead department staff or admin | All required evaluators scored | ✅ Yes | `403 Forbidden` (lecturer, student)<br>`409 Conflict` (missing evaluations) | `ProjectResultsController.Preview`<br>`PreviewProjectResultQuery` |
| **Publish Project Result** | `LEAD_STAFF` or `ADMIN` | Lead department staff or admin | Confirmation token valid; project in evaluation stage | ✅ Yes | `403 Forbidden` (student, lecturer)<br>`409 Conflict` (stale/invalid confirmation token) | `ProjectResultsController.Publish`<br>`PublishProjectResultCommand` |
| **Read Student Result (Owner Student)** | `STUDENT_MEMBER` or `STUDENT_LEADER` | Active member belonging to the result's student ID | Result status = `PUBLISHED` | ✅ Yes (Redacted: `snapshotJson = "{}"`) | `403 Forbidden` (wrong student)<br>`404 NotFound` (not yet published) | `PolicyEvaluationController.Student`<br>`EvaluationSchemeService.GetStudentAsync` |
| **Read Student Result (Staff / Admin)** | `LEAD_STAFF` or `ADMIN` | Department staff or admin | Result status = `PUBLISHED` | ✅ Yes (Full: unredacted `snapshotJson`) | `403 Forbidden` (foreign staff without department match) | `PolicyEvaluationController.Student`<br>`EvaluationSchemeService.GetStudentAsync` |

---

## 3. Negative & Boundary Scenarios

### 3.1 Wrong Project Scope
- If an evaluator, supervisor, or student attempts to access `/api/v1/projects/{projectId}/...` where their ID has no persisted relation to `projectId`, the server immediately responds with `403 Forbidden` via `IProjectAccessService` or `IEvaluationAssignmentAccessService`.

### 3.2 Wrong Department Boundary
- In `INTERDISCIPLINARY` mode, Department Staff belonging to Department B cannot publish results or revoke assignments configured under Department A's lead authority unless explicitly designated. Server enforces `DepartmentId == row.DepartmentId` check (`403 Forbidden`).

### 3.3 Revoked Evaluator
- If an assignment has `Status = 'REVOKED'`, the evaluator attempting to create an evaluation draft or access assignment evidence receives `403 Forbidden` (`"The assignment is outside your scope."`) or `409 Conflict`.

### 3.4 Stale Concurrency Token
- If two managers concurrently attempt to revoke the same evaluator, or two team members attempt to submit the final package with a stale `concurrencyToken`, the second request is aborted with `409 Conflict` with a concurrency violation description.

### 3.5 Student Privacy Protection
- A student querying `/api/v1/projects/{projectId}/students/{targetStudentId}/result` where `targetStudentId != actor.UserId` receives `403 Forbidden`.
- When querying their own result, the server enforces privacy redaction: `snapshotJson` is returned as `"{}"` to prevent raw criterion breakdown leaks unless explicitly published via approved future channels.
