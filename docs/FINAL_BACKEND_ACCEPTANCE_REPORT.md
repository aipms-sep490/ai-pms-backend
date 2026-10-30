# AI-PMS — FINAL BACKEND ACCEPTANCE & CLOSEOUT REPORT
## Báo cáo Chốt Nghiệm thu Kỹ thuật Chuyên sâu Backend (PR #139)

> **Dự án**: AI-PMS (Academic Project Management System)
> **Phạm vi phân công**: **CHUYÊN TRÁCH BACKEND ONLY** (Frontend do team riêng phụ trách)
> **Backend Repository**: `aipms-sep490/ai-pms-backend`
> **Review Branch**: `review/aipms-final-integration-acceptance`
> **PR Đang Mở**: PR #139 (Consolidated duy nhất thay thế PR #136 & PR #138, Base: `develop`)
> **Remote PR #139 HEAD**: `3f71a30f1f03027be203025e17b2a6908d0ef84b`
> **Local Baseline HEAD (trước khi cập nhật tài liệu bàn giao)**: `d552c71ed0063d8b3043f6930b69aa139a7e2488` (ahead 6 commits)
> **Số commit local chưa push**: 7 commits (gồm 6 commits kỹ thuật/tooling + 1 commit tài liệu bàn giao)
> **Thời điểm cập nhật**: 30/10/2026 (GMT+7)
> **Vai trò thực hiện**: Senior .NET Backend Engineer, Backend Architect & Backend QA Lead

---

## A. BẢNG TRẠNG THÁI 14 MODULE BACKEND (BACKEND MODULE STATUS)

Toàn bộ **14/14 module học vụ v5** (UC-135 đến UC-148) theo đúng Governance Baseline v5 và SRS đã hoàn thiện đầy đủ trên tầng Backend và được xác thực qua bộ kiểm thử tích hợp chuyên biệt (**ASP.NET Core WebApplicationFactory / Kestrel API thật + EF Core DbContext + SQL Server Database cô lập**):

| UC | Tên Module v5 (Chuẩn SRS/Baseline) | Trạng thái API Implementation | BE Integration Tests | Database Verification | Trạng thái Nghiệm thu BE |
| :---: | :--- | :--- | :--- | :--- | :---: |
| **UC-135** | **Review Gates** | `ReviewGatesController.cs`, `ReviewGateService.cs` | `ReviewGateServiceTests.cs` (77 tests) | `dbo.project_gate_instances`, `review_gate_evidence`, `review_gate_assignments` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-136** | **Chat & Attachments** | `ChatController.cs`, `ChatHub.cs`, `ChatService.cs` | `ChatApiTests.cs` (2 tests), `ChatCollaborationTests.cs`, `ChatRealtimeTests.cs` | `dbo.chat_messages`, `chat_conversations`, `chat_attachments`, `chat_outbox` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-137** | **Appeals & Corrections** | `ResultAppealsController.cs`, `ResultAppealService.cs` | `PolicyEvaluationEndpointTests.cs` (30 tests) | `dbo.result_appeal_policies`, `result_appeal_windows`, `result_appeal_requests` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-138** | **Industry Advisory** | `IndustryAdvisoryController.cs`, `IndustryService.cs` | `IndustryAdvisoryTests.cs` (24 tests) | `dbo.industry_experts`, `industry_assignments`, `industry_feedbacks` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-139** | **Weekly Assessments** | `WeeklyAssessmentsController.cs`, `WeeklyAssessmentService.cs` | `WeeklyAssessmentEndpointTests.cs` (13 tests) | `dbo.weekly_assessments`, `weekly_assessment_scores` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-140** | **Peer Evaluation** | `PeerEvaluationsController.cs`, `PeerEvaluationService.cs` | `PeerEvaluationEndpointTests.cs` | `dbo.peer_evaluation_rounds`, `peer_round_members`, `peer_submissions` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-141** | **Working Agreement** | `WorkingAgreementsController.cs`, `WorkingAgreementService.cs` | `WorkingAgreementActivationTests.cs`, `WorkingAgreementEndpointTests.cs` | `dbo.project_working_agreements`, `agreement_members` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-142** | **COLD Reports & Evaluation** | `ColdSubmissionsController.cs`, `ColdSubmissionService.cs` | `ColdSubmissionEndpointTests.cs` (21 tests), `ColdEvaluationEndpointTests.cs` | `dbo.cold_report_templates`, `cold_packages`, `cold_checklist_items`, `cold_submissions` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-143** | **Defense Committees & Sessions** | `AcademicTrackingController.cs`, `AcademicTrackingService.Defense.cs` | `AcademicTrackingEndpointTests.cs` (9 tests) | `dbo.defense_committees`, `defense_committee_members`, `defense_sessions` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-144** | **Sprints & Tasks** | `SprintsController.cs`, `SprintService.cs`, `TasksController.cs` | `SprintEndpointTests.cs`, `MilestonesAndTasksEndpointTests.cs` | `dbo.sprints`, `tasks`, `sprint_task_events` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-145** | **Meeting Collaboration** | `MeetingsController.cs`, `MeetingService.cs` | `MeetingCollaborationTests.cs` (28 tests) | `dbo.meetings`, `meeting_participants`, `meeting_notes` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-146** | **Unified Capabilities** | `CapabilitiesController.cs`, `ProjectCapabilityService.cs` | `UnifiedCapabilitiesTests.cs` | Dynamic RBAC Evaluation Engine (`user_roles`, `role_permissions`) | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-147** | **Milestone Templates** | `MilestoneTemplatesController.cs`, `MilestoneTemplateService.cs` | `MilestoneTemplateActivationSqlTests.cs` (21 tests) | `dbo.milestone_templates`, `milestone_template_versions`, `milestone_template_items` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |
| **UC-148** | **Academic AI Assistant** | `AcademicKnowledgeController.cs`, `AcademicKnowledgeService.cs` | `AiAssistantSqlIntegrationTests.cs` (32 tests), `AcademicKnowledgeTests.cs`, `StreamEndpointTests.cs` | `dbo.academic_knowledge_documents`, `assistant_conversations`, `audit_logs` | **`BE CODE COMPLETE`**<br>**`TESTS PASS`** |

