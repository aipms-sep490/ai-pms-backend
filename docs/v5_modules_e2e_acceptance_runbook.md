# AI-PMS — 14 Modules v5 Official E2E Acceptance Runbook

> **Document Version**: 2.0.0 (Standardized against official `docs/v5_modules_staging_verification.md` & SRS/SDD UC-135–UC-148)  
> **Target Environment**:
> - Backend Staging: `https://api-staging.khaidz.com` (WSL + Cloudflare Tunnel)
> - Staging Loopback: `http://localhost:5088` (Internal staging port on host)
> - Frontend Staging: `http://localhost:5173` (or deployed staging host)
> - Database: Live shared `AI_PMS` container (Read/Write verified; **Non-destructive test policy strictly enforced — NO reset / NO seed**)  
> **Audience**: Khải (QA / Solution Lead), Senior Backend Engineer, DevOps Engineer  
> **Baseline Commit**: `fix/complete-be07-staging-v5-partials` @ `95f7e46` (Base: `review/be-error-codes-and-audit` @ `971c1c5`)

---

## 1. Security & Credentials Notice

> [!CAUTION]
> **Zero Plaintext Password Policy**: In compliance with security standards, all plaintext passwords have been removed from this repository. Do NOT commit production or staging credentials to Git.

### Credential Retrieval Procedure
- Test accounts listed below must authenticate using the authorized staging password stored securely in the DevOps/Team Secret Vault or WSL private config (`~/.config/aipms/appsettings.Staging.json`).
- In test runners and documentation, passwords are represented by `<STAGING_TEST_PASSWORD>`.
- **Security Recommendation**: If default passwords (e.g. initial demo seed values) were ever used in shared staging environments, DevOps/QA must immediately rotate them via administrative hash update prior to broad team access.

### Official Staging Actor Accounts

| Role | Staging Username / Email | Password Placeholder | Department & Scoped Role |
| :--- | :--- | :--- | :--- |
| **System Admin** | `admin@aipms.test` | `<STAGING_TEST_PASSWORD>` | Platform Governance, Academic Semesters & Global Settings |
| **Department Staff (SE/IT)** | `staff.it@aipms.test` | `<STAGING_TEST_PASSWORD>` | Software Engineering / IT Proposals, Rubrics & Review Gates |
| **Department Staff (Business)** | `staff.business@aipms.test` | `<STAGING_TEST_PASSWORD>` | Business Administration / Interdisciplinary Approvals |
| **Lecturer (Primary Supervisor)**| `lecturer1@aipms.test` | `<STAGING_TEST_PASSWORD>` | Primary Project Supervision, Weekly Reviews, Defense Committee |
| **Lecturer (Discipline Mentor)** | `lecturer2@aipms.test` | `<STAGING_TEST_PASSWORD>` | Cross-discipline Mentorship, Specialized Evidence Evaluation |
| **Student (Team Leader)** | `student.leader@aipms.test`| `<STAGING_TEST_PASSWORD>` | Capstone Team Leader, Topic Proposal, Deliverable Submissions |
| **Student (Team Member)** | `student.member@aipms.test`| `<STAGING_TEST_PASSWORD>` | Team Member, Working Agreement Sign-off, Peer Evaluation |

---

## 2. Pre-Flight Staging Healthcheck

Before executing manual smoke tests, verify that the staging instance is online and running the intended commit:

```bash
curl -s https://api-staging.khaidz.com/api/v1/system | jq .
```

Expected response structure:
```json
{
  "name": "AI-PMS API",
  "version": "v1",
  "status": "ok",
  "commitSha": "<TARGET_DEPLOYED_COMMIT_SHA>"
}
```

---

## 3. The 14 Official v5.0 Modules E2E Test Suite (UC-135 – UC-148)

### Module 1: Review Gates (UC-135)
- **Controller & API**: `ReviewGatesController.cs` | `GET/POST /api/v1/projects/{id}/review-gates`
- **FE Feature Flag**: `VITE_ENABLE_GATES`
- **Actor**: Department Staff (`staff.it@aipms.test`)
- **Frontend Screen**: Project Workspace > Academic Review Gates Tab
- **Step-by-Step UI Actions**:
  1. Login as Department Staff.
  2. Open project detail page and navigate to **Review Gates**.
  3. Inspect the gate checklist stages (G1 to G6).
  4. Update status for Gate 1 (e.g. change to `PASSED` with reviewer comments).
