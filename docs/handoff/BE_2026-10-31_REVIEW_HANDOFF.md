# AI-PMS — BÁO CÁO TỔNG KẾT VÀ BÀN GIAO REVIEW BACKEND (31/10/2026)

> **Mục tiêu**: Bàn giao tổng thể kết quả hoàn tất các nhiệm vụ kỹ thuật ngày 31/10/2026, ma trận 23 tasks Backend, bằng chứng HTTP contracts và trạng thái sẵn sàng nghiệm thu cho PR #139 (`review/aipms-final-integration-acceptance` -> `develop`).  
> **Người nhận tài liệu**: Tin (Frontend / Reviewer), Khải (DevOps / Solution Lead), Academic Committee.

---

## I. TỔNG QUAN KẾT QUẢ THỰC HIỆN (EXECUTIVE SUMMARY)

Trong phiên làm việc ngày 31/10/2026, Backend Coding Agent đã hoàn thành toàn bộ các đầu việc trong tài liệu `BE_NHIEM_VU_2026-10-31.md`:

1. **Ba nhiệm vụ kỹ thuật cốt lõi (Nhiệm vụ A)**:
   - Chuẩn hóa lỗi `ACADEMIC_SCOPE_UNKNOWN` thành mã máy RFC 7807 trong `problem.code` tại toàn bộ các service và workflow; bổ sung response-level HTTP integration tests.
   - Khắc phục triệt để logic fallback của `AcademicKnowledgeService`: loại bỏ hoàn toàn việc dò chuỗi `answer.Contains("kết nối")`, phân loại chính xác lỗi transport/network bằng strongly-typed exceptions, bổ sung regression test chống rò rỉ evidence/citations khi tài liệu chứa từ khóa "kết nối".
   - Bổ sung integration tests gửi HTTP request thực tế qua WebApplicationFactory, chứng minh phản hồi `HTTP 409 Conflict` kèm đúng `problem.code` cho đủ 5 mã lỗi Progress Report mới (và xác minh 2 mã lỗi cũ, tổng cộng 7 mã lỗi).
2. **Ổn định hạ tầng kiểm thử tích hợp (Integration Test Infrastructure)**:
   - Xử lý dứt điểm hiện tượng SQL Connection Timeout khi chạy trọn vẹn bộ 1.373 Integration Tests trên SQL Server Express cục bộ: tái sử dụng connection qua 64 scripts migration trong `SqlSchemaBootstrap.cs`, bổ sung cơ chế retry khi dọn dẹp database trong `IsolatedSqlDatabase.cs`, và cấu hình `parallelizeTestCollections: false` trong `tests/AIPMS.IntegrationTests/xunit.runner.json`.
3. **Bằng chứng kiểm thử thực nghiệm (Test Evidence)**:
   - **Build Release (`-warnaserror`)**: 0 Warning(s), 0 Error(s).
   - **Unit Tests**: **1.237 / 1.237 PASS** (100%).
   - **Integration Tests**: **1.373 / 1.373 PASS** (100% full suite, thời gian chạy: 38m28s, 0 fail, 0 skipped, 0 database test tồn đọng).
   - **Tổng cộng**: **2.610 / 2.610 automated tests PASS** trên commit HEAD `efe45a0`.
4. **Tài liệu bàn giao cho Frontend và Team Review (Nhiệm vụ B, C, D)**:
   - `docs/handoff/BE_2026-10-31_FE_ERROR_CONTRACT_HANDOFF.md`: Bàn giao chi tiết 7 mã lỗi HTTP 409 Progress Report và hợp đồng `ACADEMIC_SCOPE_UNKNOWN`.
   - `docs/handoff/BE18_V5_E2E_ACCEPTANCE_MATRIX.md`: Ma trận nghiệm thu 14 module v5 (UC-135 đến UC-148), quy tắc phân định trạng thái rõ ràng giữa `IMPLEMENTED`, `AUTOMATED VERIFIED` và `STAGING E2E ACCEPTED`.

---

## II. CHI TIẾT 3 TECHNICAL FIXES NGÀY 31/10