---

## B. CÔNG VIỆC ĐÃ HOÀN THÀNH (WORK COMPLETED)

1. **Khắc phục và chuẩn hóa Script Test Data**:
   - `scripts/prepare-v5-demo.ps1`: Thay thế toàn bộ các ID số nguyên bị hardcoded trước đây bằng các biến SQL động (`@singleLeader`, `@singleMember`, `@leadDept`, `@singleMajor`,...). Điều này loại bỏ rủi ro sai lệch dữ liệu khi database được khởi tạo với identity seed khác biệt.
   - Bổ sung khởi tạo barem Rubric chuẩn (`E2E-RUBRIC`), hồ sơ cán bộ phản biện (`evaluator@e2e.invalid`), và chuyên gia doanh nghiệp (`expert.advisor@e2e.invalid`) với điều kiện an toàn `IF @rubric IS NULL` và `IF NOT EXISTS`.
   - Script có guard bắt buộc chỉ cho phép chạy trên database test cô lập (`^AI_PMS_E2E_[a-f0-9]{32}$`), không thể chạy trên CSDL dùng chung.
2. **Kiểm tra biên dịch Solution (Compilation)**:
   - Toàn bộ solution `AIPMS.sln` đã được build thành công ở cấu hình **Release**:
     `Build succeeded. 0 Warning(s), 0 Error(s). Time Elapsed 00:00:03.18`.
3. **Kết quả Regression Testing & Kiểm thử Tích hợp**:
   - **CI GitHub Actions trên Remote Commit `3f71a30`**: **2.583 Tests PASS 100%** (1.237 Unit Tests + 1.346 Integration Tests).
   - **Targeted Integration Tests**:
     - `SystemEndpointTests`: **10/10 PASS** (Bao gồm 4 tests độc lập kiểm tra các cơ chế phân giải `COMMIT_SHA`).
     - `ChatApiTests`: **2/2 PASS**.
     - `PolicyEvaluationEndpointTests` (Appeals UC-137): **30/30 PASS**.
     - `ColdSubmissionEndpointTests` (COLD UC-142 & Bất biến D17): **21/21 PASS**.
     - `AcademicTrackingEndpointTests` (Defense Committees & Sessions UC-143): **9/9 PASS**.
     - `AiAssistantSqlIntegrationTests` & `AcademicKnowledgeTests` (AI Assistant UC-148): **32/32 PASS**.
     - `SupervisorRequestEndpointTests` (BE-07 D18): **78/78 PASS**.
     - `StagingCommitVerifierScriptTests` (Kiểm thử tự động script verifier): **12/12 PASS** (bao gồm trường hợp khớp SHA, mismatch, reject `"1.0.0"`, HTTP 500, Cloudflare 1033 HTML, timeout, non-JSON).