- **Expected Result**: Gate status updates to `PASSED`; audit log recorded.
- **Negative Test**: Login as Student Leader (`student.leader@aipms.test`) and attempt `POST /api/v1/projects/{id}/review-gates`. Expected: HTTP 403 Forbidden.
- **Status**: `AUTOMATED_TEST_PASS` (`ReviewGateTests.cs`) | `E2E_ACCEPTANCE_PENDING` (Staging smoke)

---

### Module 2: Chat & Attachments (UC-136)
- **Controller, Hub & API**: `ChatController.cs`, `ChatHub.cs` | `GET/POST /api/v1/projects/{id}/chat`, SignalR `/hubs/chat`
- **FE Feature Flag**: `VITE_ENABLE_CHAT`, `VITE_ENABLE_CHAT_ATTACHMENTS`
- **Actor**: Student Leader (`student.leader@aipms.test`) & Member (`student.member@aipms.test`)
- **Frontend Screen**: Project Workspace > Team Chat Window
- **Step-by-Step UI Actions**:
  1. Open two browser sessions (Leader & Member).
  2. Send a text message with a file attachment (.png / .pdf) in the project channel.
  3. Verify the message and attachment appear instantly in the second browser session via SignalR.
- **Expected Result**: Message delivered via WebSocket event `ReceiveMessage`; attachment downloadable; message persisted in SQL.
- **Negative Test**: Unassigned student from another project attempts to join `/hubs/chat` channel. Expected: Unauthorized / room subscription denied.
- **Status**: `AUTOMATED_TEST_PASS` (`ChatApiTests.cs`, `ChatRealtimeTests.cs`) | `E2E_ACCEPTANCE_PENDING` (Staging smoke)

---

### Module 3: Appeals & Result Corrections (UC-137)
- **Controller & API**: `ResultAppealsController.cs` | `GET/POST /api/v1/projects/{id}/appeals`, `/corrections`
- **FE Feature Flag**: `VITE_ENABLE_RESULT_CORRECTION`
- **Actor**: Student Leader (`student.leader@aipms.test`) & Department Staff (`staff.it@aipms.test`)
- **Frontend Screen**: Student Grade View > Request Appeal Modal & Staff Appeals Queue
- **Step-by-Step UI Actions**:
  1. Student navigates to finalized milestone/defense grade.
  2. Submits an appeal request with rationale and supporting document.
  3. Department Staff opens Appeals Queue, reviews claim, and submits decision.
- **Expected Result**: Appeal transitions `SUBMITTED` -> `UNDER_REVIEW` -> `RESOLVED`; grade correction requires two-person approval.
- **Negative Test**: Student submits appeal after appeal window has expired. Expected: HTTP 409 Conflict (`APPEAL_WINDOW_CLOSED`).
- **Status**: `AUTOMATED_TEST_PASS` (`ResultAppealEndpointTests.cs`, `ResultRevisionEndpointTests.cs`) | `E2E_ACCEPTANCE_PENDING` (Staging smoke)

---

### Module 4: Industry Advisory (UC-138)
- **Controller & API**: `IndustryController.cs` | `GET/POST /api/v1/projects/{id}/industry-advisors`
- **FE Feature Flag**: Controlled via Mentors UI
- **Actor**: Lecturer / Supervisor (`lecturer1@aipms.test`)
- **Frontend Screen**: Project Settings > External Advisors Management
- **Step-by-Step UI Actions**:
  1. Lecturer opens project supervision panel.
  2. Invites an external industry advisor with email, organization, and advisory role.
  3. Sets advisory scope permissions.
- **Expected Result**: Advisor record created; advisor receives advisory notification; advisory feedback is marked as advisory (non-binding on academic grade).
- **Negative Test**: Student leader attempts to invite or remove an industry advisor. Expected: HTTP 403 Forbidden.
- **Status**: `AUTOMATED_TEST_PASS` (`IndustryEndpointTests.cs`) | `E2E_ACCEPTANCE_PENDING` (Staging smoke)

---