### 1. Nhiệm vụ A1 — Chuẩn hóa `ACADEMIC_SCOPE_UNKNOWN`
- **Mã nguồn**:
  - `src/AIPMS.Application/Common/Constants/WorkflowErrorCodes.cs`: Định nghĩa `public const string AcademicScopeUnknown = "ACADEMIC_SCOPE_UNKNOWN";`.
  - Thay thế toàn bộ các chuỗi cứng `"ACADEMIC_SCOPE_UNKNOWN"` thành `WorkflowErrorCodes.AcademicScopeUnknown` trong:
    - `src/AIPMS.Application/Features/SupervisorWorkflow/Services/SupervisorAssignmentWorkflow.cs:51`
    - `src/AIPMS.Infrastructure/Persistence/Repositories/ProjectRepository.Hybrid.cs:122`
    - `src/AIPMS.Application/Features/SupervisorWorkflow/Services/SupervisorRecommendationService.cs:42`
    - `src/AIPMS.Application/Features/PeerEvaluations/Services/PeerEvaluationService.cs:45`
    - `src/AIPMS.Application/Features/ResultAppeals/Services/ResultAppealService.cs:45`
- **Response-Level Integration Tests (HTTP 409 & `problem.code`)**:
  - `SupervisorRecommendationEndpointTests.cs:198`: `Recommendations_without_frozen_academic_scope_returns_409_with_AcademicScopeUnknown` -> **PASS**.
  - `PeerEvaluationEndpointTests.cs:152`: `Peer_without_frozen_academic_scope_returns_409_with_AcademicScopeUnknown` -> **PASS**.

### 2. Nhiệm vụ A2 — Academic AI Fallback & Chống rò rỉ Evidence
- **Mã nguồn**:
  - `src/AIPMS.Application/Features/AcademicKnowledge/Services/AcademicKnowledgeService.cs:292`:
    ```csharp
    var isConnectionFailure = ex is HttpRequestException or TimeoutException or IOException or SocketException or OperationCanceledException;
    ```
  - Loại bỏ hoàn toàn kiểm tra `answer.Contains("kết nối")`. Khi AI trả về JSON lỗi, schema không hợp lệ, hoặc parsing exception, hệ thống không bị đánh đồng thành sự cố mạng.
  - Phân lập evidence: Khi evidence đầu vào chứa chữ "kết nối" (ví dụ: "kết nối doanh nghiệp") và bị filter hoặc AI lỗi, hệ thống trả về fallback chuẩn, không rò rỉ project evidence hoặc trích dẫn sai.
- **Regression Tests**:
  - `AcademicKnowledgeTests.cs:455`: `FallbackWithEvidenceContainingConnectKeyword_DoesNotTriggerNetworkErrorMessageAndDoesNotLeakEvidence` -> **PASS**.
  - Toàn bộ suite `AcademicKnowledgeTests.cs` (13 tests) -> **PASS**.

### 3. Nhiệm vụ A3 — Bảy mã lỗi Progress Report HTTP 409
Tất cả 7 mã lỗi nghiệp vụ của Progress Report đều có integration test gửi HTTP request thật và kiểm tra chính xác HTTP 409 cùng `problem.code`:
1. `REPORT_CYCLE_REQUIRED`: `ProgressReportEndpointTests.cs:727` (`POST /api/v1/progress-reports/{id}/submit`) -> **PASS**.
2. `REPORT_DEADLINE_PASSED`: `ProgressReportEndpointTests.cs:768` (`POST /api/v1/progress-reports/{id}/submit`) -> **PASS**.
3. `REPORT_ALREADY_EXISTS_FOR_CYCLE`: `ProgressReportEndpointTests.cs:826` (`POST /api/v1/projects/{projectId}/progress-reports`) -> **PASS**.
4. `REPORT_ALREADY_EXISTS_FOR_PERIOD`: `ProgressReportEndpointTests.cs:896` (`POST /api/v1/projects/{projectId}/progress-reports`) -> **PASS**.
5. `REPORT_FEEDBACK_ON_DRAFT_NOT_ALLOWED`: `ProgressReportEndpointTests.cs:943` (`POST /api/v1/progress-reports/{id}/feedback`) -> **PASS**.
6. `REPORT_ALREADY_SUBMITTED`: `ProgressReportEndpointTests.cs:263` (`POST /api/v1/progress-reports/{id}/submit`) -> **PASS**.
7. `REPORT_NOT_EDITABLE`: `ProgressReportEndpointTests.cs:269` (`PUT /api/v1/progress-reports/{id}`) -> **PASS**.

---

## III. MA TRẬN TỔNG HỢP 23 TASK BACKEND (BE-01 ĐẾN BE-23)

