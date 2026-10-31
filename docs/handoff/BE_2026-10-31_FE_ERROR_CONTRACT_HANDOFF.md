# AI-PMS — FRONTEND ERROR CONTRACT & API HANDOFF (31/10/2026)

> **Mục đích**: Bàn giao hợp đồng mã lỗi HTTP 409 cho Frontend Team (Tin & FE devs), đối chiếu với các yêu cầu trong `BE_NHIEM_VU_2026-10-31.md`.  
> **Trạng thái**: Hợp đồng mã lỗi Backend đã được chứng minh 100% bằng integration tests gửi HTTP request thực tế qua WebApplicationFactory.

---

## I. BẢY PROGRESS REPORT HTTP 409 ERROR CODES

Trước ngày 31/10/2026, Frontend chưa mapping riêng 5 mã lỗi Progress Report do thiếu response evidence. Hiện tại Backend đã bổ sung đầy đủ HTTP response integration tests cho toàn bộ 7 mã lỗi nghiệp vụ của Progress Report.

### Bảng đối chiếu chi tiết 7 mã lỗi HTTP 409

| STT | Error Code (`problem.code`) | Endpoint gọi | HTTP Status | Integration Test Method (Proof) | Suggested FE Handling / User Notification | Trạng thái kiểm chứng |
| :--- | :--- | :--- | :---: | :--- | :--- | :---: |
| 1 | `REPORT_CYCLE_REQUIRED` | `POST /api/v1/progress-reports/{id}/submit` | `409 Conflict` | `Submit_ProgressReportWithoutReportingCycle_WhenPolicyRequiresCycle_Returns409Conflict_WithReportCycleRequired`<br>*(ProgressReportEndpointTests.cs:727)* | "Học kỳ yêu cầu báo cáo tiến độ phải gắn với chu kỳ báo cáo (reporting cycle). Vui lòng chọn chu kỳ trước khi nộp." | **AUTOMATED VERIFIED** *(Staging unverified)* |
| 2 | `REPORT_DEADLINE_PASSED` | `POST /api/v1/progress-reports/{id}/submit` | `409 Conflict` | `Submit_AfterReportingCycleDeadline_WhenLatePolicyBlock_Returns409Conflict_WithReportDeadlinePassed`<br>*(ProgressReportEndpointTests.cs:768)* | "Hạn chót nộp báo cáo cho chu kỳ này đã kết thúc và chính sách học kỳ không cho phép nộp muộn." | **AUTOMATED VERIFIED** *(Staging unverified)* |
| 3 | `REPORT_ALREADY_EXISTS_FOR_CYCLE` | `POST /api/v1/projects/{projectId}/progress-reports` | `409 Conflict` | `Create_DuplicateReportForSameReportingCycle_Returns409Conflict_WithReportAlreadyExistsForCycle`<br>*(ProgressReportEndpointTests.cs:826)* | "Nhóm đã có báo cáo tiến độ cho chu kỳ này. Không thể tạo báo cáo trùng lặp." | **AUTOMATED VERIFIED** *(Staging unverified)* |
| 4 | `REPORT_ALREADY_EXISTS_FOR_PERIOD` | `POST /api/v1/projects/{projectId}/progress-reports` | `409 Conflict` | `Create_DuplicateReportForSamePeriodWithoutCycle_Returns409Conflict_WithReportAlreadyExistsForPeriod`<br>*(ProgressReportEndpointTests.cs:896)* | "Đã tồn tại báo cáo tiến độ trong khoảng thời gian này của dự án." | **AUTOMATED VERIFIED** *(Staging unverified)* |
| 5 | `REPORT_FEEDBACK_ON_DRAFT_NOT_ALLOWED` | `POST /api/v1/progress-reports/{id}/feedback` | `409 Conflict` | `AddFeedback_OnDraftProgressReport_Returns409Conflict_WithReportFeedbackOnDraftNotAllowed`<br>*(ProgressReportEndpointTests.cs:943)* | "Báo cáo đang ở trạng thái Nháp (DRAFT). Giảng viên chỉ có thể gửi nhận xét khi nhóm đã nộp báo cáo chính thức." | **AUTOMATED VERIFIED** *(Staging unverified)* |
| 6 | `REPORT_ALREADY_SUBMITTED` | `POST /api/v1/progress-reports/{id}/submit` | `409 Conflict` | `Report_DraftSubmittedReviewedFlow_FullLifecycle`<br>*(ProgressReportEndpointTests.cs:263)* | "Báo cáo này đã được nộp trước đó. Không thể nộp lại." | **AUTOMATED VERIFIED** *(Staging unverified)* |
| 7 | `REPORT_NOT_EDITABLE` | `PUT /api/v1/progress-reports/{id}` | `409 Conflict` | `Report_DraftSubmittedReviewedFlow_FullLifecycle`<br>*(ProgressReportEndpointTests.cs:269)* | "Báo cáo đã nộp hoặc đang trong quá trình đánh giá, không thể chỉnh sửa nội dung." | **AUTOMATED VERIFIED** *(Staging unverified)* |

