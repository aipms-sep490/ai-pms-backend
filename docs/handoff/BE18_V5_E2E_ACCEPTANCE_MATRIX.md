# AI-PMS — BE-18 V5 MODULES (UC-135–UC-148) E2E ACCEPTANCE MATRIX

> **Tài liệu tham chiếu gốc**: `docs/v5_modules_e2e_acceptance_runbook.md`  
> **Phiên bản**: 3.0.0 (31/10/2026)  
> **Nguyên tắc phân định trạng thái**:
> 1. `IMPLEMENTED`: Backend có mã nguồn xử lý nghiệp vụ hoàn chỉnh.
> 2. `AUTOMATED VERIFIED`: Có kiểm thử tự động (Unit / Integration Tests) PASS 100%.
> 3. `STAGING E2E ACCEPTED`: Đã chạy nghiệm thu thực tế trên môi trường staging chung (`api-staging.khaidz.com`) và có bằng chứng log/screenshot được QA/Solution Lead ký xác nhận.

---

## I. MA TRẬN NGHIỆM THU 14 MODULE V5 (UC-135 ĐẾN UC-148)

| UC ID | Tên Module v5 | Backend Implementation File | Automated Test Proof | Trạng thái Code / Test | Staging E2E Result | Reviewer / Sign-off |
| :--- | :--- | :--- | :--- | :---: | :---: | :---: |
| **UC-135** | Review Gates | `ReviewGatesController.cs`<br>`ReviewGateService.cs` | `ReviewGateTests.cs` | **IMPLEMENTED**<br>**AUTOMATED VERIFIED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Tin (FE) / Khải (Lead) |
| **UC-136** | Chat & Attachments | `ChatController.cs`<br>`ChatHub.cs` | `ChatApiTests.cs`<br>`ChatRealtimeTests.cs` | **IMPLEMENTED**<br>**AUTOMATED VERIFIED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Tin (FE) / Khải (Lead) |
| **UC-137** | Appeals & Corrections | `ResultAppealsController.cs`<br>`ResultAppealService.cs` | `ResultAppealEndpointTests.cs`<br>`ResultRevisionEndpointTests.cs` | **IMPLEMENTED**<br>**AUTOMATED VERIFIED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Tin (FE) / Khải (Lead) |
| **UC-138** | Industry Advisory | `IndustryController.cs`<br>`IndustryService.cs` | `IndustryEndpointTests.cs` | **IMPLEMENTED**<br>**AUTOMATED VERIFIED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Tin (FE) / Khải (Lead) |
| **UC-139** | Weekly Assessments | `WeeklyAssessmentsController.cs`<br>`WeeklyAssessmentService.cs` | `WeeklyAssessmentEndpointTests.cs`<br>`WeeklyAssessmentWeeksTests.cs` | **IMPLEMENTED**<br>**AUTOMATED VERIFIED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Tin (FE) / Khải (Lead) |
| **UC-140** | Peer Evaluation | `PeerEvaluationsController.cs`<br>`PeerEvaluationService.cs` | `PeerEvaluationEndpointTests.cs` | **IMPLEMENTED**<br>**AUTOMATED VERIFIED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Tin (FE) / Khải (Lead) |
| **UC-141** | Working Agreement | `WorkingAgreementsController.cs`<br>`WorkingAgreementService.cs` | `WorkingAgreementEndpointTests.cs`<br>`WorkingAgreementActivationTests.cs` | **IMPLEMENTED**<br>**AUTOMATED VERIFIED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Tin (FE) / Khải (Lead) |
| **UC-142** | COLD Submissions & Eval | `ColdSubmissionsController.cs`<br>`ColdEvaluationsController.cs` | `ColdSubmissionEndpointTests.cs`<br>`ColdEvaluationEndpointTests.cs` | **IMPLEMENTED**<br>**AUTOMATED VERIFIED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Tin (FE) / Khải (Lead) |
| **UC-143** | Defense & Sessions | `EvaluationDraftsController.cs`<br>`DefenseSessionService.cs` | `HierarchicalEvaluationTests.cs`<br>`InterdisciplinaryEvaluationTests.cs` | **IMPLEMENTED**<br>**AUTOMATED VERIFIED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Tin (FE) / Khải (Lead) |
| **UC-144** | Sprints & Kanban Board | `SprintsController.cs`<br>`SprintService.cs` | `SprintEndpointTests.cs` | **IMPLEMENTED**<br>**AUTOMATED VERIFIED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Tin (FE) / Khải (Lead) |
| **UC-145** | Meeting Collaboration | `MeetingCollaborationController.cs`<br>`MeetingService.cs` | `MeetingCollaborationEndpointTests.cs` | **IMPLEMENTED**<br>**AUTOMATED VERIFIED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Tin (FE) / Khải (Lead) |
| **UC-146** | Unified Capabilities | `ExecutionCapabilitiesController.cs`<br>`CapabilityEvaluator.cs` | `V5AcademicCapabilityTests.cs`<br>`CapabilityEnforcementParityTests.cs` | **IMPLEMENTED**<br>**AUTOMATED VERIFIED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Tin (FE) / Khải (Lead) |
| **UC-147** | Milestone Templates | `MilestoneTemplatesController.cs`<br>`MilestoneTemplateService.cs` | `MilestoneTemplateActivationSqlTests.cs` | **IMPLEMENTED**<br>**AUTOMATED VERIFIED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Tin (FE) / Khải (Lead) |
| **UC-148** | Academic AI Assistant | `AiAssistantController.cs`<br>`AcademicKnowledgeService.cs` | `AcademicKnowledgeTests.cs`<br>`AiAssistantEndpointTests.cs` | **IMPLEMENTED**<br>**AUTOMATED VERIFIED** | `NOT EXECUTED — PENDING STAGING ACCEPTANCE` | Tin (FE) / Khải (Lead) |