Bảng đối chiếu phục vụ reviewer đánh giá nhanh tình trạng triển khai và nghiệm thu:

| Task ID | Tên chức năng | File Implementation chính | Automated Test chứng minh | Trạng thái Code | Trạng thái Nghiệm thu Thực tế | Dependency còn lại |
| :--- | :--- | :--- | :--- | :---: | :---: | :--- |
| **BE-01** | Capability chặn mời Mentor (FUNC-08) | `CapabilityEvaluator.cs`<br>`ExecutionCapabilitiesController.cs` | `CapabilityEnforcementParityTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | Chờ FE bật capability check |
| **BE-02** | Dịch vụ Email & Staging Fallback (ENV-04) | `EmailService.cs`<br>`SmtpEmailSender.cs` | `EmailNotificationTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | Khải cấu hình SMTP staging |
| **BE-03** | Google Login Origin (ENV-02) | `GoogleAuthService.cs`<br>`Program.cs` (CORS) | `AuthEndpointTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | Google Client ID staging |
| **BE-04** | Timestamp UTC offset (Z) toàn cục | `UtcDateTimeJsonConverter.cs`<br>`Program.cs` | `JsonOptionsParityTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | FE parse ISO 8601 UTC |
| **BE-05** | AuthUserDto bổ sung Title/Phone (FUNC-04) | `AuthUserDto.cs`<br>`AuthService.cs` | `AuthEndpointTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | FE1 hiển thị profile |
| **BE-06** | Member tạo báo cáo tiến độ nháp (SPEC-02) | `ProgressReportService.cs`<br>`ProgressReportsController.cs` | `ProgressReportEndpointTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | FE form nộp draft |
| **BE-07** | Thứ tự mời GVHD & Invariants (SPEC-01/D18) | `SupervisorRequestWorkflow.cs`<br>`SupervisorAssignmentWorkflow.cs` | `SupervisorRequestEndpointTests.cs`<br>`D18SupervisorInvariantsTests.cs` (77 tests) | **IMPLEMENTED** | `AUTOMATED VERIFIED`<br>*(D18 academic governance pending sign-off)* | Xác nhận chính thức từ Academic Committee |
| **BE-08** | Generate Risk Alert (UC-108) | `RiskAlertBackgroundService.cs`<br>`RiskAssessmentService.cs` | `RiskAlertTests.cs` | **IMPLEMENTED** *(Background worker, không có Controller riêng)* | `AUTOMATED VERIFIED` | Kiểm tra cron job trên staging |
| **BE-09** | Auto-prefill Progress Data (UC-076) | `ProgressReportService.cs` | `ProgressReportEndpointTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | FE gọi prefill API |
| **BE-10** | Deadline Reminder (UC-107) | `DeadlineReminderBackgroundService.cs` | `DeadlineReminderTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | Cấu hình notification staging |
| **BE-11** | Tách Rate Limit Xác thực (ENV-05) | `Program.cs` (RateLimiter policies) | `RateLimiterTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | Đã bypass cho IP test |
| **BE-12** | Refresh Authentication Session (UC-003) | `TokenService.cs`<br>`AuthController.cs` | `AuthEndpointTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | FE token refresh interceptor |
| **BE-13** | Academic AI Assistant (UC-148) | `AcademicKnowledgeService.cs`<br>`AiAssistantController.cs` | `AcademicKnowledgeTests.cs`<br>`AiAssistantEndpointTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | Khải kết nối OpenAI/Gemini API key |
| **BE-14** | Contribution Snapshot & Trend (UC-098/101) | `ContributionTrackingService.cs` | `ContributionAnalyticsTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | Dữ liệu commit/jira thực tế |
| **BE-15** | Phân công Evaluator đa phạm vi (UC-123) | `EvaluationAssignmentService.cs` | `EvaluatorAssignmentEndpointTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | FE2 phân công hội đồng |
| **BE-16** | Công bố điểm chính thức (UC-127) | `DefenseSessionController.cs`<br>`GradePublishingService.cs` | `GradePublishingEndpointTests.cs` | **IMPLEMENTED** *(Đã có endpoint công bố)* | `AUTOMATED VERIFIED` | Hội đồng nhập điểm thật |
| **BE-17** | Update User Account (UC-013) | `UsersController.cs` | `UserAccountEndpointTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | FE trang quản lý tài khoản |
| **BE-18** | Nghiệm thu 14 module v5 trên staging | 14 Controllers v5 & Services | 14 test suites tương ứng (1.373 tests) | **IMPLEMENTED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Chờ Khải deploy và phối hợp FE E2E |
| **BE-19** | Seed bộ môn & ngành Design | `seed_demo_interdisciplinary_ecom.sql` | `SeedDataTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | Chạy migration trên staging |
| **BE-20** | Endpoint /system trả commit SHA | `SystemController.cs` | `SystemEndpointTests.cs`<br>`verify-staging-commit.ps1` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | Staging CI verification script |
| **BE-21** | i18n Notification (UX-03) | `NotificationService.cs`<br>`NotificationDto.cs` | `NotificationEndpointTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | FE i18n dictionary mapping |
| **BE-22** | TeamInvitationDto bổ sung tên nhóm (UX-02) | `TeamInvitationDto.cs`<br>`TeamService.cs` | `TeamInvitationEndpointTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | FE hiển thị invitation card |
| **BE-23** | Ghim Capability ↔ Enforcement qua test | `CapabilityEvaluator.cs` | `CapabilityEnforcementParityTests.cs` | **IMPLEMENTED** | `AUTOMATED VERIFIED` | Không có (Hoàn tất) |

---

## IV. BẰNG CHỨNG KIỂM THỬ THỰC TẾ (TEST EVIDENCE)

1. **Kiểm tra biên dịch Release Mode**:
   ```bash
   dotnet build AIPMS.sln -c Release -warnaserror
   ```
   *Kết quả*: **0 Warning(s), 0 Error(s)**.
2. **Kiểm tra Unit Tests**:
   ```bash
   dotnet test tests/AIPMS.UnitTests/AIPMS.UnitTests.csproj -c Release --no-build
   ```
   *Kết quả*: **Passed! - Failed: 0, Passed: 1.237, Skipped: 0, Total: 1.237, Duration: 6 s**.
3. **Kiểm tra Full Integration Tests (Isolated Local SQL Server Express)**:
   ```bash
   dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj -c Release --no-build
   ```
   *Kết quả*: **Passed! - Failed: 0, Passed: 1.373, Skipped: 0, Total: 1.373, Duration: 38 m 28 s**.
4. **Kiểm tra Database Rò rỉ (Zero Orphaned Databases)**:
   ```sql
   SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'AI_PMS_TEST_%';
   ```
   *Kết quả*: **0 databases remaining** (toàn bộ 1.373 test databases đã được dọn dẹp sạch sẽ).
5. **Tổng hợp tự động hóa**:
   - **2.610 / 2.610 tests PASS (100%)**.

---

## V. KẾ HOẠCH BÀN GIAO & PHÂN CÔNG HÀNH ĐỘNG (ACTION ITEMS)

### 1. Dành cho Tin và Frontend Team
- [ ] Tham khảo tài liệu `docs/handoff/BE_2026-10-31_FE_ERROR_CONTRACT_HANDOFF.md`.
- [ ] Ánh xạ 7 mã lỗi HTTP 409 Progress Report (`REPORT_CYCLE_REQUIRED`, `REPORT_DEADLINE_PASSED`, v.v.) và `ACADEMIC_SCOPE_UNKNOWN` vào thông báo người dùng tương ứng.
- [ ] Sử dụng trường `error.response?.data?.code` để phân loại lỗi, tránh so khớp chuỗi trên `detail`.

### 2. Dành cho Khải (DevOps / Solution Lead)
- [ ] Review PR #139 trên GitHub (`review/aipms-final-integration-acceptance` -> `develop`).
- [ ] Pull commit mới nhất hoặc merge PR #139 vào `develop` sau khi hoàn tất code review.
- [ ] Triển khai bản build mới lên server staging (`api-staging.khaidz.com`) và xác nhận qua endpoint `GET /api/v1/system`.
- [ ] Giữ nguyên chính sách bảo toàn dữ liệu Staging (Non-destructive: NO reset, NO drop database).

### 3. Dành cho Academic Committee / Chủ nhiệm Đồ án
- [ ] Xem xét tài liệu BE-07 và xác nhận quy trình gán GVHD không vi phạm quy chế học thuật D18.
- [ ] Phối hợp thực hiện nghiệm thu chính thức 14 module v5 theo ma trận `docs/handoff/BE18_V5_E2E_ACCEPTANCE_MATRIX.md`.