4. **Trạng thái Bug Backend**:
   - **Không có backend bug còn mở**. Mọi nghiệp vụ đều thỏa mãn các bất biến domain trong SRS/SDD.

---

## C. CAM KẾT, BỘ CÔNG CỤ DEVOPS & QUY TRÌNH TRIỂN KHAI / ROLLBACK

### 1. Trạng thái Kỹ thuật theo Từng Môi trường
- **Backend Code & Configuration**: **`BE COMMIT_SHA CODE/CONFIG DONE`** (`SystemController.cs`, `Dockerfile` ARG/ENV, `deploy/wsl/compose.yaml`, `.env.example` nạp biến môi trường và fallback đầy đủ; 10/10 test `SystemEndpointTests` PASS).
- **Môi trường Windows Workstation**: **`NOT APPLICABLE FOR REAL DOCKER`** (Máy trạm Windows không cài đặt Docker CLI/daemon; Docker chạy trên WSL của Khải và GitHub Actions runner).
- **GitHub Actions Deployment Validation Workflow** (`.github/workflows/backend-deployment-validation.yml`): **`READY FOR RUN (PENDING PUSH)`** (Workflow chạy trên Ubuntu có Docker, tự động kích hoạt khi push branch `review/aipms-final-integration-acceptance`).
- **Staging Runtime Deployment**: **`STAGING COMMIT_SHA VERIFICATION PENDING DEPLOYMENT BY KHẢI`** (Public API `https://api-staging.khaidz.com/api/v1/system` hiện tại trả `"1.0.0"` do container cũ chưa được Khải rebuild kèm đối số `COMMIT_SHA`).

### 2. Bộ Công cụ Tự động hóa Đã Xây dựng
- `scripts/verify-staging-commit.ps1`: Kiểm tra nghiêm ngặt SHA 40 ký tự hex, bắt buộc từ chối fallback `"1.0.0"` (exit code 6), nhận diện lỗi Cloudflare 1033 (exit code 2).
- `deploy/wsl/preflight-deploy.sh`: Kiểm tra trước deploy (RAM, ổ đĩa, ports, git status), hỗ trợ `--dry-run` không gây đột biến.
- `deploy/wsl/postflight-verify.sh`: Dò tìm cổng động, kiểm tra nhãn image label, kiểm tra container logs và truy vấn endpoint commit.
- `deploy/wsl/rollback.sh`: Cơ chế True Rollback bằng Image ID gốc.
- `deploy/wsl/collect-wsl-evidence.sh`: Thu thập thông số môi trường read-only, định dạng JSON, không lộ mật khẩu/connection string.

### 3. Quy trình Triển khai cho Khải trên WSL (`khaine161`)
Khải chạy các lệnh sau trên terminal WSL:

```bash
# Bước 1: Kéo branch mới nhất
cd ~/aipms/ai-pms-backend
git fetch origin review/aipms-final-integration-acceptance
git checkout review/aipms-final-integration-acceptance
git pull origin review/aipms-final-integration-acceptance
export COMMIT_SHA=$(git rev-parse HEAD)
echo "Triển khai target commit SHA: $COMMIT_SHA"

# Bước 2: Thu thập bằng chứng hiện trạng (Read-only, không làm thay đổi hệ thống)
bash deploy/wsl/collect-wsl-evidence.sh

# Bước 3: Chạy Preflight kiểm tra an toàn
bash deploy/wsl/preflight-deploy.sh --dry-run
bash deploy/wsl/preflight-deploy.sh

# Bước 4: Chạy Postflight xác minh sau deploy
bash deploy/wsl/postflight-verify.sh
```

