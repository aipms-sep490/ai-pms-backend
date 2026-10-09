# AI-PMS — Rà soát hiện trạng & Kế hoạch hoàn thiện (FE lead, giao việc BE, DB)

Ngày lập: 09/10/2026 · Người lập: Claude (vai trò Senior Frontend / UI-UX lead) · Người yêu cầu: Vo Van

> Chuẩn tham chiếu: **CIB v4.0 (08/10/2026)** > SRS/SDD > Governance v3.0 / Workflow v2. Mọi quyết định D1–D16 vẫn **OPEN**, nên kế hoạch chỉ xây khung cấu hình được (feature flag/config) cho phần chưa chốt.

---

## 0. Tóm tắt điều hành

| Hạng mục | Kết luận ngắn |
|---|---|
| Mức hoàn thiện | Luồng P0 (đội nhóm → đăng ký → duyệt → GVHD → thực thi → bàn giao) đã có ở cả BE và FE. **Luồng chấm điểm → công bố kết quả chưa từng chạy hết trên DB thật** (các bảng scheme/assignment/result đều 0 dòng). |
| Thiếu lớn nhất (P0) | (1) Bảng `contribution_snapshots` có trong script & code nhưng **không tồn tại trên DB** → API đóng góp sẽ lỗi 500. (2) 4 lỗi phân quyền BE còn mở (Admin publish kết quả liên khoa, governance cho Admin, calendar quá rộng, mentor được coi là manager). (3) Chưa có dữ liệu seed cho scheme/rubric/assignment để demo A/B/C. |
| Thiếu theo tài liệu (P1) | Checkpoint/Review Gate (G1–G6), duyệt minh chứng có lịch sử, hội đồng bảo vệ, Working Agreement, lưu trữ kết quả AI. |
| Thiếu theo tài liệu (P2) | Gợi ý GVHD bằng AI (UC-054/116), Industry Expert, peer evaluation, phúc khảo/đính chính kết quả. |
| UI/UX | Nền tảng tốt (React 19 + Tailwind 4, có design-system MASTER.md) nhưng **token lệch tài liệu**, **255 chỗ hard-code màu primary**, **84 class màu Tailwind thô**, chữ 10–11px dùng >100 lần, **chỉ 33/66 trang dùng khung `WorkspacePage`**, thiếu primitive Table/Tabs/Form/Toast/EmptyState, **2 nguồn route song song**. |
| FE chưa nối API có sẵn | Import chương trình đào tạo (`/users/curriculum-import`), xuất danh sách nhóm (`/teams/export`). |
| Bảo mật | Chuỗi kết nối dùng tài khoản `sa` và mật khẩu đã được dán trong chat → **cần đổi mật khẩu `sa` ngay** và tạo user DB riêng quyền tối thiểu cho ứng dụng. File này không chứa mật khẩu. |

---

## 1. Phạm vi & nguồn đã rà

