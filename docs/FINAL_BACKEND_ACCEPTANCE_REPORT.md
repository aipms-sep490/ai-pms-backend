# AI-PMS — FINAL BACKEND ACCEPTANCE & CLOSEOUT REPORT
## Báo cáo Chốt Nghiệm thu Kỹ thuật Chuyên sâu Backend (PR #139)

> **Dự án**: AI-PMS (Academic Project Management System)  
> **Phạm vi phân công**: **CHUYÊN TRÁCH BACKEND ONLY** (Frontend do team riêng phụ trách)  
> **Backend Repository**: `aipms-sep490/ai-pms-backend`  
> **Review Branch**: `review/aipms-final-integration-acceptance`  
> **PR Đang Mở**: PR #139 (Consolidated duy nhất thay thế PR #136 & PR #138, Base: `develop`)  
> **Target Source Commit**: `3f71a30f1f03027be203025e17b2a6908d0ef84b`  
> **Thời điểm hoàn tất**: 29/10/2026 (22:45 GMT+7)  
> **Vai trò thực hiện**: Senior .NET Backend Engineer, Backend Architect & Backend QA Lead  

---

## A. BẢNG TRẠNG THÁI 14 MODULE BACKEND (BACKEND MODULE STATUS)

Toàn bộ **14/14 module học vụ v5** (UC-135 đến UC-148) đã hoàn thiện đầy đủ trên tầng Backend và được xác thực qua bộ kiểm thử tích hợp chuyên biệt (**ASP.NET Core WebApplicationFactory / Kestrel API thật + EF Core DbContext + SQL Server Database cô lập**):

| UC | Tên Module v5 | Trạng thái API Implementation | BE Integration Tests | Database Verification | Trạng thái Nghiệm thu BE |
| :---: | :--- | :--- | :--- | :--- | :---: |
| **UC-135** | **Review Gates** | `ReviewGatesController.cs`, `ReviewGateService.cs` | `ReviewGateServiceTests.cs` (77 tests) | `dbo.project_gate_instances`, `review_gate_evidence`, `review_gate_assignments` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-136** | **Chat & Attachments** | `ChatController.cs`, `ChatHub.cs`, `ChatService.cs` | `ChatApiTests.cs` (2 tests), `ChatCollaborationTests.cs`, `ChatRealtimeTests.cs` | `dbo.chat_messages`, `chat_conversations`, `chat_attachments`, `chat_outbox` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-137** | **Appeals & Corrections** | `ResultAppealsController.cs`, `ResultAppealService.cs` | `PolicyEvaluationEndpointTests.cs` (30 tests) | `dbo.result_appeal_policies`, `result_appeal_windows`, `result_appeal_requests` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-138** | **Industry Advisory** | `IndustryAdvisoryController.cs`, `IndustryService.cs` | `IndustryAdvisoryTests.cs` (24 tests) | `dbo.industry_experts`, `industry_assignments`, `industry_feedbacks` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-139** | **Weekly Assessments** | `WeeklyAssessmentsController.cs`, `WeeklyAssessmentService.cs` | `WeeklyAssessmentEndpointTests.cs` (13 tests) | `dbo.weekly_assessments`, `weekly_assessment_scores` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-140** | **Peer Evaluation** | `PeerEvaluationsController.cs`, `PeerEvaluationService.cs` | `PeerEvaluationEndpointTests.cs` | `dbo.peer_evaluation_rounds`, `peer_round_members`, `peer_submissions` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-141** | **Working Agreement** | `WorkingAgreementsController.cs`, `WorkingAgreementService.cs` | `WorkingAgreementActivationTests.cs`, `WorkingAgreementEndpointTests.cs` | `dbo.project_working_agreements`, `agreement_members` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-142** | **COLD Reports & Eval** | `ColdSubmissionsController.cs`, `ColdSubmissionService.cs` | `ColdSubmissionEndpointTests.cs` (21 tests), `ColdEvaluationEndpointTests.cs` | `dbo.cold_report_templates`, `cold_packages`, `cold_checklist_items`, `cold_submissions` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-143** | **Defense & Sessions** | `AcademicTrackingController.cs`, `AcademicTrackingService.Defense.cs` | `AcademicTrackingEndpointTests.cs` (9 tests) | `dbo.defense_committees`, `defense_committee_members`, `defense_sessions` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-144** | **Sprints & Tasks** | `SprintsController.cs`, `SprintService.cs`, `TasksController.cs` | `SprintEndpointTests.cs`, `MilestonesAndTasksEndpointTests.cs` | `dbo.sprints`, `tasks`, `sprint_task_events` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-145** | **Meeting Collaboration** | `MeetingsController.cs`, `MeetingService.cs` | `MeetingCollaborationTests.cs` (28 tests) | `dbo.meetings`, `meeting_participants`, `meeting_notes` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-146** | **Unified Capabilities** | `CapabilitiesController.cs`, `ProjectCapabilityService.cs` | `UnifiedCapabilitiesTests.cs` | Dynamic RBAC Evaluation Engine (`user_roles`, `role_permissions`) | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-147** | **Milestone Templates** | `MilestoneTemplatesController.cs`, `MilestoneTemplateService.cs` | `MilestoneTemplateActivationSqlTests.cs` (21 tests) | `dbo.milestone_templates`, `milestone_template_versions`, `milestone_template_items` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-148** | **Academic AI Assistant** | `AcademicKnowledgeController.cs`, `AcademicKnowledgeService.cs` | `AiAssistantSqlIntegrationTests.cs` (32 tests), `AcademicKnowledgeTests.cs`, `StreamEndpointTests.cs` | `dbo.academic_knowledge_documents`, `assistant_conversations`, `audit_logs` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |

---

## B. CÔNG VIỆC ĐÃ HOÀN THÀNH (WORK COMPLETED)

1. **Khắc phục và chuẩn hóa Script Test Data**:
   - [`scripts/prepare-v5-demo.ps1`](file:///D:/AI_PMS/ai-pms-backend/scripts/prepare-v5-demo.ps1): Thay thế toàn bộ các ID số nguyên bị hardcoded trước đây (`10`, `11`, `12`, `14`, `1`, `2`) bằng các biến SQL động (`@singleLeader`, `@singleMember`, `@leadDept`, `@singleMajor`,...). Điều này loại bỏ hoàn toàn rủi ro sai lệch dữ liệu khi database được khởi tạo với identity seed khác biệt.
   - Thêm khối khởi tạo barem Rubric chuẩn (`E2E-RUBRIC`), hồ sơ chuyên môn của cán bộ phản biện (`evaluator@e2e.invalid`), và chuyên gia doanh nghiệp (`expert.advisor@e2e.invalid`) với điều kiện an toàn `IF @rubric IS NULL` và `IF NOT EXISTS`.
   - Script có guard bắt buộc chỉ cho phép chạy trên database test do chính mình sở hữu (`^AI_PMS_E2E_[a-f0-9]{32}$`), không thể vô tình chạy trên CSDL dùng chung.
2. **Kiểm tra biên dịch Solution (Compilation)**:
   - Toàn bộ solution `AIPMS.sln` đã được build thành công ở cấu hình **Release**:
     `Build succeeded. 0 Warning(s), 0 Error(s). Time Elapsed 00:00:26.09`.
3. **Kết quả Regression Testing thực tế**:
   - `SystemEndpointTests`: **10/10 PASS** (Bao gồm 4 tests độc lập cho cơ chế phân giải `COMMIT_SHA`).
   - `ChatApiTests`: **2/2 PASS**.
   - `PolicyEvaluationEndpointTests` (Appeals UC-137): **30/30 PASS**.
   - `ColdSubmissionEndpointTests` (COLD UC-142 & Bất biến D17): **21/21 PASS**.
   - `AcademicTrackingEndpointTests` (Defense Committees & Sessions UC-143): **9/9 PASS**.
   - `AiAssistantSqlIntegrationTests` & `AcademicKnowledgeTests` (AI Assistant UC-148): **32/32 PASS**.
   - `SupervisorRequestEndpointTests` (BE-07 D18): **78/78 PASS**.
   - Tổng cộng CI: **1.237 Unit Tests + 1.346 Integration Tests = 2.583 Tests SUCCESS**.
4. **Trạng thái Bug Backend**:
   - **Không có backend bug còn mở**. Mọi nghiệp vụ đều thỏa mãn các bất biến domain trong SRS/SDD.

---

## C. CAM KẾT & XÁC MINH COMMIT_SHA

1. **Trạng thái Kỹ thuật**:
   - Backend Code: **`BE COMMIT_SHA IMPLEMENTATION DONE`**.
   - Môi trường chạy thực tế Local: **`LOCAL RUNTIME VERIFIED`** (Được inject và đọc chuẩn xác qua biến môi trường `COMMIT_SHA`).
   - Docker Verification: **`PENDING STAGING DEPLOYMENT`** (Máy trạm Windows không cài Docker engine; Docker daemon chạy trên WSL của Khải).
   - Staging Deployment: **`STAGING DEPLOYMENT PENDING`** (Endpoint staging hiện trả `"1.0.0"` do container đang chạy được build thiếu build-arg `COMMIT_SHA`).
2. **Quy trình Triển khai cho Khải trên WSL (`khaine161`)**:
   Khải chạy các lệnh sau trên terminal WSL:

   ```bash
   # Bước 1: Kéo đúng target commit của PR #139
   cd ~/aipms/ai-pms-backend
   git fetch origin review/aipms-final-integration-acceptance
   git checkout review/aipms-final-integration-acceptance
   git pull origin review/aipms-final-integration-acceptance
   git rev-parse HEAD # Bắt buộc phải là: 3f71a30f1f03027be203025e17b2a6908d0ef84b

   # Bước 2: Build lại image và up container với COMMIT_SHA
   export COMMIT_SHA=$(git rev-parse HEAD)
   docker compose -f deploy/wsl/compose.yaml build --build-arg COMMIT_SHA=$COMMIT_SHA api
   docker compose -f deploy/wsl/compose.yaml up -d api

   # Bước 3: Xác minh endpoint public
   curl -s https://api-staging.khaidz.com/api/v1/system | jq .
   ```

   **Lệnh Rollback an toàn (nếu cần):**
   ```bash
   docker compose -f deploy/wsl/compose.yaml restart api
   ```

---

## D. HỢP ĐỒNG API BÀN GIAO CHO TEAM FRONTEND (API HANDOFF)

### 1. Nguyên tắc Giao tiếp & CORS (Lưu ý Trình duyệt)
- **Header `Origin`**: Trình duyệt Web tự động quản lý và gửi header `Origin` khi gửi các yêu cầu cross-origin hoặc WebSocket handshake. **Đội ngũ Frontend không cần và không được đặt thủ công header `Origin`** (trình duyệt sẽ ném lỗi `Refused to set unsafe header "Origin"`).
- **Backend CORS**: Backend đã cấu hình danh sách allowed origins qua `CorsSettings:AllowedOrigins` (mặc định cho phép `http://localhost:5173`, `http://localhost:5175`, `http://127.0.0.1:5197`). Khi triển khai staging/production, cấu hình được nạp từ biến môi trường `Cors__AllowedOrigins__0`.

### 2. Chi tiết Hợp đồng các Module Trọng yếu

#### a. Chat & Attachments (UC-136)
- REST:
  - `POST /api/v1/chat/projects/{projectId}/conversation`: Mở/lấy phòng chat đồ án.
  - `POST /api/v1/chat/conversations/{id}/messages`: `{ clientMessageId: UUID, body: string, attachmentIds?: string[] }`.
  - `POST /api/v1/chat/conversations/{id}/uploads`: Upload tệp đính kèm (`multipart/form-data`, form field `file`, giới hạn 10MB).
  - `GET /api/v1/chat/conversations/{id}/messages`: Query tin nhắn (cursor pagination qua `beforeSequence`).
- SignalR WebSocket:
  - Endpoint: `/hubs/chat` (kết nối kèm Bearer token qua `accessTokenFactory` hoặc query `?access_token=...`).
  - Events client lắng nghe: `MessagesChanged`, `ConversationChanged`, `TypingChanged`, `PresenceChanged`, `AccessRevoked`.

#### b. Appeals & Corrections (UC-137)
- `POST /api/v1/projects/{projectId}/result/appeal-policy`: Staff lập chính sách (`{ expectedVersion: number, approvalReference: string }`).
- `POST /api/v1/projects/{projectId}/result/appeal-windows`: Staff mở đợt nhận đơn.
- `POST /api/v1/projects/{projectId}/result/correction-requests`: Sinh viên nộp đơn (`{ kind, policyId, windowId, scope, targetStudentId, studentResultVersion, componentId, reason }`).
- `POST /api/v1/projects/{projectId}/result/correction-requests/{id}/reviewer`: Staff chỉ định giảng viên phúc khảo (Primary Supervisor bị cấm làm reviewer).
- `PUT /api/v1/projects/{projectId}/result/correction-requests/{id}/review`: Reviewer nhập nhận xét và điểm sửa đổi kèm `concurrencyToken`.
- `POST /api/v1/projects/{projectId}/result/correction-requests/{id}/decision`: Staff chốt quyết định (`{ concurrencyToken, decision: "ACCEPTED"|"REJECTED", note, confirmationToken }`).
- Error Code chuẩn: `APPEAL_WINDOW_CLOSED` (409 Conflict) khi nộp đơn ngoài khung thời gian.

#### c. COLD Reports & Evaluation (UC-142)
- `POST /api/v1/cold-report-templates`: Department Staff tạo template danh mục báo cáo. Bắt buộc mảng `reports` phải có ít nhất 1 báo cáo có `required: true`.
- `POST /api/v1/projects/{projectId}/cold/checklist`: Staff áp dụng checklist cho đợt đồ án.
- `POST /api/v1/cold-checklist-items/{itemId}/draft`: Sinh viên nộp draft báo cáo và tải file minh chứng.
- `POST /api/v1/projects/{projectId}/cold/lock`: **Chỉ Department Staff của Lead Department mới có quyền khóa checklist**. Nếu có báo cáo bắt buộc chưa nộp, API trả `409 Conflict` (`COLD_REPORT_REQUIRED`).
- **Bất biến D17**: Giảng viên hướng dẫn chính (Primary Supervisor) bị chặn, không được phân công chấm COLD của đề tài mình (`409 EVALUATOR_ROLE_CONFLICT`).

#### d. Defense & Sessions (UC-143)
- `POST /api/v1/projects/{projectId}/defense-committees`: Staff lập Hội đồng bảo vệ (`{ name, members: [{ userId, majorId }] }`). Thành viên phải có hồ sơ `supervisor_profiles` khả dụng và chuyên môn trùng khớp.
- `POST /api/v1/projects/{projectId}/defense-sessions`: Staff xếp lịch bảo vệ (`{ committeeId, startAt, endAt, room }`). Trùng phòng/giờ sẽ bị trả `409 Conflict`.
- `POST /api/v1/defense-sessions/{id}/cancel`: Staff hủy ca bảo vệ (`{ concurrencyToken, reason }`).

#### e. Academic AI Assistant (UC-148)
- `POST /api/v1/academic/assistant/ask`: Hỏi đáp quy chế học vụ (`{ query, mode, projectPeriodId }`).
- `POST /api/v1/academic/assistant/stream`: Nhận phản hồi dạng stream NDJSON (`application/x-ndjson`).
- `POST /api/v1/projects/{projectId}/ai/assistant/ask`: Hỏi đáp tiến độ đề tài (chỉ thành viên nhóm mới có quyền truy cập, người ngoài nhận `403 Forbidden`).
- **Rate Limit**: Áp dụng giới hạn 20 requests/phút theo IP/User, request thứ 21 trả `429 Too Many Requests`.
- **Response Format**: Trả về `answer`, `answerType`, mảng `citations` (`[{ id, title, documentCode, excerpt }]`), và `insufficientEvidence`. Khi dịch vụ bên ngoài gián đoạn, backend trả về cấu trúc fallback an toàn, không sinh lỗi crash 500.

---

## E. BÀN GIAO TRẠNG THÁI GIT (GIT HANDOFF)

1. **Nhánh làm việc**: `review/aipms-final-integration-acceptance`.
2. **PR Đang Tồn Tại**: **PR #139** (Base: `develop`, Remote HEAD: `3f71a30f1f03027be203025e17b2a6908d0ef84b`).
3. **Files Changed/Untracked**:
   - `modified: scripts/prepare-v5-demo.ps1` (Đã chuẩn hóa biến động, sẵn sàng đưa vào commit nếu được User cho phép).
   - `untracked: docs/FINAL_BACKEND_ACCEPTANCE_REPORT.md` (Tài liệu nghiệm thu chính thức).
   - `untracked: docs/FINAL_STAGING_ACCEPTANCE_REPORT.md` (Tài liệu nghiệm thu staging trước đó).
4. **Nguyên tắc an toàn**:
   - **Chưa có local commit nào được tạo thêm mà chưa có lệnh trực tiếp**. Remote branch và PR #139 vẫn giữ nguyên HEAD tại `3f71a30`.
   - Khi Quản lý Dự án cho phép, tôi có thể tạo một local commit sạch:
     `chore(scripts): stabilize demo database preparation and add final backend acceptance documentation`
     và chờ lệnh push tiếp theo.

---

## F. KẾT LUẬN & QUYẾT ĐỊNH NGHIỆM THU (COMPLETION DECISION)

- [x] **`BE CODE COMPLETE`**: Toàn bộ 14/14 module v5 đã có mã nguồn Backend hoàn chỉnh, không có bug mở.
- [x] **`BE INTEGRATION TESTS PASS`**: 2.583 Unit & Integration Tests cùng các targeted tests (Chat, Appeals, COLD, Defense, AI, D18) đều PASS 100%.
- [x] **`BE CHANGES READY FOR REVIEW`**: Mã nguồn và script đã sẵn sàng cho review.
- [ ] **`STAGING DEPLOYMENT PENDING`**: Chờ Khải thực hiện 3 lệnh deploy trên WSL theo Mục C để image nhận `COMMIT_SHA`.
- [ ] **`ACADEMIC POLICY SIGN-OFF PENDING`**: Phê duyệt chính sách ghép cặp D18 thuộc thẩm quyền của Hội đồng học vụ.