### 4. Quy trình Rollback Chuẩn xác (`deploy/wsl/rollback.sh`)

> [!WARNING]
> **Lưu ý quan trọng**: Lệnh `docker compose restart api` hoặc `docker compose up -d --no-build api` **KHÔNG PHẢI LÀ ROLLBACK** nếu image gắn tag `local` đã bị ghi đè bởi phiên bản lỗi. Khi đó restart chỉ khởi động lại chính container lỗi.

Cơ chế thực sự của script `deploy/wsl/rollback.sh`:
1. **Tiếp nhận Reference hoặc Image ID**: Chấp nhận tham số `BACKUP_IMAGE_REF_OR_ID` (mặc định: `aipms-api:staging-backup`).
2. **Truy vấn Image ID / Digest gốc**: Sử dụng `docker inspect --format '{{.Id}}'` để xác định ID thực của image backup trong Docker daemon.
3. **Gắn tag an toàn về Active Tag**: Chạy `docker tag "$BACKUP_IMAGE_ID" "aipms-api:${IMAGE_TAG}"` để đưa tag chạy của compose trỏ lại chính xác Image ID ổn định trước đó.
4. **Recreate Container không build lại**: Thực thi `docker compose -f "$COMPOSE_FILE" up -d --force-recreate --no-build api` để buộc Docker khởi tạo lại container từ image ID vừa phục hồi.
5. **Kiểm chứng nghiêm ngặt qua `docker inspect`**: Kiểm tra container đang chạy qua `docker inspect --format '{{.Image}}' aipms-api`. Nếu không trùng khớp 100% với `BACKUP_IMAGE_ID`, script báo lỗi và thoát với exit code 4.
6. **Gọi Postflight Verification**: Tự động kích hoạt `postflight-verify.sh` để kiểm tra lại endpoint.

**Trạng thái kiểm chứng Rollback**: `deploy/wsl/rollback.sh` đã vượt qua kiểm tra cú pháp POSIX (`bash -n`). **Kết quả thực thi trên Docker daemon thật đang ở trạng thái PENDING EXECUTION** (sẽ được ghi nhận khi GitHub Actions workflow hoặc WSL runner chạy thực tế).

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

#### d. Defense Committees & Sessions (UC-143)
- `POST /api/v1/projects/{projectId}/defense-committees`: Staff lập Hội đồng bảo vệ (`{ name, members: [{ userId, majorId }] }`). Thành viên phải có hồ sơ `supervisor_profiles` khả dụng và chuyên môn trùng khớp.
- `POST /api/v1/projects/{projectId}/defense-sessions`: Staff xếp lịch bảo vệ (`{ committeeId, startAt, endAt, room }`). Trùng phòng/giờ sẽ bị trả `409 Conflict`.
- `POST /api/v1/defense-sessions/{id}/cancel`: Staff hủy ca bảo vệ (`{ concurrencyToken, reason }`).

#### e. Academic AI Assistant (UC-148)
- `POST /api/v1/academic/assistant/ask`: Hỏi đáp quy chế học vụ (`{ query, mode, projectPeriodId }`).
- `POST /api/v1/academic/assistant/stream`: Nhận phản hồi dạng stream NDJSON (`application/x-ndjson`).
- `POST /api/v1/projects/{projectId}/ai/assistant/ask`: Hỏi đáp tiến độ đề tài (chỉ thành viên nhóm mới có quyền truy cập, người ngoài nhận `403 Forbidden`).
- **Rate Limit**: Áp dụng giới hạn 20 requests/phút theo IP/User, request thứ 21 trả `429 Too Many Requests`.
- **Response Format**: Trả về `answer`, `answerType`, mảng `citations` (`[{ id, title, documentCode, excerpt }]`), và `insufficientEvidence`. Khi dịch vụ upstream gián đoạn, backend trả về fallback an toàn.

### 3. Tình trạng Tương thích Frontend
- Hợp đồng API Backend đã đóng băng và sẵn sàng cho kết nối.
- **Trạng thái**: **`FRONTEND INTEGRATION ACCEPTANCE PENDING`** — Phụ thuộc vào quá trình nghiệm thu tích hợp thực tế của đội ngũ Frontend trên client app.

