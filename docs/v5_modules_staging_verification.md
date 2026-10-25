# Verification and Rollout Matrix: 14 v5.0 Modules on Staging & Production

> Document generated for **BE-18** as part of *Checklist đưa dự án về trạng thái Complete — 24/10/2026*.
> Cross-referenced with Backend Controllers, Frontend Feature Flags, and Automated Test Suites.

---

## 1. Executive Summary

The 14 v5.0 modules expand the core governance lifecycle of AI-PMS into rich collaborative, advisory, and real-time workflows. All 14 modules have fully implemented backend endpoints with domain and authorization rules enforced (fail-closed model).

To ensure complete contract parity between Backend and Frontend without runtime 404/409 regressions:
- **Backend**: Core API controllers are always enabled and secured via ASP.NET Core Policies. Background worker services (`ScheduledNotifications`, `MeetingReminders`, `RiskAlert`) are configurable via `appsettings.json`.
- **Frontend**: Feature flags (`VITE_ENABLE_*` in `.env` / `.env.example`) control UI route visibility and navigation items.

---

## 2. The 14 v5 Modules Matrix

| # | Module Name | Backend Namespace / Controller | Primary API Endpoints | FE Flag (`VITE_ENABLE_*`) | Staging State | Automated Test Suite | UC Code |
|---|-------------|--------------------------------|----------------------|---------------------------|---------------|----------------------|---------|
| 1 | **Review Gates** | `Controllers/ReviewGatesController.cs` | `GET/POST /api/v1/projects/{id}/review-gates` | `VITE_ENABLE_GATES` | Fully Ready | `ReviewGateTests.cs` (integration/unit) | `UC-135` |
| 2 | **Chat & Attachments** | `Controllers/ChatController.cs`, `Hubs/ChatHub.cs` | `GET/POST /api/v1/projects/{id}/chat`, SignalR `/hubs/chat` | `VITE_ENABLE_CHAT`<br>`VITE_ENABLE_CHAT_ATTACHMENTS` | Fully Ready | `ChatApiTests.cs`, `ChatRealtimeTests.cs` | `UC-136` |
| 3 | **Appeals & Result Corrections** | `Controllers/ResultAppealsController.cs` | `GET/POST /api/v1/projects/{id}/appeals`, `/corrections` | `VITE_ENABLE_RESULT_CORRECTION` | Fully Ready | `ResultAppealEndpointTests.cs`, `ResultRevisionEndpointTests.cs` | `UC-137` |
| 4 | **Industry Advisory** | `Controllers/IndustryController.cs` | `GET/POST /api/v1/projects/{id}/industry-advisors` | Controlled via Mentors UI | Fully Ready | `IndustryEndpointTests.cs` | `UC-138` |
| 5 | **Weekly Assessments** | `Controllers/WeeklyAssessmentsController.cs` | `GET/POST /api/v1/projects/{id}/weekly-assessments` | `VITE_ENABLE_WEEKLY_ASSESSMENT` | Fully Ready | `WeeklyAssessmentEndpointTests.cs`, `WeeklyAssessmentWeeksTests.cs` | `UC-139` |
| 6 | **Peer Evaluation** | `Controllers/PeerEvaluationsController.cs` | `GET/POST /api/v1/projects/{id}/peer-evaluations` | `VITE_ENABLE_PEER_EVAL` | Fully Ready | `PeerEvaluationEndpointTests.cs` | `UC-140` |
| 7 | **Working Agreement** | `Controllers/WorkingAgreementsController.cs` | `GET/POST /api/v1/projects/{id}/working-agreements` | `VITE_ENABLE_WORKING_AGREEMENT` | Fully Ready | `WorkingAgreementEndpointTests.cs`, `WorkingAgreementActivationTests.cs` | `UC-141` |
| 8 | **COLD Reports & Evaluation** | `Controllers/ColdSubmissionsController.cs` | `GET/POST /api/v1/projects/{id}/cold-submissions` | `VITE_ENABLE_COLD_REPORT` | Fully Ready | `ColdSubmissionEndpointTests.cs`, `ColdEvaluationEndpointTests.cs` | `UC-142` |
| 9 | **Defense & Sessions** | `Controllers/EvaluationDraftsController.cs` | `GET/POST /api/v1/projects/{id}/defense-sessions` | `VITE_ENABLE_DEFENSE` | Fully Ready | `HierarchicalEvaluationTests.cs`, `InterdisciplinaryEvaluationTests.cs` | `UC-143` |
| 10 | **Sprints & Backlog Board** | `Controllers/SprintsController.cs` | `GET/POST /api/v1/projects/{id}/sprints` | `VITE_ENABLE_JIRA_SPRINT` | Fully Ready | `SprintEndpointTests.cs` | `UC-144` |
| 11 | **Meeting Collaboration** | `Controllers/MeetingCollaborationController.cs` | `GET/PUT /api/v1/meetings/{id}/collaboration` | `VITE_ENABLE_MEETING_COLLABORATION` | Fully Ready | `MeetingCollaborationEndpointTests.cs` | `UC-145` |
| 12 | **Unified Capabilities** | `Controllers/ExecutionCapabilitiesController.cs` | `GET /api/v1/capabilities/projects/{id}` | `VITE_ENABLE_UNIFIED_CAPABILITIES` | Fully Ready | `V5AcademicCapabilityTests.cs`, `CapabilityEnforcementParityTests.cs` | `UC-146` |
| 13 | **Milestone Templates** | `Controllers/MilestoneTemplatesController.cs` | `GET/POST /api/v1/academic/milestone-templates` | Controlled via Academic Admin | Fully Ready | `MilestoneTemplateActivationSqlTests.cs` | `UC-147` |
| 14 | **Academic AI Assistant** | `Controllers/AiAssistantController.cs` | `GET/POST /api/v1/ai/assistant/query`, `/runs` | `VITE_ENABLE_AI_RUN_HISTORY` | Fully Ready | `AcademicKnowledgeTests.cs`, `AiAssistantEndpointTests.cs` | `UC-148` |

