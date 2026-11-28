# AI-PMS — Gói Chuẩn bị Học kỳ Thí điểm 3 Track (`TK-W4-14`)

> Phạm vi: Học kỳ thí điểm thuần nghiệp vụ lõi V4 cho cả 3 Track: **`IT_ONLY`**, **`MKT_ONLY`**, và **`IT_MKT`** (liên ngành IT × MKT).
> Tuân thủ `db/README.md` (`TK-W0-06`): thư mục `db/pilot/` tách biệt hoàn toàn khỏi `db/changes/` và **không chứa** tài khoản demo hay mật khẩu hardcode.

---

## 1. Thành phần Gói Pilot

1. `db/pilot/01_seed_3track_pilot_config.sql`
   - Script SQL idempotent (chạy lặp lại an toàn qua `MERGE` / guard `IF NOT EXISTS`).
   - Khởi tạo **Học kỳ thí điểm 15 tuần** (`PILOT-FALL2026`), Organization (`PILOT`), 2 Khoa (`IT`, `MKT`), 2 Ngành (`SE`, `MKT`), 4 Chuyên ngành hẹp (`SE-WEB`, `SE-AI`, `MKT-DIG`, `MKT-BRAND`).
   - Thiết lập đủ **6 cửa sổ `ProjectPeriod`** theo đúng thứ tự thời gian (`REGISTRATION` → `PROJECT_REVIEW` → `SUPERVISOR_SELECTION` → `EXECUTION` → `FINAL_SUBMISSION` → `EVALUATION`), kèm:
     - `enable_defense_room_scheduling = 0` (`POL-06`: chia phòng Defense là tùy chọn non-blocking).
     - `project_period_calendars` (Thứ 2–Thứ 6 `62`, ngày nghỉ lễ Việt Nam).
     - `period_policy_versions` trạng thái `LOCKED`.
   - Thiết lập **3 bộ Rubric chuẩn** (`PILOT-RUB-IT`, `PILOT-RUB-MKT`, `PILOT-RUB-ITMKT`) với tổng trọng số đúng `100.00%`, đủ 4 mức chất lượng `rubric_criterion_levels`, và `rubric_versions` trạng thái `PUBLISHED`.
   - Thiết lập **3 `ReviewGateTemplate`** (`PILOT-GATE-IT-ONLY`, `PILOT-GATE-MKT-ONLY`, `PILOT-GATE-IT-MKT`), mỗi template gồm 3 Gate `G1 → G2 → G3`, yêu cầu minh chứng `INDIVIDUAL` và đầu ra tích hợp `INT-G1..G3` (`POL-05`).
   - Thiết lập **3 `AcademicBlueprint` trạng thái `PUBLISHED`** cho `IT_ONLY`, `MKT_ONLY`, và `IT_MKT` (Khoa chủ trì `IT` + Khoa phối hợp `MKT` đã đồng duyệt `APPROVED`, cấu hình preset `POL-01`: `30/50/20` đơn ngành, `40/40/20` liên ngành, `PassThreshold = 5.00`, floor `< 4.00`).
2. `db/pilot/samples/pilot_students_import_template.xlsx`
   - Tệp Excel mẫu 1 sheet, không chứa công thức, khớp tiêu đề `StudentAccountFileReader` (`MSSV`, `Ho ten`, `Email`, `SDT`, `Khung`, `Lop`, `Chuyen nganh hep`) và `CurriculumFileReader`.
3. `scripts/verify-pilot-readiness.ps1`
   - Kiểm tra tự động sau khi nạp migration + gói pilot:
     - Có đủ migration mới nhất (`20261215`, `20261216`, `20261217`) và 4 SQL trigger bất biến bảng điểm (`tr_immutable_*`) đang `is_disabled = 0`.
     - Đủ 6 canonical periods, 3 `AcademicBlueprint` `PUBLISHED` (`IT_ONLY`, `MKT_ONLY`, `IT_MKT`), tổng trọng số Rubric = `100.00%`, `enable_defense_room_scheduling = 0`.
     - Quét `db/pilot/` và `db/changes/` xác nhận **0** mật khẩu demo/hardcode.

---

## 2. Thứ tự Nạp & Kiểm tra trên Staging / Pre-Prod

1. Bảo đảm CSDL đã chạy đủ `db/schema.sql` + toàn bộ `db/e2e/migrations.json` (bao gồm `20261215`, `20261216`, `20261217`).
2. Bảo đảm ít nhất 1 tài khoản `ADMIN` / `DEPARTMENT_STAFF` thật đã được tạo trước trong `dbo.users` (`status = 'ACTIVE'`) để làm tham chiếu `created_by` (hoặc truyền `SESSION_CONTEXT(N'pilot_actor_id')` trước khi chạy script).
3. Chạy `01_seed_3track_pilot_config.sql` (có thể truyền tham số/biến `SESSION_CONTEXT(N'pilot_semester_code')` nếu muốn đổi mã học kỳ khác `PILOT-FALL2026`).
4. Chạy xác minh:
   ```powershell
   $env:AIPMS_SCHEMA_CHECK_CONNECTION = "<Staging/PreProd SQL ConnectionString>"
   ./scripts/verify-pilot-readiness.ps1 -SemesterCode "PILOT-FALL2026"
   ```
5. Nhập danh sách sinh viên thực tế qua màn hình **Admin → Nhập tài khoản SV** sử dụng mẫu `db/pilot/samples/pilot_students_import_template.xlsx`.

---

## 3. Ma trận Cấu hình Ứng dụng Staging / Production

| Cấu hình | Staging (`appsettings.Staging.example.json`) | Production (`appsettings.Production.example.json`) | Ghi chú |
|---|---|---|---|
| `Appeals:Enabled` / `VITE_ENABLE_RESULT_CORRECTION` | `true` / `true` | `true` / `true` | `TK-W4-04` bật phúc khảo/đính chính |
| `PeerEvaluation:Enabled` / `VITE_ENABLE_PEER_EVAL` | `false` / `false` | `false` / `false` | `Q-X-51` tắt mặc định trong kỳ thí điểm |
| `ExecutionConcurrency:RequireTokens` | `true` | `true` (sau 72h Staging xanh) | `TK-W4-10` chống ghi đè đồng thời |
| `ExecutionConcurrency:LogMissingTokens` | `true` | `false` | Dò caller thiếu token trên Staging |
| `NotificationEmail:Enabled` | `true` | `true` | `TK-W4-08` (yêu cầu SMTP `Email:Host`) |
| `ScheduledNotifications:Enabled` | `true` | `true` | `TK-W4-08` nhắc hạn 24h & quá hạn |
| `MeetingReminders:Enabled` | `true` | `true` | `TK-W4-08` làn riêng 15p trước họp |
| `RiskAlert:Enabled` | `true` | `true` | `TK-W4-08` dùng Health Pulse thống nhất |
| `DepartmentDigest:Enabled` | `true` | `true` | `TK-W4-09` Digest 17:00 GMT+7 |
| `NotificationRealtime:Enabled` | `true` | `true` | `TK-W4-11` Nhánh B Mobile-Safe |