### Module 5: Weekly Assessments (UC-139)
- **Controller & API**: `WeeklyAssessmentsController.cs` | `GET/POST /api/v1/projects/{id}/weekly-assessments`
- **FE Feature Flag**: `VITE_ENABLE_WEEKLY_ASSESSMENT`
- **Actor**: Lecturer / Supervisor (`lecturer1@aipms.test`)
- **Frontend Screen**: Project Workspace > Weekly Assessments Tab
- **Step-by-Step UI Actions**:
  1. Lecturer selects an active milestone week (Week 1–12).
  2. Scores team and individual members across criteria (progress, attendance, sprint completion).
  3. Submits weekly evaluation.
- **Expected Result**: Weekly assessment saved; individual scores displayed in student progress analytics; does not alter final course grade.
- **Negative Test**: Lecturer submits weekly assessment for a week outside project execution bounds. Expected: HTTP 400/409 Validation failure.
- **Status**: `AUTOMATED_TEST_PASS` (`WeeklyAssessmentEndpointTests.cs`, `WeeklyAssessmentWeeksTests.cs` — 13 tests PASS) | `E2E_ACCEPTANCE_PENDING` (Staging smoke)

---

### Module 6: Peer Evaluation (UC-140)
- **Controller & API**: `PeerEvaluationsController.cs` | `GET/POST /api/v1/projects/{id}/peer-evaluations`
- **FE Feature Flag**: `VITE_ENABLE_PEER_EVAL`
- **Actor**: Student Member (`student.member@aipms.test`) & Leader (`student.leader@aipms.test`)
- **Frontend Screen**: Project Workspace > Peer Evaluation Portal
- **Step-by-Step UI Actions**:
  1. Student opens peer evaluation form during active checkpoint window.
  2. Rates team members' contributions, collaboration, and delivers qualitative feedback.
  3. Submits confidential evaluation.
- **Expected Result**: Submission confirmed; ratings anonymized in aggregate team reports; submitted state locked against re-submission.
- **Negative Test**: Student attempts to evaluate themselves or evaluate members of another team. Expected: HTTP 400/403 Error.
- **Status**: `AUTOMATED_TEST_PASS` (`PeerEvaluationEndpointTests.cs`) | `E2E_ACCEPTANCE_PENDING` (Staging smoke)

---

### Module 7: Working Agreement (UC-141)
- **Controller & API**: `WorkingAgreementsController.cs` | `GET/POST /api/v1/projects/{id}/working-agreements`
- **FE Feature Flag**: `VITE_ENABLE_WORKING_AGREEMENT`
- **Actor**: Student Leader (`student.leader@aipms.test`) & Members
- **Frontend Screen**: Project Workspace > Working Agreement Charter
- **Step-by-Step UI Actions**:
  1. Leader creates draft team agreement (ground rules, communications, response times).
  2. Leader publishes agreement for team review.
  3. Every roster member clicks **Consent / Agree**.
- **Expected Result**: Status updates to `READY`; consensus achieved.
- **Negative Test**: Supervisor attempts to accept supervision on project where policy requires working agreement but consensus is incomplete. Expected: HTTP 422 `WORKING_AGREEMENT_INCOMPLETE`.
- **Status**: `AUTOMATED_TEST_PASS` (`WorkingAgreementEndpointTests.cs`, `WorkingAgreementActivationTests.cs`) | `E2E_ACCEPTANCE_PENDING` (Staging smoke)

---

### Module 8: COLD Reports & Evaluation (UC-142)
- **Controller & API**: `ColdSubmissionsController.cs`, `ColdEvaluationsController.cs` | `GET/POST /api/v1/projects/{id}/cold-submissions`
- **FE Feature Flag**: `VITE_ENABLE_COLD_REPORT`
- **Actor**: Student Team & Department Evaluator (`lecturer1@aipms.test`)
- **Frontend Screen**: Final Submissions Portal & COLD Grading Console
- **Step-by-Step UI Actions**:
  1. Student uploads COLD capstone report archive (.zip / .pdf).
  2. Finalizes package submission before deadline.
  3. Evaluator accesses COLD submission package and grades against department rubric.