---

## 3. Smoke Test Verification Guidelines for Staging

1. **Review Gates (UC-135)**:
   - Call `GET /api/v1/projects/{id}/review-gates` as Department Staff. Verify stage items (G1–G6) return current review status.
   - Verify non-staff member receives 403 Forbidden on update mutations.
2. **Realtime Chat (UC-136)**:
   - Connect SignalR client to `/hubs/chat` using valid Bearer token.
   - Verify room join and message broadcast to assigned project members.
3. **Appeals & Revisions (UC-137)**:
   - Verify student can create appeal request within the open appeal window.
   - Verify scoped staff can review and two-person approval is enforced for grade corrections.
4. **Industry Advisory (UC-138)**:
   - Verify primary supervisor can invite industry advisor and manage review permissions.
5. **Weekly Assessments (UC-139)**:
   - Verify primary supervisor can submit weekly individual demo scores.
   - Verify supervisor cannot submit scores for weeks outside the project period dates.
6. **Peer Evaluation (UC-140)**:
   - Verify students can submit peer review evidence for team members before checkpoint deadlines.
7. **Working Agreement (UC-141)**:
   - Verify team members can collaboratively draft, review, and sign working agreements.
8. **COLD Reports (UC-142)**:
   - Verify students can download COLD template, draft checklist items, and submit reports with file attachments.
9. **Defense Management (UC-143)**:
   - Verify committee members can access defense schedules and enter evaluation rubrics.
10. **Sprints & Backlog (UC-144)**:
    - Verify creating sprints, moving tasks across Kanban columns, and updating story points.
11. **Meeting Collaboration (UC-145)**:
    - Verify real-time agenda editing and note locking to prevent concurrent overwrite.
12. **Unified Capabilities (UC-146)**:
    - Verify capability map matches enforcement rules (`CapabilityEnforcementParityTests`).
13. **Milestone Templates (UC-147)**:
    - Verify department staff can activate milestone templates for new project periods.
14. **Academic AI Assistant (UC-148)**:
    - Query academic policy terms and verify responses are grounded with citations from active department policies.
