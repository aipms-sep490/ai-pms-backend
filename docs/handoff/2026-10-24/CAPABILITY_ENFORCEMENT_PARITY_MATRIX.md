# BẢNG ĐỐI CHIẾU CAPABILITY ↔ ENFORCEMENT (BE-23)

Tài liệu này là hợp đồng kiến trúc chốt chặn giữa hai cơ chế thẩm quyền trong Backend:
1. **Capability (`IWorkflowContextReader`, `IExecutionCapabilityService`)**: Trả quyền và lý do (`allowed: bool`, `reasons: string[]`) cho Frontend render giao diện.
2. **Enforcement (Command Handlers, Domain Workflows)**: Thẩm định thực tế khi ghi dữ liệu; từ chối bằng exception (`403 Forbidden`, `409 Conflict`, `400 Bad Request`).

---

## 1. Ma trận Đối chiếu Toàn Diện

| Hành động (`Action Code`) | Endpoint ghi tương ứng | Nguồn luật Capability | Nguồn luật Enforcement | Tình trạng |
|---|---|---|---|---|
| `submit_project` | `POST /projects/{id}/submit` | `TopicRules.ProposalCompletenessIssues` + `projects.GetSupervisorConfirmationAsync` + `ProjectStateMachine.CanTransition` | `SubmitProjectCommand` gọi cùng `TopicRules` + `GetSupervisorConfirmationAsync` + `ProjectStateMachine` | ✅ **DÙNG CHUNG** |
| `resubmit_project` | `POST /projects/{id}/resubmit` | `ProjectStateMachine` (Status = `REVISION_REQUIRED`) + `GetSupervisorConfirmationAsync` | `ResubmitProjectCommand` gọi cùng quy tắc | ✅ **DÙNG CHUNG** |
| `send_supervisor_request` (PRIMARY) | `POST /projects/{id}/supervisor-requests` (type: PRIMARY) | `SupervisorSelectionEligibility.CanRequestPrimary` | `SupervisorRequestWorkflow.RequireEligibilityAsync` gọi `SupervisorSelectionEligibility.CanRequestPrimary` | ✅ **DÙNG CHUNG** (đã thống nhất ở BE-01) |
| `send_supervisor_request` (MENTOR) | `POST /projects/{id}/supervisor-requests` (type: DISCIPLINE_MENTOR) | `SupervisorSelectionEligibility.CanRequestMentor` | `SupervisorRequestWorkflow.RequireEligibilityAsync` gọi `SupervisorSelectionEligibility.CanRequestMentor` | ✅ **DÙNG CHUNG** (đã thống nhất ở BE-01) |
| `create_progress_report` | `POST /projects/{id}/progress-reports` | `tasks.IsUserActiveTeamMemberAsync` + `Status == "ACTIVE"` | `CreateProgressReportCommand`: `tasks.IsUserActiveTeamMemberAsync` + `Status == "ACTIVE"` | ✅ **DÙNG CHUNG** (đã thống nhất ở BE-06) |
| `submit_progress_report` | `POST /projects/{id}/progress-reports/{reportId}/submit` | `tasks.IsProjectLeaderOrSupervisorAsync` + `Status == "ACTIVE"` | `SubmitProgressReportCommand`: chỉ Leader nhóm được submit | ✅ **DÙNG CHUNG** (Leader submission only) |
| `publish_project_result` | `POST /projects/{id}/results/publish` | Department Staff / Admin + Project có Finalized Evaluation | `PublishOfficialResultCommand`: Staff/Admin + Finalized Evaluation | ✅ **DÙNG CHUNG** |
| `create_task` | `POST /milestones/{id}/tasks` | `tasks.IsProjectLeaderOrSupervisorAsync` + `Status == "ACTIVE"` | `CreateTaskCommand`: `tasks.IsProjectLeaderOrSupervisorAsync` + `Status == "ACTIVE"` | ✅ **DÙNG CHUNG** |
| `assign_task` | `PUT /tasks/{id}/assignees` | `tasks.IsProjectLeaderOrSupervisorAsync` + `Status == "ACTIVE"` | `AssignTaskCommand`: `tasks.IsProjectLeaderOrSupervisorAsync` + `Status == "ACTIVE"` | ✅ **DÙNG CHUNG** |
| `change_task_status` | `PUT /tasks/{id}/status` | Task Writer (Manager / Assignee / Mentor) + `Status == "ACTIVE"` | `UpdateTaskStatusCommand`: Writer + Active Project | ✅ **DÙNG CHUNG** |

---

## 2. Nguyên tắc Bất biến (Architecture Invariants)

1. **Zero Logic Duplication**: Mọi điều kiện trạng thái và quyền hạn chuyển sang Domain Policy thuần (`AIPMS.Domain.*`).
2. **Fail-Closed Consistency**: Capability nói `false` với lý do `R`, Enforcement **bắt buộc** từ chối nếu request được gửi đến. Capability nói `true`, Enforcement **không được phép** chặn vì lý do trạng thái / vai trò tương ứng.
3. **Fixture Honesty**: Không bao giờ viết integration fixture xóa sạch trạng thái để ép test xanh; mọi kịch bản phải mô phỏng vòng đời dự án thực tế.