| Nguồn | Phiên bản rà soát |
|---|---|
| Frontend `ai-pms-frontend` | `develop` @ `3426d70` (merge PR #52, CIB v4 FE1) |
| Backend `ai-pms-backend` | `develop` @ `4c61c98` (merge PR #107 roster export, #106 curriculum import) |
| Database `AI_PMS` | Kết nối đọc-chỉ lúc 09/10/2026 từ máy LAPTOP-2AU0UMVF: 104 bảng, 267 khóa ngoại, 4 role, 21 user, 4 project, 5 kỳ đồ án |
| Tài liệu | CIB v4.0, Governance v3.0, SRS (Report 3, UC-001…UC-134), SDD (Report 4), Báo cáo quy trình Workflow v2 |
| Tài liệu nội bộ FE đã có | `docs/FINAL_FE_BE_SYNC_AUDIT.md`, `docs/CIB_V4_FE1_IMPLEMENTATION_REPORT_2026-10-09.md`, `docs/AI-PMS_Backend_Developer_Handoff_v1.0.md`, `design-system/ai-pms/MASTER.md` |

Chỉ thực hiện truy vấn đọc (`sys.tables`, `sys.columns`, đếm dòng). Không ghi gì vào DB.

---

## 2. Hệ thống đang thiếu gì (ma trận khoảng trống)

Ký hiệu: ✅ có và dùng được · 🟡 có một phần / chưa nghiệm thu · ❌ chưa có

| # | Chức năng (nguồn) | BE | FE | DB | Ưu tiên | Cách hoàn thiện |
|---|---|---|---|---|---|---|
| G1 | Đóng góp thành viên (UC-097…103, BR-110–112) | ✅ code | ✅ trang `/project/contributions` | ❌ thiếu bảng `contribution_snapshots` | **P0** | Chạy `db/changes/20260916_add_contribution_snapshots.sql` trên DB dùng chung; thêm bước kiểm tra drift vào CI (so `db/e2e/migrations.json` với `sys.tables`). |
| G2 | Chấm điểm theo scheme + công bố StudentResult/ProjectResult (BR-59, BRX-EVAL/RESULT, UC-123…127) | 🟡 có API, chưa chạy E2E | 🟡 có màn hình, mới test bằng fixture | 🟡 bảng đủ nhưng 0 dòng | **P0** | BE seed rubric/scheme/assignment cho 3 kịch bản demo; golden-test công thức; FE nghiệm thu trên runtime thật. |
| G3 | Ai được công bố kết quả liên khoa (CIB §3: Admin không có quyền học thuật) | ❌ code đang bắt **Admin** công bố (`EvaluationSchemeService.Results.cs:36-43`) | 🟡 | – | **P0 (cần quyết định)** | Khuyến nghị: Lead Department công bố sau khi đủ quyết định của các khoa tham gia. BE đổi guard; FE đổi nhãn/CTA. |
| G4 | Lỗi phân quyền BE-AW-001/003/005/011 (tài liệu FE `FINAL_FE_BE_SYNC_AUDIT.md`) | ❌ vẫn còn (`ProjectGovernanceService.cs:31` vẫn có nhánh Admin) | FE đang chặn tạm (fail-closed) | – | **P0** | Xem BE-03…BE-06 ở mục 6. |
| G5 | Lọc task theo ngành có phân trang (CIB §4.1) | ❌ `GET /tasks/project/{id}` chưa có `majorId` | 🟡 chờ BE | ✅ `task_disciplines` | P0 | BE-07. |
| G6 | Ma trận quyền (UC-015/016) | 🟡 có `PermissionsController` | 🟡 trang `/admin/access/rbac` | ❌ `permissions`, `role_permissions` = 0 dòng | P1 | Chốt: hoặc seed danh mục permission, hoặc bỏ màn RBAC khỏi MVP. Khuyến nghị seed. |
| G7 | Mẫu mốc đồ án (UC-030) | ✅ | ✅ `/admin/milestone-templates` | 🟡 0 dòng | P1 | Seed mẫu cho IT / Marketing / Liên ngành. Chuyển quyền quản lý mẫu sang Department (SRS: actor là Department Staff), Admin chỉ xem. |
| G8 | Checkpoint & Review Gate G1–G6 (CIB §5, BRX-GATE-01/02, D2) | ❌ | ❌ | ❌ | P1 | Thêm bảng (mục 4.3), API, màn hình gate cho Supervisor/Mentor và Student. |
| G9 | Duyệt minh chứng có lịch sử (CIB §4.1 bước 12) | 🟡 chỉ có `verification_status` | 🟡 sổ minh chứng chỉ đọc/tạo | 🟡 | P1 | Thêm `evidence_reviews` append-only. |
| G10 | Hội đồng/đợt bảo vệ (EvaluationStage DEFENSE, CIB §6.2 "3 GV + 1 DN") | ❌ | ❌ | ❌ | P1 | Thêm `evaluation_committees`, `committee_members`, `defense_sessions`. |
| G11 | Working Agreement trước khi ACTIVE (D1) | ❌ | ❌ | ❌ | P1 (bật bằng cấu hình) | Thêm `project_working_agreements` + `agreement_acceptances`. |
| G12 | Phân tích rủi ro AI được lưu lại, xem lại, giải thích (UC-112…119) | 🟡 rule-based, tính lại mỗi lần | 🟡 sau feature flag | ❌ không có bảng | P1 | Thêm `ai_analysis_runs` + `ai_risk_factors`. |
| G13 | Gợi ý GVHD bằng AI (UC-053/054/116, BR-60–63) | ❌ không có code | ❌ | ❌ | P2 | Thêm `supervisor_recommendation_runs/items`; xếp hạng sau khi lọc capacity; AI không được tự gán. |
| G14 | Industry Expert (CIB §6.2, D6) | ❌ | ❌ | ❌ role | P2 | Role `INDUSTRY_EXPERT` + `industry_expert_profiles`; mặc định ADVISORY. |
| G15 | Peer evaluation (D7) | ❌ | ❌ | ❌ | P2 | `peer_evaluations` chỉ làm evidence, không tự cộng điểm. |
| G16 | Phúc khảo / đính chính kết quả (BRX-RESULT-04, D16) | ❌ | ❌ | ❌ | P2 | `result_correction_requests` + version kết quả. |
| G17 | Import chương trình đào tạo, xuất danh sách nhóm (UC-012, UC-132) | ✅ (PR #106, #107) | ✅ đã nối trong PR FE đợt 1 | ✅ `users.curriculum_code` | **P0 FE** | FE-08. |
| G18 | Bình luận task (UC-072) | ✅ `TaskCommentsController` | 🟡 có component, bảng 0 dòng | ✅ | P1 | Nghiệm thu; kiểm tra luồng tạo bình luận trên runtime. |
| G19 | Xuất báo cáo PDF/Excel cho khoa (UC-132) | 🟡 chỉ có `dashboards/portfolio/export` | 🟡 | – | P1 | Chuẩn hóa export + audit (BR-150–152). |

---

## 3. Rà soát tuần tự từng Role: chức năng & luồng

DB hiện có 4 **role hệ thống**: `ADMIN` (2 user), `DEPARTMENT_STAFF` (3), `LECTURER` (4), `STUDENT` (13). Các vai trò nghiệp vụ còn lại **không phải role** mà là **assignment** (đúng theo CIB §3.1): Primary Supervisor, Discipline Mentor, Evaluator là LECTURER có assignment; Lead/Participating Department suy ra từ phạm vi học vụ đã đóng băng của project; Student Leader là `team_members.is_leader`.

Quy ước: **[Có]** đã chạy được · **[Thiếu]** cần làm · route FE trong ngoặc.

### 3.1 System Administrator (`ADMIN`)
Giới hạn cứng: quản trị kỹ thuật, **không** duyệt học thuật, **không** sửa/công bố điểm.

1. Đăng nhập → Quản trị nền tảng (`/admin/access`). **[Có]**
2. Quản lý user: tạo, khóa/mở, gán role, sửa hồ sơ học vụ có concurrency token (`/admin/access/users/:id`). **[Có]**
3. Import user/chương trình đào tạo từ Excel: preview → commit. **[Thiếu FE]** (BE đã có `POST /users/curriculum-import/preview|commit`).
4. Cấu trúc tổ chức: Organization → Department → Major (`/academic`). **[Có]**
5. Ma trận quyền (`/admin/access/rbac`). **[Thiếu dữ liệu]** bảng permission rỗng.
6. Xem audit log (UC-133). **[Có BE `/security/audit-logs`, cần màn hình chuyên dụng có lọc]**
7. Xuất danh sách nhóm. **[Thiếu FE]** (`GET /teams/export`).
8. Sai lệch cần sửa: Admin đang (a) quản lý mẫu mốc đồ án, (b) là người công bố kết quả liên khoa, (c) có quyền governance project. Cả ba trái CIB §3 → chuyển về Department (BE-03, BE-04, FE-09).

### 3.2 Department Staff — Lead Department (`DEPARTMENT_STAFF`, khoa chủ trì)
1. Thiết lập học kỳ & kỳ đồ án (REGISTRATION → PROJECT_REVIEW → EXECUTION → FINAL_SUBMISSION) (`/academic/governance`). **[Có]** DB có 5 kỳ SEP490 FA26/SP27.
2. Công bố policy kỳ (mode cho phép, kích thước nhóm, số ngành tối thiểu, quota GVHD) (`/academic/project-periods/:id/policy`). **[Có]** — 2 policy version trên DB.
3. Quản lý đề tài (`/department/topics`), rubric có version (`/academic/rubrics`). **[Có]**
4. Xác minh hồ sơ học vụ & chứng chỉ sinh viên (`/academic/profile-verifications`, `/department/student-qualifications`). **[Có]**
5. Thẩm định đề cương: SUBMITTED → UNDER_REVIEW → REVISION_REQUIRED / APPROVED / REJECTED, có lý do (`/department/projects/review`). **[Có]**; liên ngành phải chờ đủ quyết định các khoa tham gia (BRX-REVIEW-01). **[Cần nghiệm thu: `project_department_decisions` = 0 dòng]**
6. Giám sát GVHD, capacity (`/department/supervisors`). **[Có]**; gợi ý AI **[Thiếu, P2]**.
7. Cấu hình scheme đánh giá, phân công evaluator theo phạm vi COMMON/MAJOR/INDIVIDUAL (`/department/projects/:id/evaluation-schemes`, `/evaluators`). **[Có màn, chưa có dữ liệu]**
8. Xem preview kết quả → công bố (`/department/projects/:id/result`). **[Có màn; guard BE đang sai chủ thể]**
9. Lưu trữ đồ án (`/department/projects/archived`). **[Có]**
10. Rủi ro dự án (`/department/projects/:id/risk`, sau flag AI). **[Một phần]**
11. **[Thiếu]** cấu hình gate/checkpoint theo kỳ; lập hội đồng bảo vệ; xuất báo cáo khoa.

### 3.3 Department Staff — Participating Department (khoa tham gia)
1. Thấy project liên ngành có ngành của khoa mình trong phạm vi đóng băng.
2. Ra quyết định cho phần trách nhiệm ngành mình (APPROVE / REQUEST_REVISION), **không** duyệt toàn project. **[Có API, chưa có dữ liệu]**
3. Đề cử mentor/evaluator cho ngành mình. **[Một phần]** — hiện cùng màn với Lead; cần hiển thị rõ "Bạn là khoa tham gia" và ẩn CTA duyệt cuối (FE-10).

### 3.4 Lecturer — Primary Supervisor
1. Hồ sơ GV, chuyên môn, capacity (`/supervisor/profile`). **[Có]**
2. Nhận/từ chối yêu cầu hướng dẫn → khi chấp nhận, BE kiểm capacity, chuyển project ACTIVE, khởi tạo milestone một lần (`/supervisor/workspace`). **[Có]**
3. Theo dõi project: tổng quan, task, gantt, báo cáo tiến độ + phản hồi, họp + biên bản + action item, deliverable, tệp, minh chứng, đóng góp, xem gói bàn giao (`/supervisor/projects/:id/*`). **[Có]**
4. **[Thiếu]** duyệt checkpoint/gate, duyệt minh chứng có lý do, xem lịch sử rủi ro đã lưu.
5. Chỉ chấm khi có EvaluationAssignment rõ ràng. **[Đúng]**

### 3.5 Lecturer — Discipline Mentor (liên ngành)
1. Danh sách project theo ngành được giao (`/mentor/workspace`). **[Có]**
2. Không gian theo `projectId + majorId`: task, báo cáo, họp, minh chứng của ngành mình (`/mentor/projects/:p/majors/:m/*`). **[Có]**
3. **[Lỗi BE]** BE đang coi mentor là "manager" giống Primary (BE-AW-001/003) → FE đang tự ẩn CTA cấu trúc; BE phải sửa.
4. **[Thiếu]** review gate chuyên ngành, phản hồi minh chứng có trạng thái.

### 3.6 Lecturer — Evaluator (giảng viên chấm / hội đồng)
1. Danh sách phân công (`/evaluator/workspace`, `/evaluator/evaluations`). **[Có]**
2. Mở phân công → xem gói bàn giao đã khóa → nhập điểm theo tiêu chí trong phạm vi → lưu DRAFT → FINALIZE (idempotent, concurrency token). **[Có màn, chưa có dữ liệu thật]**
3. Thiếu điểm = PENDING, không bao giờ = 0 (BRX-EVAL-04). FE cần hiển thị rõ.
4. **[Thiếu]** xem minh chứng theo phạm vi phân công (BE-AW-007 mới trả metadata), lịch bảo vệ.

### 3.7 Industry Expert — **[Chưa có, P2]**
Cần role/identity riêng, cổng xem gói bàn giao trong phạm vi, phản hồi ADVISORY; chỉ có trọng số khi scheme đã công bố cho phép.

### 3.8 Student Leader
1. Chọn kỳ → tạo nhóm, chọn mode SINGLE_MAJOR / INTERDISCIPLINARY (`/team/create`). **[Có]**
2. Mời thành viên, hủy lời mời, chuyển trưởng nhóm (qua yêu cầu có mentor duyệt) (`/team`). **[Có]**
3. Nộp chứng chỉ của bản thân → chờ khoa xác minh. **[Có]**
4. Kiểm tra eligibility (quota từng ngành) → khóa nhóm. **[Có API; `team_eligibility_checks` = 0 dòng → cần nghiệm thu]**
5. Chọn nguồn đề tài (đề tài khoa / tự đề xuất) → điền đề cương → validate → nộp (snapshot) (`/project/source`, `/project/register`). **[Có]**
6. Theo dõi thẩm định, sửa theo yêu cầu (`/project/status`, `/project/edit`). **[Có]**
7. Gửi yêu cầu GVHD (`/project/supervisor`). **[Có]**
8. ACTIVE: lập milestone/task, gán người, báo cáo tiến độ, họp, deliverable, minh chứng (`/project/*`). **[Có]**
9. Bàn giao cuối: chọn version deliverable → khóa gói (`/project/final-submission`). **[Có; DB 0 dòng]**
10. Xem kết quả của mình (`/project/result`). **[Có]**
11. **[Thiếu]** ký Working Agreement, nộp evidence cho gate, xem trạng thái gate.

### 3.9 Student Member
1. Nhận/từ chối lời mời; rời nhóm có kiểm soát. **[Có]**
2. Làm task được giao, cập nhật trạng thái, báo BLOCKED, nộp minh chứng, bình luận. **[Có; bình luận cần nghiệm thu]**
3. Tham gia họp, đóng góp báo cáo. **[Có]**
4. Xem kết quả cá nhân sau công bố. **[Có]**
5. **[Thiếu, P2]** peer input.

### 3.10 System / AI
- Có: phân tích tiến độ rule-based, tóm tắt báo cáo dạng template "grounded", hỏi đáp trong phạm vi project, thông báo + email outbox, nhắc hạn định kỳ.
- Thiếu: lưu kết quả phân tích để xem lại và giải thích (UC-118), dự báo trễ hạn có đánh giá (UC-114), gợi ý GVHD (UC-054/116), ngưỡng đủ dữ liệu (D12). AI không được đổi trạng thái hay gán người (BRX-AI-01).

---

## 4. Phân tích Database `AI_PMS`

### 4.1 Tổng quan
- 104 bảng (`dbo`), 267 FK; mọi bảng nghiệp vụ đều có khóa ngoại (chỉ `sysdiagrams` không có).
- **Mọi bảng trên DB đều được BE map** (`ToTable(...)`), không có bảng "mồ côi" trong code.
- **Drift:** BE map `contribution_snapshots` nhưng bảng **không có trên DB** (script `20260916_add_contribution_snapshots.sql` chưa được chạy). Các script khác trong `db/changes` (tới `20261009_add_student_curriculum_code.sql`) đã có mặt.

### 4.2 Phân loại bảng

**A. Đang dùng, có dữ liệu thật (dùng được ngay)**
`users`, `roles`, `user_roles`, `organizations`, `departments`, `majors`, `academic_semesters`, `project_periods`, `period_policy_versions`, `teams`, `team_members`, `team_invitations`, `team_academic_configurations`, `team_major_requirements`, `projects`, `project_majors`, `project_major_requirements`, `project_topics`, `project_registration_snapshots`, `project_status_history`, `project_tags`, `tags`, `supervisor_profiles`, `supervisor_expertise`, `supervisor_requests`, `supervisor_assignments`, `supervisor_feedback`, `milestones`, `tasks`, `task_assignees`, `task_dependencies`, `task_status_history`, `progress_reports`, `deliverables`, `deliverable_versions`, `files`, `meetings`, `meeting_participants`, `meeting_action_items`, `meeting_video_*` (3 bảng), `video_provider_*` (2), `chat_*` (5), `notifications`, `notification_recipients`, `notification_email_deliveries`, `scheduled_notification_occurrences`, `rubrics`, `rubric_versions`, `rubric_criteria`, `evaluation_criteria`, `evaluations`, `evaluation_details`, `academic_profile_verifications`, `audit_logs`, `refresh_tokens`, `external_login_challenges`.

**B. Có schema + code nhưng 0 dòng → chưa được dùng/nghiệm thu (rủi ro demo)**

| Nhóm | Bảng | Ý nghĩa |
|---|---|---|
| Chấm điểm mới (CIB v4) | `evaluation_schemes`, `evaluation_scheme_components`, `evaluation_assignments`, `evaluation_draft_states`, `evaluation_finalizations`, `student_results`, `student_result_evaluations` | Luồng P0 cốt lõi chưa chạy lần nào. |
| Kết quả kiểu cũ | `project_result_policies`, `project_result_policy_items`, `project_results`, `project_result_evaluations` | Song song với scheme mới → cần chốt 1 mô hình (mục 4.4). |
| Bàn giao cuối | `final_submissions`, `final_submission_items`, `final_submission_drafts`, `final_submission_draft_items`, `final_submission_requirements`, `final_submission_requirement_items` | Chưa có nhóm nào khóa gói. |
| Liên ngành | `project_department_decisions`, `team_major_responsibilities`, `topic_major_requirements`, `task_disciplines`, `project_evidence`, `team_eligibility_checks`, `team_eligibility_issues` | Luồng liên ngành chưa đi hết. |
| Điều hành | `progress_report_periods`, `project_action_items`, `meeting_decisions`, `task_comments` | Chưa có dữ liệu. |
| Cấu hình | `milestone_templates`, `milestone_template_versions`, `milestone_template_items`, `project_milestone_template_applications`, `project_period_qualification_policies`, `period_policy_usages`, `student_qualifications` | Chưa seed. |
| Bảo mật | `permissions`, `role_permissions`, `password_reset_tokens`, `password_recovery_requests`, `team_leader_change_requests`, `user_external_logins` | `permissions` rỗng là vấn đề; các bảng khác rỗng là bình thường. |

**C. Thiếu trên DB (đã có script)**: `contribution_snapshots` → chạy script ngay (BE-01).

### 4.3 Bảng cần thêm và vì sao

| Ưu tiên | Bảng đề xuất | Cột chính | Vì sao phải thêm |
|---|---|---|---|
| P1 | `project_checkpoints` | id, project_id, policy_version_id, gate_code (G1…G6), is_required, due_at, status (PLANNED/IN_REVIEW/PASSED/REVISION_REQUIRED/MISSED), concurrency_token | CIB §5 & §10 liệt kê `project_checkpoints / gate_reviews`; BRX-GATE-01/02. Không thể biểu diễn gate bằng project state (CIB Hình 3 cấm). |
| P1 | `gate_reviews` | id, checkpoint_id, reviewer_id, major_id?, decision, reason, decided_at | Quyết định review phải append-only, có người duyệt và lý do. |
| P1 | `gate_review_evidence` | gate_review_id, evidence_id | Gate bắt buộc không được PASS nếu thiếu evidence (BRX-GATE-01). |
| P1 | `evidence_reviews` | id, evidence_id, reviewer_id, decision (ACCEPTED/NEEDS_WORK/REJECTED), comment, reviewed_at | CIB §4.1 bước 12: "không tự chấp nhận chỉ vì có file". Hiện `project_evidence.verification_status` chỉ giữ trạng thái cuối, mất lịch sử. |
| P1 | `evaluation_committees`, `committee_members`, `defense_sessions` | hội đồng theo kỳ/khoa; thành viên + vai trò (CHAIR/SECRETARY/MEMBER/INDUSTRY); phiên bảo vệ (project, thời gian, phòng/online) | EvaluationStage = DEFENSE và mô hình "3 GV + 1 DN" (CIB §6.2) chưa có chỗ lưu; `evaluation_assignments` chỉ lưu từng người lẻ. |
| P1 | `project_working_agreements`, `agreement_acceptances` | phiên bản thỏa thuận nhóm; mỗi thành viên xác nhận | D1 + Governance M4. Bật/tắt theo policy kỳ. |
| P1 | `ai_analysis_runs`, `ai_risk_factors` | run: project_id, kind (RISK/SUMMARY/CONTRIBUTION), model/rule_version, input_hash, sufficiency, status, created_at; factor: run_id, code, weight, evidence_ref | UC-112…119 yêu cầu **xem lại** kết quả AI đã lưu và giải thích; BR-130–137 cần truy vết (grounded + citation). Hiện mỗi lần mở trang lại tính mới, không audit được. |
| P2 | `supervisor_recommendation_runs`, `supervisor_recommendation_items` | run theo project; item: supervisor_profile_id, rank, score, lý do, capacity_snapshot | UC-054/116, BR-60–63: lọc capacity trước, AI chỉ xếp hạng, lưu để giải trình. |
| P2 | `industry_expert_profiles` (+ role `INDUSTRY_EXPERT`) | user_id, company, position, verified_by | D6/CIB §6.2. Không nên dùng LECTURER cho chuyên gia doanh nghiệp. |
| P2 | `peer_evaluations` | project_id, rater_id, ratee_id, round, payload_json, submitted_at | D7: chỉ là evidence, không tự cộng điểm. |
| P2 | `result_correction_requests` | result_type, result_id, requested_by, reason, status, approved_by, new_result_id | BRX-RESULT-04 / D16: đính chính tạo version mới, không ghi đè. |

**Không cần thêm** `discipline_mentor_assignments` như CIB §10 gợi ý: `supervisor_assignments.assignment_type` + `major_id` đã biểu diễn được mentor theo ngành. Tương tự, `evaluation_scores` đã có tương đương `evaluation_details`.

### 4.4 Nợ kỹ thuật trên DB (nên dọn)
1. **Hai mô hình kết quả song song**: `project_result_policies/*` + `project_results` (cũ) và `evaluation_schemes/*` + `student_results` (CIB v4). Chọn scheme làm chuẩn; mô hình cũ chỉ đọc (LEGACY).
2. **Trùng dữ liệu**: `meetings.decisions` (cột) và bảng `meeting_decisions`; `meeting_action_items` và `project_action_items` (source_type=MEETING); `progress_reports.issues_and_risks` và `risks`/`blockers`. Chốt một nguồn, cột còn lại đánh dấu deprecated.
3. `refresh_tokens` đã 384 dòng cho 21 user → cần job dọn token hết hạn.
4. `sysdiagrams` là bảng của SSMS, loại khỏi schema chuẩn.
5. Không phân biệt Trưởng bộ môn và Nhân viên khoa (chỉ có `DEPARTMENT_STAFF`). Nếu quy chế yêu cầu Trưởng bộ môn ký duyệt công bố, cần thêm cờ/role.
6. Ứng dụng đang dùng `sa`. Tạo login riêng (`aipms_app`) chỉ có quyền DML trên `dbo`, login migration riêng.

---

## 5. Kế hoạch UI/UX đồng bộ (phần FE — tôi đảm nhận)

### 5.1 Hiện trạng đo được
| Vấn đề | Số liệu | Hệ quả |
|---|---|---|
| Token lệch tài liệu | `MASTER.md` ghi primary `#1E3A5F` (xanh navy); `src/index.css` dùng `#0f5b4e` (xanh lục) | Designer/dev làm theo 2 chuẩn khác nhau. |
| Hard-code màu | `#0f5b4e` xuất hiện 255 lần, `#596863` 79 lần, `#14201d` 72 lần trong tsx/css | Đổi thương hiệu/dark mode gần như không thể. |
| Class màu Tailwind thô | 84 tổ hợp khác nhau (`bg-red-50`, `text-slate-500`…) | Trạng thái cùng nghĩa hiển thị khác màu giữa các trang. |
| Cỡ chữ tùy ý | `text-[11px]` 74 lần, `text-[10px]` 30 lần, cùng 18/19/20/22px lẻ | Dưới 12px khó đọc, vi phạm khuyến nghị accessibility. |
| Khung trang không đồng nhất | 33/66 trang dùng `WorkspacePage` | Header, khoảng cách, vị trí CTA khác nhau giữa các role. |
| CSS chồng lớp | `workspace-consistency/flat/polish/experience/page.css` + ~15 file CSS theo trang (3.158 dòng) | Các lớp "polish" sửa lẫn nhau, khó dự đoán. |
| Thiếu primitive | Không có DataTable, Tabs, FormField/Input/Select, Toast, EmptyState, Pagination, Stepper, Drawer | Mỗi trang tự viết bảng/form. |
| 2 nguồn route | `routes.config.ts` (`mvpRoutes`, vẫn dùng ở `QuickNavigation`) và `workspace-route-registry.ts` (Sidebar) | Tiêu đề/breadcrumb/icon lệch nhau. |
| Màu tag ngành cố định | `--color-tag-se/uiux/ai/qa/is` | DB có ngành Marketing/Design… sẽ không có màu. |
| File rác gốc repo | `ui-changes.html`, `ui-effects.html`, `ui_devtools_audit_feedback.md`, `src/ui-review` | Gây nhiễu, có thể lọt vào build. |

### 5.2 Nguyên tắc thiết kế chốt (cập nhật `MASTER.md`)
1. **Một nguồn token**: chốt primary (đề xuất giữ xanh lục `#0F5B4E` vì đã phủ toàn app, sửa `MASTER.md` cho khớp). Thêm thang neutral `ink-900…ink-400`, `surface`, `muted` thay `#596863`, `#14201d`.
2. **Thang chữ**: 12 / 14 / 16 / 18 / 20 / 24 / 30 (tên `text-caption`, `text-body-sm`, `text-body`, `text-title-sm`, `text-title`, `text-h2`, `text-h1`). Không dùng chữ dưới 12px.
3. **Khoảng cách**: bội số 4/8; khoảng giữa section 24px, trong card 16px.
4. **Trạng thái nghiệp vụ = một component** `StatusBadge` ánh xạ enum BE → nhãn tiếng Việt + màu + icon (Project, Team, Gate, Evaluation, Evidence, Result). Không trang nào tự chọn màu.
5. **Màu ngành sinh từ dữ liệu**: palette 8 màu, gán theo `major.code` bằng hàm băm ổn định; không cố định theo tên ngành.
6. **Mỗi CTA bị chặn phải có lý do** (từ `my-capabilities` / reason code) — tiếp tục dùng `WorkflowActionGate`.
7. **5 trạng thái bắt buộc** cho mọi vùng dữ liệu: loading, empty, partial, 403/404 (không lộ tồn tại), 409 (giữ nháp, tải lại).

### 5.3 Bộ khung trang (page archetypes)
Mọi trang phải thuộc 1 trong 5 khuôn, dùng chung `WorkspacePage`:

| Khuôn | Cấu trúc | Áp dụng |
|---|---|---|
| **Dashboard** | PageHeader (ngữ cảnh: kỳ, mode, trạng thái) → hàng KPI 3–4 thẻ → "Việc cần làm" → lối tắt | Tổng quan SV, GVHD, Mentor, Khoa, Admin |
| **List / Queue** | PageHeader + nút chính → thanh lọc (Tabs trạng thái + tìm kiếm + bộ lọc) → DataTable có phân trang → Drawer xem nhanh | Thẩm định đề cương, phân công chấm, danh sách user, đề tài, lưu trữ |
| **Detail / Workspace** | Header có trạng thái + hành động → Tabs (Tổng quan, Công việc, Báo cáo, Họp, Tệp, Minh chứng, Đóng góp, AI) → panel phải: timeline/người phụ trách | Project ở mọi role (SV, GVHD, Mentor, Khoa) |
| **Form / Wizard** | Stepper (đăng ký: Nhóm → Đề tài → Đề cương → Kiểm tra → Nộp) → form 1 cột tối đa 720px → thanh hành động dính đáy | Tạo nhóm, đăng ký đồ án, scheme, policy kỳ |
| **Review / Scoring** | 2 cột: trái = tài liệu/gói bàn giao (viewer), phải = rubric chấm theo tiêu chí, tổng tạm do BE trả, nút Lưu nháp / Hoàn tất | Chấm điểm, duyệt gate, duyệt minh chứng |

**Project hub thống nhất**: hiện SV dùng `/project/*`, GVHD dùng `/supervisor/projects/:id/*`, Mentor `/mentor/projects/:id/majors/:m/*`, Khoa `/department/projects/:id/*` với bố cục khác nhau. Giữ route theo role (để guard), nhưng dùng **một `ProjectHubLayout`** chung (header + tabs) với danh sách tab do capability quyết định.

### 5.4 Điều hướng theo role (IA mới)
Sidebar tối đa 7 mục cấp 1 mỗi role, nhóm còn lại vào tab trong trang.

| Role | Mục sidebar đề xuất |
|---|---|
| Sinh viên | Tổng quan · Nhóm của tôi · Đồ án (hub: công việc, mốc, lịch, báo cáo, họp, tệp, minh chứng, đóng góp, AI) · Bàn giao cuối · Kết quả · Lịch · Tin nhắn |
| Giảng viên | Tổng quan · Yêu cầu hướng dẫn · Đồ án hướng dẫn · Hướng dẫn chuyên ngành · Chấm điểm (chỉ khi có assignment) · Hồ sơ GV · Lịch |
| Khoa | Tổng quan · Thẩm định (đề cương + quyết định liên khoa) · Danh mục đồ án (hub) · Đánh giá & kết quả (scheme, phân công, công bố) · Giảng viên · Cấu hình học vụ (kỳ, policy, rubric, đề tài, mẫu mốc, gate) · Sinh viên (xác minh hồ sơ/chứng chỉ) |
| Admin | Tổng quan · Người dùng (+ import) · Phân quyền · Cấu trúc tổ chức · Nhật ký hệ thống · Xuất dữ liệu |

Hiện sinh viên có tới 15 mục, khoa 11 mục, nên gom như trên.

### 5.5 Danh sách tinh chỉnh từng màn (theo thứ tự làm)
1. **Đăng nhập / quên mật khẩu**: căn giữa, 1 cột, giữ trạng thái Google không khả dụng.
2. **Tổng quan SV** (`OverviewPage`): thẻ "Bước tiếp theo" theo state (FORMING → … → PUBLISHED) dạng stepper ngang; KPI: task trễ, báo cáo sắp hạn, họp tới.
3. **Quản lý nhóm** (`TeamManagementPage`, 408 dòng): tách 3 tab Thành viên / Lời mời / Điều kiện; bảng quota theo ngành có cột Yêu cầu, Hiện có, Trạng thái.
4. **Đăng ký đồ án**: chuyển sang wizard 5 bước, tóm tắt eligibility ở bước kiểm tra, lỗi 422 hiển thị theo danh sách issue.
5. **Task board**: Kanban + bảng; chip ngành PRIMARY/SUPPORTING; lọc ngành (chờ BE-07).
6. **Báo cáo tiến độ**: form theo mục cố định (đã làm / đang làm / kế hoạch / vướng mắc / rủi ro), hạn nộp và trạng thái trễ rõ ràng.
7. **Bàn giao cuối** (330 dòng): checklist bắt buộc ở trái, chọn version ở phải, xác nhận khóa có modal tóm tắt.
8. **Kết quả SV**: thẻ điểm tổng + breakdown COMMON / MAJOR / INDIVIDUAL (khi BE có DTO), nhãn "Đang chờ" thay vì 0.
9. **GVHD dashboard**: bảng project với cột rủi ro, báo cáo chờ phản hồi, yêu cầu mới.
10. **Thẩm định đề cương (khoa)**: List/Queue + Drawer; liên ngành hiển thị ma trận quyết định từng khoa.
11. **Scheme & phân công chấm**: bảng phủ phạm vi (ngành × tiêu chí × evaluator), ô thiếu tô cảnh báo.
12. **Chấm điểm**: khuôn Review/Scoring.
13. **Công bố kết quả**: preview, danh sách blocker (`NOT_READY`, `LOCKED_PACKAGE_REQUIRED`), xác nhận 2 bước.
14. **Admin**: bảng user có lọc role/khoa/trạng thái, màn import preview → commit, xuất roster.

### 5.6 Kỹ năng/công cụ dùng trong triển khai FE
- Skill `design:design-system` để chuẩn hóa token & component, `design:accessibility-review` cho WCAG AA, `design:ux-copy` cho nhãn tiếng Việt và thông báo lỗi, `design:design-handoff` khi giao spec cho dev.
- Playwright (đã có trong repo) chụp ảnh 375 / 768 / 1024 / 1440 px mỗi khuôn trang để so trước/sau.
- `oxlint` thêm rule cấm hex và `text-[Npx]` trong tsx (sau khi migrate xong).
- Sinh TypeScript client từ OpenAPI BE (hiện `src/types/backend` viết tay) để FE–BE không lệch.

---

## 6. Phần BE — phân tích & giao việc cho dev

Đội BE: **KhaiNQ** (BE-A: học vụ, phân quyền, dữ liệu) và **DongVV** (BE-B: đánh giá, kết quả, AI). File giao việc chi tiết: `BE_KhaiNQ_Nhiem_vu.md`, `BE_DongVV_Nhiem_vu.md`.

| ID | Việc | Người | Ưu tiên | Tiêu chí hoàn thành |
|---|---|---|---|---|
| BE-01 | Chạy script `contribution_snapshots` trên DB dùng chung; thêm kiểm tra drift script ↔ DB vào CI | KhaiNQ | P0 | `GET /projects/{id}/contributions` trả 200 trên DB thật; CI fail khi thiếu bảng |
| BE-02 | Seed dữ liệu demo A/B/C (IT đơn ngành, Marketing đơn ngành, liên ngành IT+Marketing+Design) gồm rubric, scheme, assignment, gói bàn giao; mỗi role 1 tài khoản test | KhaiNQ | P0 | FE chạy được trọn luồng đến công bố trên staging |
| BE-03 | Đổi chủ thể công bố kết quả liên khoa: Lead Department (sau khi đủ quyết định khoa tham gia) thay vì Admin (`EvaluationSchemeService.Results.cs`) | DongVV | P0 (cần chốt) | Test: Admin 403, Lead 200, Participating 403 |
| BE-04 | Governance projection: bỏ nhánh Admin, giới hạn theo quan hệ khoa–project (`ProjectGovernanceService.cs`) — BE-AW-005 | KhaiNQ | P0 | Test 4 actor: Admin, Lead, Participating, khoa ngoài |
| BE-05 | Phân biệt PRIMARY và DISCIPLINE_MENTOR trong predicate manager (BE-AW-001/003) | KhaiNQ | P0 | Mentor không tạo/xóa milestone, không gán task ngoài ngành |
| BE-06 | Calendar: lọc theo phạm vi ngành/assignment trước khi truy vấn (BE-AW-011) | KhaiNQ | P1 | Test cross-major bị chặn; `DateOnly` không lệch múi giờ |
| BE-07 | `GET /tasks/project/{id}?majorId=&role=` có phân trang + totalCount + kiểm quyền | KhaiNQ | P0 | FE lọc task không cần tự gom |
| BE-08 | Evaluator evidence projection theo phạm vi assignment (BE-AW-007) | DongVV | P1 | Trả danh sách file/minh chứng thuộc phạm vi |
| BE-09 | DTO kết quả của chính sinh viên có tên scheme + breakdown được phép xem | DongVV | P0 | `/projects/{id}/students/{me}/result` có `components[]` |
| BE-10 | `GET /projects/{id}/my-capabilities` thống nhất (CIB §11.4) với reason code | KhaiNQ | P1 | FE bỏ suy luận quyền phía client |
| BE-11 | Danh mục permission: seed `permissions` + `role_permissions`, hoặc gỡ endpoint (chờ chốt) | KhaiNQ | P1 | Màn RBAC có dữ liệu thật |
| BE-12 | Checkpoint/Gate: bảng + API (`/projects/{id}/checkpoints`, `/gate-reviews`) | DongVV | P1 | BRX-GATE-01/02 có test |
| BE-13 | `evidence_reviews` + API duyệt minh chứng | KhaiNQ | P1 | Lịch sử append-only |
| BE-14 | Hội đồng & lịch bảo vệ | DongVV | P1 | Phân công chấm theo hội đồng |
| BE-15 | Lưu kết quả AI (`ai_analysis_runs`, `ai_risk_factors`) + endpoint xem lịch sử | DongVV | P1 | Xem lại được run cũ, có citation |
| BE-16 | Working Agreement (bật theo policy) | KhaiNQ | P1 | Không ACTIVE nếu policy bắt buộc mà chưa đủ xác nhận |
| BE-17 | Chốt 1 mô hình kết quả; đánh dấu `project_result_policies/*` LEGACY | DongVV | P1 | Một nguồn duy nhất cho FE |
| BE-18 | Gợi ý GVHD (rule + AI xếp hạng sau lọc capacity) | DongVV | P2 | Không tự tạo request/assignment |
| BE-19 | Industry Expert, peer evaluation, phúc khảo | KhaiNQ/DongVV | P2 | Theo D6, D7, D16 |
| BE-20 | Hạ tầng: login DB riêng thay `sa`, job dọn `refresh_tokens`, xuất OpenAPI ổn định cho FE codegen | KhaiNQ | P0 | Không dùng `sa` trong appsettings |

---

## 7. Cơ chế đồng bộ FE ↔ BE

1. **Hợp đồng trước, code sau**: mỗi ticket BE có API mới phải merge OpenAPI + ví dụ payload + mã lỗi trước; FE sinh client và dựng màn với mock cùng lúc (CIB §11.6).
2. **Tên trạng thái thống nhất** giữa DB, API, UI và test: FE chỉ dịch enum sang nhãn tiếng Việt qua một bảng duy nhất (`display-label.ts`).
3. **Mã lỗi ổn định**: 403/404 không lộ tồn tại, 409 kèm entity mới, 422 kèm `issues[]` có `ruleCode`. FE hiển thị theo `ruleCode`.
4. **Branch & PR**: mỗi feature dùng chung tiền tố ticket (`BE-12`, `FE-12`) ở cả 2 repo; PR FE link PR BE tương ứng.
5. **Kiểm tra chéo hằng tuần (30 phút)**: chạy 3 kịch bản demo trên staging; ghi kết quả vào `docs/FINAL_FE_BE_SYNC_AUDIT.md`.
6. **Không ai ghi trực tiếp lên DB dùng chung** ngoài script trong `db/changes` đã review.

---

## 8. Sprint plan

**Giả định** (anh/chị chỉnh lại): sprint 2 tuần, bắt đầu thứ Hai 12/10/2026; đội 1 FE (lead UI/UX) + 2 BE; mỗi người ~8 điểm/tuần, lên kế hoạch ở 75% công suất. Chưa có dữ liệu nghỉ phép/lịch họp.

### Capacity mỗi sprint
| Người | Ngày làm | Điểm khả dụng | Kế hoạch (75%) |
|---|---|---|---|
| FE lead | 10/10 | 16 | 12 |
| KhaiNQ | 10/10 | 16 | 12 |
| DongVV | 10/10 | 16 | 12 |
| **Tổng** | | **48** | **36** |

### Sprint 1 (12/10 – 23/10): "Nền tảng sạch + gỡ chặn P0"
**Mục tiêu:** DB khớp code, phân quyền đúng tài liệu, FE có bộ token và primitive chung.

| Ưu tiên | Việc | Ước lượng | Người | Phụ thuộc |
|---|---|---|---|---|
| P0 | BE-01 drift + CI | 2 | KhaiNQ | – |
| P0 | BE-20 login DB riêng, OpenAPI ổn định | 2 | KhaiNQ | – |
| P0 | BE-04 + BE-05 phân quyền | 5 | KhaiNQ | – |
| P0 | BE-02 seed demo A/B/C | 5 | DongVV | BE-01 |
| P0 | BE-03 chủ thể công bố | 3 | DongVV | Quyết định của anh/chị |
| P0 | FE-01 Chốt token, cập nhật `MASTER.md`, thang chữ, `StatusBadge`, màu ngành động | 4 | FE | – |
| P0 | FE-02 Primitive: DataTable, Tabs, FormField, Toast, EmptyState, Pagination, Stepper, Drawer | 5 | FE | FE-01 |
| P0 | FE-03 Gộp 1 route registry, IA sidebar mới | 2 | FE | – |
| P1 | FE-08 Admin import chương trình + xuất roster | 2 | FE | – |
| **Tải** | | **30 / 36** | | |

### Sprint 2 (26/10 – 06/11): "Chấm điểm → công bố chạy thật"
| Ưu tiên | Việc | Ước lượng | Người |
|---|---|---|---|
| P0 | BE-07 lọc task theo ngành | 3 | KhaiNQ |
| P0 | BE-09 DTO kết quả cá nhân | 3 | DongVV |
| P0 | BE-17 chốt mô hình kết quả | 3 | DongVV |
| P1 | BE-08 evidence cho evaluator | 3 | DongVV |
| P1 | BE-10 my-capabilities | 5 | KhaiNQ |
| P0 | FE-04 Khuôn Review/Scoring + màn chấm điểm | 4 | FE |
| P0 | FE-05 Scheme/phân công (ma trận phủ) + công bố kết quả | 4 | FE |
| P0 | FE-06 Kết quả SV có breakdown | 2 | FE |
| P1 | FE-07 Project hub chung (`ProjectHubLayout`) | 3 | FE |
| **Tải** | | **30 / 36** | |

### Sprint 3 (09/11 – 20/11): "Gate, minh chứng, hội đồng (P1)"
BE-12, BE-13, BE-14, BE-06, BE-11 · FE: màn gate cho SV/GVHD/Mentor, duyệt minh chứng, lịch bảo vệ, tinh chỉnh màn 2–7 ở mục 5.5.

### Sprint 4 (23/11 – 04/12): "AI có lưu vết + Working Agreement + diễn tập bảo vệ"
BE-15, BE-16 · FE: trang AI (lịch sử run, giải thích yếu tố), Working Agreement, màn 9–14 ở mục 5.5, rà accessibility, diễn tập demo A/B/C.

### Backlog P2 (sau MVP)
BE-18 gợi ý GVHD, BE-19 Industry/peer/phúc khảo, cổng doanh nghiệp, dashboard hiệu chuẩn điểm.

### Rủi ro
| Rủi ro | Ảnh hưởng | Giảm thiểu |
|---|---|---|
| D1–D16 chưa chốt | Làm xong phải sửa luật | Xây dạng cấu hình/flag; chốt tối thiểu D2, D4, D5, D6 trước Sprint 3 |
| Chưa chốt ai công bố kết quả liên khoa | Chặn BE-03, demo C | Xin quyết định trong tuần này |
| DB dùng chung bị ghi tay | Drift lặp lại | Chỉ chạy script đã review, CI kiểm drift |
| Mật khẩu `sa` đã lộ trong chat | Rủi ro truy cập trái phép | Đổi mật khẩu ngay, BE-20 |
| FE refactor UI làm vỡ luồng | Lỗi hồi quy | Mỗi khuôn trang có Playwright screenshot + 775 test Vitest hiện có phải xanh |
| Thiếu tài khoản test theo role | Không nghiệm thu được | BE-02 bắt buộc trong Sprint 1 |

### Definition of Done
- [ ] Code review và merge vào `develop`, CI xanh ở cả 2 repo
- [ ] OpenAPI cập nhật, FE client sinh lại, không field ngoài hợp đồng
- [ ] Test âm (403/404/409/422) cho mọi lệnh có quyền
- [ ] Màn hình đạt 5 trạng thái, không tràn ngang ở 375 px, focus nhìn thấy, chữ ≥ 12 px
- [ ] Chạy được trên staging với tài khoản đúng role (không dùng Admin thay)
- [ ] Cập nhật ma trận truy vết Actor → UC → BR → API → Screen → Test

### Mốc thời gian
| Ngày | Sự kiện |
|---|---|
| 12/10 | Bắt đầu Sprint 1 |
| 16/10 | Kiểm tra giữa sprint + chốt chủ thể công bố |
| 23/10 | Demo Sprint 1, retro |
| 06/11 | Demo luồng chấm → công bố (kịch bản A) |
| 20/11 | Demo gate + liên ngành (kịch bản C) |
| 04/12 | Diễn tập bảo vệ 3 kịch bản |

---

## 9. Quyết định (cập nhật 09/10/2026)
1. **Đã chốt:** Lead Department công bố kết quả liên khoa (BE-03, FE theo sau khi BE đổi guard).
2. **Đã chốt:** giữ xanh lục `#0F5B4E`; `MASTER.md` đã được sửa cho khớp.
3. **Đang chờ:** ma trận permission. Mặc định đang làm: seed danh mục quyền + màn Phân quyền chỉ đọc trong MVP.
4. **Đang chờ:** Gate (D2) và Working Agreement (D1). Mặc định đang làm: Gate có trong MVP ở mức theo dõi (không chặn), Working Agreement để sau MVP.
5. **Đã chốt:** BE gồm KhaiNQ và DongVV.