- **Expected Result**: Package locked upon submission; grade calculated with 2 decimal precision; evaluator feedback persisted.
- **Negative Test (D17 Invariant)**: Evaluator attempts to evaluate a COLD report of a project where they are Primary Supervisor. Expected: HTTP 403 / 409 Conflict (`EVALUATOR_ROLE_CONFLICT`).
- **Status**: `AUTOMATED_TEST_PASS` (`ColdSubmissionEndpointTests.cs`, `ColdEvaluationEndpointTests.cs` — 12 tests PASS) | `E2E_ACCEPTANCE_PENDING` (Staging smoke)

---

### Module 9: Defense & Sessions (UC-143)
- **Controller & API**: `EvaluationDraftsController.cs` | `GET/POST /api/v1/projects/{id}/defense-sessions`
- **FE Feature Flag**: `VITE_ENABLE_DEFENSE`
- **Actor**: Department Staff (`staff.it@aipms.test`) & Committee Members
- **Frontend Screen**: Department Portal > Defense Committee & Schedule Matrix
- **Step-by-Step UI Actions**:
  1. Staff assigns defense session (room, date/time, committee chair, secretary, reviewers).
  2. Committee members access defense scoring sheet during oral defense.
  3. Enters rubric criteria scores and submits committee score.
- **Expected Result**: Committee schedule published; scores weighted and aggregated automatically.
- **Negative Test**: Committee member submits scores outside active defense session window. Expected: HTTP 409 Conflict.
- **Status**: `AUTOMATED_TEST_PASS` (`HierarchicalEvaluationTests.cs`, `InterdisciplinaryEvaluationTests.cs`) | `E2E_ACCEPTANCE_PENDING` (Staging smoke)

---

### Module 10: Sprints & Backlog Board (UC-144)
- **Controller & API**: `SprintsController.cs` | `GET/POST /api/v1/projects/{id}/sprints`
- **FE Feature Flag**: `VITE_ENABLE_JIRA_SPRINT`
- **Actor**: Student Leader & Members
- **Frontend Screen**: Project Workspace > Sprints / Kanban Board
- **Step-by-Step UI Actions**:
  1. Leader creates Sprint 1 with start date, end date, and sprint goal.
  2. Creates backlog tasks with story points and assigns members.
  3. Moves tasks across Kanban board: `TODO` -> `IN_PROGRESS` -> `DONE`.
- **Expected Result**: Task state updates in real-time with optimistic concurrency token; sprint burndown updates.
- **Negative Test**: Non-member student attempts to mutate tasks or sprint configuration. Expected: HTTP 403 Forbidden.
- **Status**: `AUTOMATED_TEST_PASS` (`SprintEndpointTests.cs`) | `E2E_ACCEPTANCE_PENDING` (Staging smoke)

---

### Module 11: Meeting Collaboration (UC-145)
- **Controller & API**: `MeetingCollaborationController.cs` | `GET/PUT /api/v1/meetings/{id}/collaboration`
- **FE Feature Flag**: `VITE_ENABLE_MEETING_COLLABORATION`
- **Actor**: Student Leader (`student.leader@aipms.test`) & Supervisor (`lecturer1@aipms.test`)
- **Frontend Screen**: Project Workspace > Mentoring Meetings > Meeting Detail
- **Step-by-Step UI Actions**:
  1. Leader schedules meeting and joins LiveKit video room.
  2. Participants edit agenda and collaborative meeting notes.
  3. Notes lock is acquired while editing to prevent concurrent overwrites.
- **Expected Result**: Notes saved cleanly; lock released upon blur/finish; attendees RSVP tracked.
- **Negative Test**: User attempts to overwrite notes held by another active lock session. Expected: HTTP 409 Conflict (`NOTE_LOCK_ACQUIRED_BY_OTHER`).
- **Status**: `AUTOMATED_TEST_PASS` (`MeetingCollaborationEndpointTests.cs`) | `E2E_ACCEPTANCE_PENDING` (Staging smoke)

---

### Module 12: Unified Capabilities (UC-146)
- **Controller & API**: `ExecutionCapabilitiesController.cs` | `GET /api/v1/capabilities/projects/{id}`
- **FE Feature Flag**: `VITE_ENABLE_UNIFIED_CAPABILITIES`
- **Actor**: Any Authenticated Project Stakeholder
- **Frontend Screen**: Project Workspace > Navigation Bar (Feature-gating pills)
- **Step-by-Step UI Actions**:
  1. Navigate to project dashboard.
  2. Frontend queries `/capabilities/projects/{id}` to fetch active project capabilities.
  3. Verify UI buttons (Submit, Request Revision, Review, Chat) enable or disable according to returned JSON.
