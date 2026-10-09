# Nhiệm vụ Backend — KhaiNQ (học vụ, phân quyền, dữ liệu, hạ tầng)

Người phối hợp: **DongVV** (đánh giá, kết quả, AI) · FE lead (giao diện) · Người duyệt: Vo Van
Nguồn: `AI-PMS_Ke_hoach_Hoan_thien_2026-10-09.md` mục 4 và 6 · Chuẩn nghiệp vụ: CIB v4.0 > SRS
Mã nguồn tham chiếu: `ai-pms-backend` `develop` @ `4c61c98`

## 0. Quy ước chung (giống DongVV)
1. Nhánh `feat/BE-xx-<mo-ta>` từ `develop`; PR tiêu đề `BE-xx: …`; 1 người review (DongVV hoặc Vo Van).
2. **DB-first:** mọi thay đổi schema là script **additive, chạy lại được** trong `db/changes/YYYYMMDD_<ten>.sql`, thêm tên vào `db/e2e/migrations.json`, cập nhật `db/schema.sql`, scaffold lại model trong `Infrastructure/Persistence/Generated`. Không đặt logic nghiệp vụ trong code generated.
3. **Hợp đồng trước:** cùng PR phải có controller + DTO + tài liệu `docs/<tinh-nang>.md` (request, response, mã lỗi 400/401/403/404/409/422, ví dụ). FE chỉ nối sau khi PR này merge.
4. **Test bắt buộc:** unit cho rule; integration cho quyền gồm ít nhất 1 case được phép và 2 case bị từ chối (sai role, sai phạm vi khoa/ngành/project).
5. **Không ghi trực tiếp lên DB dùng chung `AI_PMS`** ngoài script đã review. Seed demo dùng script riêng có thể xóa.
6. Báo FE trong nhóm trước khi đổi tên field, enum, URL hoặc mã lỗi.

## 1. Sprint 1 (12/10 – 23/10)

### BE-01 · Sửa drift DB `contribution_snapshots` — P0, 2 điểm
- **Vấn đề:** BE map bảng `contribution_snapshots` (`AipmsDbContext.Contributions.cs:14`) nhưng DB `AI_PMS` không có bảng; script `db/changes/20260916_add_contribution_snapshots.sql` chưa chạy. `GET /api/v1/projects/{id}/contributions` sẽ lỗi 500.
- **Làm:** chạy script trên DB dùng chung (sau khi Vo Van đồng ý); viết `scripts/check-schema-drift` so `db/e2e/migrations.json` với `sys.tables` và chạy trong CI trên DB test.
- **Xong khi:** API đóng góp trả 200 trên DB thật; CI báo lỗi nếu thiếu bảng.

### BE-20 · Hạ tầng an toàn — P0, 2 điểm
- Tạo login SQL `aipms_app` (chỉ DML trên `dbo`) và `aipms_migrator` (DDL), bỏ `sa` khỏi mọi `appsettings*` và tài liệu. Mật khẩu `sa` đã lộ trong chat ngày 09/10 → đổi ngay.
- Job dọn `refresh_tokens` đã hết hạn/thu hồi quá 30 ngày (hiện 384 dòng / 21 user).
- Đảm bảo Swagger/OpenAPI ổn định để FE sinh kiểu.

### BE-04 · Governance chỉ cho khoa có quan hệ với project — P0, 3 điểm
- **Vấn đề:** `ProjectGovernanceService.cs:31` cho Admin quyền `MANAGE_GOVERNANCE`; CIB §3: Admin không có quyền học thuật.
- **Làm:** bỏ nhánh Admin; quyền theo quan hệ khoa ↔ phạm vi học vụ đã đóng băng của project (Lead hoặc Participating). Admin không có quan hệ → 403.
- **Test:** Admin 403, Lead 200, Participating 200 (chỉ đọc phần ngành mình), khoa ngoài 403/404.

### BE-05 · Phân biệt Primary Supervisor và Discipline Mentor — P0, 3 điểm
- **Vấn đề:** predicate "manager" trong `ExecutionCapabilityService.cs` coi mọi supervisor đang hoạt động là manager (BE-AW-001/003 trong `ai-pms-frontend/docs/FINAL_FE_BE_SYNC_AUDIT.md`).
- **Làm:** thao tác cấu trúc (tạo/xóa milestone, sửa task ngoài ngành, đổi người phụ trách) chỉ cho Student Leader hoặc `assignment_type = PRIMARY`. Mentor chỉ thao tác trong `major_id` của assignment.
- **Test:** mentor tạo milestone → 403; mentor sửa task thuộc ngành mình → 200; ngành khác → 403.