> [!IMPORTANT]
> Toàn bộ 14 module trên đã có mã nguồn hoàn tất và kiểm thử tích hợp tự động PASS 100% trong môi trường test cô lập. Tuy nhiên, theo nguyên tắc minh bạch, cột **Staging E2E Result** được ghi nhận chính xác là **`NOT EXECUTED — PENDING STAGING ACCEPTANCE`** cho tới khi Khải deploy bản build mới nhất lên Staging và cùng team Frontend thực hiện kịch bản theo runbook.

---

## II. KẾ HOẠCH PHỐI HỢP VỚI FRONTEND TEAM & DEVOPS (KHẢI)

### 1. Phân định Namespace dữ liệu học kỳ (Semester Namespaces)
- **Frontend Team 1**: Sử dụng học kỳ có tiền tố `F8-` (ví dụ `F8-FA26-SE`).
- **Frontend Team 2**: Sử dụng học kỳ có tiền tố `G10-` (ví dụ `G10-FA26-BIZ`).
- **Backend / Acceptance Testing đề xuất**: Sử dụng tiền tố riêng `BE-V5-` (ví dụ `BE-V5-FA26`) hoặc `ACCEPT-V5-` khi chạy kiểm thử nghiệm thu trực tiếp trên Staging.
  > *Quy tắc an toàn*: Backend Agent tuyệt đối KHÔNG tự ý tạo dữ liệu học kỳ thật trên database staging khi chưa có lệnh điều phối của Khải.

### 2. Danh mục Tài khoản & Roles kiểm thử (Test Actors)

Tuân thủ chính sách bảo mật Zero Plaintext Password. Mọi tài khoản sử dụng mật khẩu test môi trường staging `<STAGING_TEST_PASSWORD>`.