- **Expected Result**: Returned capabilities map matches project state, user role, and period policy flags exactly.
- **Negative Test**: Unauthenticated query to capabilities endpoint. Expected: HTTP 401 Unauthorized.
- **Status**: `AUTOMATED_TEST_PASS` (`V5AcademicCapabilityTests.cs`, `CapabilityEnforcementParityTests.cs`) | `E2E_ACCEPTANCE_PENDING` (Staging smoke)

---

### Module 13: Milestone Templates (UC-147)
- **Controller & API**: `MilestoneTemplatesController.cs` | `GET/POST /api/v1/academic/milestone-templates`
- **FE Feature Flag**: Controlled via Academic Admin
- **Actor**: System Admin (`admin@aipms.test`) / Department Staff (`staff.it@aipms.test`)
- **Frontend Screen**: Academic Administration > Milestone Template Designer
- **Step-by-Step UI Actions**:
  1. Admin creates milestone template with default offset days (e.g. Inception, Checkpoint 1, Final Defense).
  2. Publishes and locks template version.
  3. Links template to upcoming Project Period.
- **Expected Result**: When projects in this period activate, milestone instances are cloned automatically from this template.
- **Negative Test**: Attempting to edit or delete a published template version that has active project bindings. Expected: HTTP 409 Conflict (`TEMPLATE_VERSION_LOCKED`).
- **Status**: `AUTOMATED_TEST_PASS` (`MilestoneTemplateActivationSqlTests.cs`) | `E2E_ACCEPTANCE_PENDING` (Staging smoke)

---

### Module 14: Academic AI Assistant (UC-148)
- **Controller & API**: `AiAssistantController.cs` | `GET/POST /api/v1/ai/assistant/query`, `/runs`
- **FE Feature Flag**: `VITE_ENABLE_AI_RUN_HISTORY`
- **Actor**: Student Leader (`student.leader@aipms.test`) & Supervisor (`lecturer1@aipms.test`)
- **Frontend Screen**: Copilot Drawer > Academic Q&A Tab
- **Step-by-Step UI Actions**:
  1. Click AI Assistant drawer icon.
  2. Submit query regarding capstone guidelines (e.g. "What are the formatting rules for the final thesis?").
  3. Inspect streamed response and source citations.
- **Expected Result**: Grounded answer generated from active department policy corpus; includes official disclaimer; conversation run logged in run history.
- **Negative Test**: Querying student asks for unauthorized exam keys or cross-department confidential rubrics. Expected: Fallback refusal with scope boundary notice.
- **Status**: `AUTOMATED_TEST_PASS` (`AcademicKnowledgeTests.cs`, `AiAssistantEndpointTests.cs`) | `E2E_ACCEPTANCE_PENDING` (Staging smoke)

---

## 4. Supplementary Governance E2E Test Sections

> [!NOTE]
> The following two workflows are supplementary governance capabilities tested alongside the core v5 platform. They do not replace any of the 14 official v5 modules above.

### Supplementary Section A: Supervisor Workflow & D18 Pre-Approval (BE-07)
- **Controller & API**: `SupervisorRequestsController.cs`, `ProjectController.cs` | `POST /api/v1/projects/{id}/supervisor-requests`, `/accept`, `/approve`
- **Actors**: Student Leader (`student.leader@aipms.test`), Lecturer (`lecturer1@aipms.test`), Department Staff (`staff.it@aipms.test`)
- **Workflow Steps**:
  1. Leader invites lecturer while project is in `DRAFT`.
  2. Lecturer accepts: Project **remains in DRAFT**; lecturer gains scoped read; no execution milestones created.
  3. Team submits proposal -> Department Staff approves.
  4. Project automatically activates (`ACTIVE`), milestones generated atomically.