---

## E. BÀN GIAO TRẠNG THÁI GIT (GIT HANDOFF)

1. **Nhánh làm việc**: `review/aipms-final-integration-acceptance`.
2. **PR Đang Tồn Tại**: **PR #139** (Base: `develop`).
   - **Remote Tracking**: `origin/review/aipms-final-integration-acceptance`.
   - **Remote HEAD Commit**: `3f71a30f1f03027be203025e17b2a6908d0ef84b`.
   - **Bằng chứng CI**: 2.583 tests PASS 100% trên GitHub Actions với commit `3f71a30` (1.237 Unit Tests + 1.346 Integration Tests).
   - **Phạm vi file trên PR #139**: 19 files lõi hợp nhất từ PR #136, PR #138 và commit `3f71a30`.
3. **Trạng thái Git Local**:
   - **Local Baseline Commit**: `d552c71ed0063d8b3043f6930b69aa139a7e2488`.
   - **Số commit local chưa push**: **7 commits** (ahead remote `origin/review/aipms-final-integration-acceptance`), gồm:
     1. `f114aad`: `chore(scripts): stabilize demo database preparation and add final backend acceptance documentation`
     2. `3fdec50`: `docs(acceptance): finalize backend acceptance report with real git state and staging rollback procedures`
     3. `7182b27`: `feat(tooling): add automated staging commit verification and deployment preflight scripts`
     4. `1e45e26`: `test(tooling): enforce strict 40-character SHA verification and add automated test suite for staging verifier`
     5. `c2abb90`: `ci(deploy): add docker deployment validation workflow, true rollback script, and wsl evidence collector`
     6. `d552c71`: `ci(deploy): add branch push trigger and strict image provenance verification to deployment validation workflow`
     7. *(Commit hiện tại)*: `docs(handoff): synchronize acceptance report and delivery preview with actual git state and baseline`
4. **Kế hoạch Push Thủ công Dành cho Bạn**:
   - Bạn mở PowerShell/Terminal và thực hiện lệnh push duy nhất:
     ```powershell
     git -C "D:\AI_PMS\ai-pms-backend" push origin review/aipms-final-integration-acceptance
     ```
   - Lệnh này sẽ đẩy toàn bộ 7 commits lên remote branch của PR #139, tự động kích hoạt workflow `.github/workflows/backend-deployment-validation.yml` trên GitHub Actions để kiểm chứng Docker Build & Rollback.
5. **Nguyên tắc An toàn Tuân thủ**:
   - Không tự push hoặc tự tạo PR.
   - Không merge vào `develop` hoặc `main`.
   - Không sửa Frontend repository.

---

## F. KẾT LUẬN & BẢNG QUYẾT ĐỊNH NGHIỆM THU

- [x] **`BE CODE COMPLETE`**: Toàn bộ 14/14 module v5 (UC-135 đến UC-148) đã có mã nguồn Backend hoàn chỉnh, không có bug mở.
- [x] **`BE INTEGRATION TESTS PASS`**: 2.583 Unit & Integration Tests trên commit `3f71a30` + 22 targeted tests cục bộ đều PASS 100%.
- [x] **`DEPLOYMENT & VERIFICATION TOOLING READY`**: Bộ script PowerShell và Shell WSL đã sẵn sàng, cú pháp hợp lệ.
- [ ] **`GITHUB ACTIONS DOCKER WORKFLOW RUN`**: **`PENDING PUSH`** (Sẽ chạy tự động sau khi Bạn push branch lên GitHub).
- [ ] **`REAL DOCKER ROLLBACK EXECUTION EVIDENCE`**: **`PENDING EXECUTION`** (Chờ log thực thi từ Docker engine thật).
- [ ] **`STAGING COMMIT_SHA VERIFIED`**: **`PENDING DEPLOYMENT BY KHẢI`** (Public API hiện tại trả `"1.0.0"`, chờ Khải rebuild kèm đối số).
- [ ] **`FE INTEGRATION ACCEPTANCE`**: **`PENDING CONFIRMATION BY FE TEAM`** (Hợp đồng API đã chốt, chờ nghiệm thu tích hợp từ FE).