---

## II. CHUẨN HÓA `ACADEMIC_SCOPE_UNKNOWN`

### 1. Phạm vi ảnh hưởng và các Endpoints đã xác minh

Lỗi `ACADEMIC_SCOPE_UNKNOWN` xảy ra khi hệ thống không xác định được cấu hình học thuật đóng băng (frozen academic snapshot / scope) của dự án trong học kỳ hiện tại.

Toàn bộ Backend đã được chuẩn hóa để trả về mã máy `ACADEMIC_SCOPE_UNKNOWN` trong thuộc tính `code` của RFC 7807 `ProblemDetails` thay vì đưa string vào `detail` hay `message`.

Các endpoints và workflows đã được xác minh qua integration tests gửi HTTP request thực tế:

1. `POST /api/v1/projects/{projectId}/supervisor-recommendations`
   - Test: `Recommendations_without_frozen_academic_scope_returns_409_with_AcademicScopeUnknown` *(SupervisorRecommendationEndpointTests.cs:198)*
2. `POST /api/v1/projects/{projectId}/peer-evaluations`
   - Test: `Peer_without_frozen_academic_scope_returns_409_with_AcademicScopeUnknown` *(PeerEvaluationEndpointTests.cs:152)*
3. Các dịch vụ lõi:
   - `SupervisorAssignmentWorkflow.cs` (gán GVHD)
   - `ProjectRepository.Hybrid.cs` (đóng băng snapshot đề tài)
   - `ResultAppealService.cs` (khiếu nại kết quả)

### 2. Schema HTTP Response thực tế từ ExceptionHandlingMiddleware

Backend trả về RFC 7807 Problem Details chuẩn với trường `code` ở root level:

```http
HTTP/1.1 409 Conflict
Content-Type: application/problem+json; charset=utf-8

{
  "type": "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.8",
  "title": "Conflict",
  "status": 409,
  "detail": "Academic scope configuration is missing or incomplete for the current semester.",
  "instance": "/api/v1/projects/542/supervisor-recommendations",
  "code": "ACADEMIC_SCOPE_UNKNOWN"
}
```

---

## III. KHUYẾN NGHỊ TÍCH HỢP CHO FRONTEND TEAM (TIN & DEVS)

1. **Bắt mã lỗi dựa trên `problem.code`**:
   - FE Axios / Fetch interceptor hoặc Service layer cần đọc trường `response.data.code`.
   - **Tuyệt đối không** dùng regex hoặc so khớp chuỗi `response.data.detail` (ví dụ: không `detail.includes("kết nối")` hay `detail.includes("scope")`), vì text mô tả có thể thay đổi hoặc được bản địa hóa.
2. **Loại bỏ generic fallbacks**:
   - Trước đây, FE để thông báo chung chung ("Đã xảy ra lỗi không xác định") khi gặp 5 mã Progress Report mới và lỗi Academic Scope.
   - Nay FE có thể ánh xạ trực tiếp `switch (error.response?.data?.code)` theo bảng ở Mục I và Mục II để hiển thị toast notification hoặc form banner chính xác cho người dùng.
3. **Kế hoạch xác nhận trên Staging**:
   - Các mã lỗi trên đã PASS 100% ở môi trường Automated Integration Tests trên Backend.
   - Khi commit được Khải deploy lên staging (`https://api-staging.khaidz.com`), FE cần chạy smoke test kịch bản nộp trùng chu kỳ và nộp báo cáo nháp để nghiệm thu trải nghiệm người dùng thực tế.