- **Negative Test**: Verify that lecturer accepting in DRAFT does not activate project, and deliverables cannot be submitted before Department approval.
- **Status**: `AUTOMATED_TEST_PASS` (77 integration tests in `SupervisorRequestEndpointTests` & `PreconfirmedApprovalTests`) | `E2E_ACCEPTANCE_PENDING`.

### Supplementary Section B: Student Qualification Certificate Governance
- **Controller & API**: `StudentQualificationsController.cs` | `POST /api/v1/student-qualifications/me/certificate`, `/verify`, `/reject`
- **Actors**: Student (`student.leader@aipms.test`), Department Staff (`staff.it@aipms.test`)
- **Workflow Steps**:
  1. Student uploads certificate (.pdf <= 20 MiB). Status becomes `PENDING_VERIFICATION`.
  2. Staff opens Verification Queue, inspects file, and clicks **Verify**.
  3. Student profile refreshes: displays **Đã xác minh** (`VERIFIED`).
- **Negative Test**: Staff from Department A attempts to verify a student from Department B. Expected: HTTP 403 Forbidden (`DEPARTMENT_SCOPE_MISMATCH`).
- **Status**: `AUTOMATED_TEST_PASS` (`StudentQualificationGovernanceEndpointTests.cs`) | `E2E_ACCEPTANCE_PENDING`.

---

## 5. Summary Matrix & Acceptance Sign-off

| # | Official v5 Module | UC Code | Primary Controller | Automated Tests | Staging E2E Smoke |
| :--- | :--- | :--- | :--- | :--- | :--- |
| 1 | Review Gates | UC-135 | `ReviewGatesController` | **PASS** | `E2E_ACCEPTANCE_PENDING` |
| 2 | Chat & Attachments | UC-136 | `ChatController`, `ChatHub` | **PASS** | `E2E_ACCEPTANCE_PENDING` |
| 3 | Appeals & Result Corrections | UC-137 | `ResultAppealsController` | **PASS** | `E2E_ACCEPTANCE_PENDING` |
| 4 | Industry Advisory | UC-138 | `IndustryController` | **PASS** | `E2E_ACCEPTANCE_PENDING` |
| 5 | Weekly Assessments | UC-139 | `WeeklyAssessmentsController`| **PASS** | `E2E_ACCEPTANCE_PENDING` |
| 6 | Peer Evaluation | UC-140 | `PeerEvaluationsController` | **PASS** | `E2E_ACCEPTANCE_PENDING` |
| 7 | Working Agreement | UC-141 | `WorkingAgreementsController`| **PASS** | `E2E_ACCEPTANCE_PENDING` |
| 8 | COLD Reports & Evaluation | UC-142 | `ColdSubmissionsController` | **PASS** | `E2E_ACCEPTANCE_PENDING` |
| 9 | Defense & Sessions | UC-143 | `EvaluationDraftsController` | **PASS** | `E2E_ACCEPTANCE_PENDING` |
| 10 | Sprints & Backlog Board | UC-144 | `SprintsController` | **PASS** | `E2E_ACCEPTANCE_PENDING` |
| 11 | Meeting Collaboration | UC-145 | `MeetingCollaborationController`| **PASS** | `E2E_ACCEPTANCE_PENDING` |
| 12 | Unified Capabilities | UC-146 | `ExecutionCapabilitiesController`| **PASS** | `E2E_ACCEPTANCE_PENDING` |
| 13 | Milestone Templates | UC-147 | `MilestoneTemplatesController`| **PASS** | `E2E_ACCEPTANCE_PENDING` |
| 14 | Academic AI Assistant | UC-148 | `AiAssistantController` | **PASS** | `E2E_ACCEPTANCE_PENDING` |
| + | *Supp. A: Supervisor D18* | BE-07 | `SupervisorRequestsController`| **PASS** | `E2E_ACCEPTANCE_PENDING` |
| + | *Supp. B: Student Qualifications*| Policy | `StudentQualificationsController`| **PASS** | `E2E_ACCEPTANCE_PENDING` |

**Verification Sign-off Recommendation**:
1. All 14 v5 modules and 2 supplementary governance flows are confirmed fully covered by backend controllers, domain rules, and passing automated test suites.
2. Full status transition from `E2E_ACCEPTANCE_PENDING` to **DONE** will occur upon execution of the UI smoke checks above on `https://api-staging.khaidz.com`.