| Role | Email Staging | Mục đích kiểm thử trong 14 UCs |
| :--- | :--- | :--- |
| **System Admin** | `admin@aipms.test` | UC-147 (Milestone Templates), UC-146 (Global Capabilities) |
| **Department Staff (SE)** | `staff.it@aipms.test` | UC-135 (Review Gates G1-G6), UC-137 (Appeals), UC-143 (Defense Sessions) |
| **Department Staff (Business)** | `staff.business@aipms.test` | UC-138 (Industry Advisory Approval), UC-143 (Hội đồng liên ngành) |
| **Supervisor (Lecturer 1)** | `lecturer1@aipms.test` | UC-139 (Weekly Assessments), UC-142 (COLD Evaluation), UC-145 (Meetings) |
| **Discipline Mentor (Lecturer 2)**| `lecturer2@aipms.test` | UC-138 (Industry/Mentor Reviews), UC-140 (Mentor Feedback) |
| **Student Leader** | `student.leader@aipms.test` | UC-136 (Chat), UC-141 (Working Agreement), UC-144 (Sprints), UC-148 (AI) |
| **Student Member** | `student.member@aipms.test` | UC-140 (Peer Eval), UC-141 (Agreement Consent), UC-145 (Meeting Notes) |

### 3. Trình tự thực thi E2E khuyến nghị (Execution Order)

Để tránh xung đột trạng thái dữ liệu (data race / state lock), các module phải được kiểm thử theo đúng thứ tự logic vòng đời đồ án:

```
[Khởi tạo Học kỳ & Template]
  └─► UC-147 (Milestone Templates) ──► Tạo Project Period
        │
[Thành lập Đội ngũ & Cam kết]
  └─► UC-141 (Working Agreement Consent) ──► UC-138 (Industry Advisory)
        │
[Vận hành Sprint & Tương tác]
  └─► UC-144 (Sprints/Kanban) ──► UC-145 (Meeting Notes Lock) ──► UC-136 (SignalR Chat) ──► UC-148 (Academic AI Q&A)
        │
[Đánh giá định kỳ & Cổng xét duyệt]
  └─► UC-139 (Weekly Assessments) ──► UC-140 (Peer Evaluation) ──► UC-135 (Review Gates G1-G6)
        │
[Nghiệm thu cuối kỳ & Bảo vệ]
  └─► UC-142 (COLD Report Submission & Invariant Grading) ──► UC-143 (Defense Committee Scoring)
        │
[Hậu kiểm & Khiếu nại]
  └─► UC-137 (Grade Appeal & 2-Person Correction) ──► UC-146 (Capabilities Final Audit)
```

### 4. Dependencies cần Khải (DevOps / Lead) thực hiện trên Staging
1. **Deploy HEAD commit**: Triển khai bản build chứa commit `efe45a0` (hoặc commit review mới nhất) lên `https://api-staging.khaidz.com`.
2. **Kiểm tra Commit SHA**: Chạy `curl -s https://api-staging.khaidz.com/api/v1/system | jq .commitSha` xác nhận đúng SHA.
3. **Giữ nguyên Non-destructive Test Policy**: Tuyệt đối không reset hoặc drop database `AI_PMS` đang chạy.
4. **Cấu hình SMTP & Cloudflare Tunnel**: Đảm bảo tunnel ổn định, không ngắt kết nối WebSocket SignalR `/hubs/chat` (UC-136).

### 5. Checklist Smoke Test & Sẵn sàng Nghiệm thu
- [x] Backend Source hoàn tất cho cả 14 modules.
- [x] Automated Integration Tests đạt 1.373/1.373 PASS.
- [x] Schema và Migration scripts đồng bộ hoàn toàn (64 migrations).
- [ ] Staging deployed commit mới nhất (Chờ Khải thực hiện).
- [ ] FE kết nối và xác nhận qua 14 kịch bản UI (Chờ Tin & FE team phối hợp).
- [ ] Báo cáo nghiệm thu chính thức có chữ ký phê duyệt của QA/Lead.