### BE-02 (phần học vụ) · Dữ liệu demo cô lập — P0, 3 điểm (phần chấm điểm do DongVV)
- Script `db/demo/seed_demo_abc.sql` + `db/demo/reset_demo_abc.sql` tạo 3 kịch bản theo CIB §16.1:
  - A: nhóm IT đơn ngành; B: nhóm Marketing đơn ngành; C: nhóm liên ngành IT + Marketing + Design.
  - Mỗi kịch bản có kỳ, policy, nhóm đã khóa eligibility, đề cương đã duyệt (C có đủ quyết định các khoa), Primary + mentor, milestone/task/evidence, deliverable version.
  - Tài khoản test mỗi role (SV trưởng nhóm, SV thành viên, GVHD, mentor, người chấm, nhân viên khoa Lead, khoa tham gia, Admin). Gửi mật khẩu qua kênh riêng, không ghi vào repo.
- Seed chạy trên DB staging riêng, không chạy trên `AI_PMS` chung trừ khi Vo Van đồng ý.

## 2. Sprint 2 (26/10 – 06/11)

### BE-07 · Lọc task theo ngành có phân trang — P0, 3 điểm
- `GET /api/v1/tasks/project/{projectId}?majorId=&disciplineRole=PRIMARY|SUPPORTING&page=&pageSize=` (`TasksController.cs:31`).
- Lọc **trước** khi phân trang và đếm `totalCount`; kiểm tra majorId thuộc phạm vi project; mentor chỉ được truyền ngành của mình.
- **Test:** nhiều trang đúng count, sai ngành 403, project LEGACY/UNKNOWN không mở quyền.

### BE-10 · `GET /api/v1/projects/{id}/my-capabilities` — P1, 5 điểm
- Gộp `execution-actions`, `projects/{id}/actions` thành một map `capabilities` + `blockedReasons` theo CIB §11.5. Giữ endpoint cũ tới khi FE chuyển xong.
- Mã lý do ổn định (ví dụ `NOT_PROJECT_MEMBER`, `STATE_NOT_ALLOWED`, `POLICY_LOCKED`).

## 3. Sprint 3 (09/11 – 20/11)

### BE-06 · Calendar theo phạm vi — P1, 3 điểm
- `CalendarService.cs`: mentor chỉ thấy mục thuộc ngành mình, evaluator chỉ thấy hạn của assignment. Giữ `DateOnly` không đổi múi giờ.

### BE-11 · Danh mục quyền (permission) — P1, 2 điểm
- Seed `permissions` (khoảng 30–40 mã, ví dụ `USER_MANAGE`, `ACADEMIC_STRUCTURE_MANAGE`, `PERIOD_MANAGE`, `PROJECT_REVIEW`, `EVALUATION_SCHEME_MANAGE`, `RESULT_PUBLISH`, `AUDIT_READ`) và `role_permissions` cho 4 role.
- MVP: ma trận **chỉ đọc** cho Admin (không cho sửa quyền hệ thống), vì quyền thật còn phụ thuộc phạm vi project/assignment (CIB §3.1).
- *Đang chờ Vo Van chốt; nếu bỏ màn RBAC thì chỉ cần ẩn endpoint ghi.*

### BE-13 · Duyệt minh chứng có lịch sử — P1, 3 điểm
- Bảng `evidence_reviews(id, evidence_id, reviewer_id, decision ACCEPTED|NEEDS_WORK|REJECTED, comment, reviewed_at)`, append-only.
- `POST /api/v1/projects/{projectId}/evidence/{id}/reviews`, `GET .../reviews`. Người duyệt: Primary (mọi ngành) hoặc mentor (ngành mình). `project_evidence.verification_status` cập nhật trong cùng transaction.

## 4. Sprint 4 (23/11 – 04/12)

### BE-16 · Working Agreement (chỉ khi Vo Van chốt đưa vào MVP) — P1, 3 điểm
- `project_working_agreements(id, project_id, version, content_json, status, created_by, created_at)`, `agreement_acceptances(agreement_id, user_id, accepted_at)`.
- Policy kỳ có cờ `require_working_agreement`; khi bật, chuyển ACTIVE bị chặn (422 `WORKING_AGREEMENT_INCOMPLETE`) nếu chưa đủ xác nhận.
- *Mặc định hiện tại: để sau MVP.*

## 5. Backlog P2
- **BE-19a** Industry Expert: role `INDUSTRY_EXPERT`, bảng `industry_expert_profiles`, phạm vi đọc gói bàn giao theo assignment; phản hồi mặc định ADVISORY.
- Gộp dữ liệu trùng: `meetings.decisions` ↔ `meeting_decisions`, `meeting_action_items` ↔ `project_action_items` (đánh dấu deprecated, không xóa).

## 6. Điểm phối hợp
| Với | Nội dung |
|---|---|
| DongVV | BE-02 dùng chung script seed (KhaiNQ phần học vụ trước, DongVV phần scheme/assignment sau). BE-10 cần mã quyền chấm/công bố của DongVV. |
| FE | FE-03/FE-07 chờ BE-10; FE lọc task chờ BE-07; màn Phân quyền chờ BE-11; màn duyệt minh chứng chờ BE-13. |
