# SỔ TAY THỰC HIỆN TOÀN DIỆN CHO BACKEND DEVELOPER (E2E / UAT WORKFLOW)
## HỆ THỐNG QUẢN LÝ ĐỒNG BỘ LỊCH BẢO VỆ ĐỀ TÀI TỐT NGHIỆP (PMS · FAMS STANDARD)
*Tài liệu tổng hợp duy nhất dành cho Backend Developer để triển khai Database Schema, cài đặt ràng buộc nghiệp vụ, tích hợp API và nghiệm thu 12 kịch bản E2E / UAT cùng Frontend.*

> **Cập nhật kiểm chứng 08/11/2026:** Đã chạy đủ 7 migration trên DB UAT `AI_PMS` trong một giao dịch; kết nối mới đọc lại đủ 7/7 đối tượng/cột. Đã tạo 6 **phòng mẫu UAT** tầng 2–4 và lịch mẫu cho kỳ `FE-ACCEPT-EVAL-FA26`; 4 vai trò đăng nhập qua BE và FE. Kiểm thử trình duyệt xác nhận khóa phòng chỉ áp dụng cho lần xem trước. **Chưa đạt 12/12 kịch bản hoặc ký duyệt UAT:** 3 nhóm của kỳ này còn Gate 2 `PLANNED`, Gate 3 chưa có cửa sổ lịch, và DB chưa có phiên bảo vệ, hội đồng, kết quả hay đơn phúc khảo. Không đánh dấu các trường hợp này là PASS.
>
> **Đính chính contract:** Các URL/request minh họa ở Chương 5–6 bên dưới không phải OpenAPI đang chạy. Dùng các route hiện hành: `GET /api/v1/rooms`; `GET/PUT /api/v1/project-periods/{id}/calendar`; `POST /api/v1/project-periods/{id}/review-gates/r3/schedule/preview|apply` (apply đồng thời công bố Gate 3); `POST /api/v1/project-periods/{id}/defense-sessions/auto-schedule/preview|apply`; `POST /api/v1/projects/{projectId}/review-gates/{gateCode}/override`; `POST /api/v1/defense-sessions/{sessionId}/change-requests`. Request lập lịch dùng `roomIds`, `maxSlotsPerDay`, `exclusions: [{roomId,date,slotNumber}]` và token lấy từ preview. Danh sách 10 mã lỗi trong Chương 5 là đề xuất nghiệp vụ, chưa phải contract đã triển khai. Xem `docs/contracts/cib-v4/openapi.json` và `docs/handoff/BE16_GATE3_GATE4_FE_E2E_UAT_20261108.md` trong nhánh BE để tích hợp FE.

### Biên bản chạy UAT hiện tại (DB `AI_PMS`, 08/11/2026)

| Kịch bản | Trạng thái | Bằng chứng / điều kiện còn thiếu |
| :---: | :--- | :--- |
| 1 | PASS | API và UI Admin hiển thị 6 phòng mẫu tầng 2–4; lịch kỳ có ngày đóng cửa 20/11, DB đọc lại đúng. |
| 2 | PARTIAL | UI khóa phòng `ALPHA_202`, request preview gửi đúng exclusion và DB vẫn `ACTIVE`; chưa có nhóm đủ điều kiện để chứng minh phân bổ tránh phòng/ca. |
| 3 | CHƯA CHẠY | DB chưa có hội đồng bảo vệ để thử GVHD ngồi hội đồng của chính nhóm. |
| 4 | CHƯA CHẠY | DB chưa có phiên bảo vệ để thử trùng phòng/giảng viên trên dữ liệu UAT. |
| 5 | PASS (chặn điều kiện) | Preview Gate 3 trả 3 nhóm `REVIEW_GATE_2_NOT_PASSED`; preview Gate 4 trả 3 nhóm `REVIEW_GATE_3_NOT_PASSED`, không tạo lịch. |
| 6 | PARTIAL | ADMIN bị 403 khi gọi override; STAFF có quyền nhưng token cố ý cũ trả 409. Chưa ghi đè quyết định học thuật thật. |
| 7 | CHƯA CHẠY trên DB UAT | DB chưa có kết quả sinh viên hoặc đơn phúc khảo; integration test trên SQL Server cô lập đã đạt 1/1 cho rút đơn, quyền sở hữu, token và audit. |
| 8 | CHƯA CHẠY | DB chưa có phiên bảo vệ để nộp đơn đổi lịch/phòng. |
| 9 | CHƯA CHẠY | Gate 3 chưa có cửa sổ lịch và chưa có nhóm đạt Gate 2 để công bố. |
| 10 | PARTIAL | Cập nhật phòng bằng token hợp lệ trả 200; dùng lại token cũ trả 409 `ROOM_STALE`. Chưa thử cạnh tranh sửa phiên bảo vệ. |
| 11 | PASS (route đã thử) | Preview: ADMIN/STAFF 200, LECTURER/STUDENT 403; override: ADMIN 403, STAFF 409 với token cũ. Trên FE, STAFF xem được sơ đồ phòng; LECTURER/STUDENT được chuyển về workspace đúng vai trò. |
| 12 | CHƯA CHẠY | Thiếu luồng học thuật Gate 2→3→4 có quyết định, hội đồng và phiên bảo vệ hợp lệ. |

> **Sự kiện vận hành:** Lần khởi động BE đầu tiên khoảng 11 giây với cấu hình worker mặc định trùng thời điểm 2 email delivery (notification recipient `70014`, `70015`) được đánh dấu `SENT` và 28 notification recipient được đánh dấu đã chuyển realtime. Worker chỉ ghi `SENT` sau khi SMTP transport trả thành công; chưa xác nhận email tới hộp thư. Đã dừng tiến trình và chạy lại với worker gửi email/realtime tắt. Không tự đảo trạng thái đã ghi.

---

## MỤC LỤC
1. [Hiện trạng Mã nguồn & Đồng bộ Git Branch](#1-hiện-trạng-mã-nguồn--đồng-bộ-git-branch)
2. [Bước 1: Chạy Mã Thật Schema trên Cơ sở Dữ liệu (SQL Server)](#2-bước-1-chạy-mã-thật-schema-trên-cơ-sở-dữ-liệu-sql-server)
3. [Bước 2: Chuẩn bị Dữ liệu mẫu (Seed Data & Fixtures)](#3-bước-2-chuẩn-bị-dữ-liệu-mẫu-seed-data--fixtures)
4. [Bước 3: Các Quy chuẩn Nghiệp vụ Cốt lõi (Core Business Rules)](#4-bước-3-các-quy-chuẩn-nghiệp-vụ-cốt-lõi-core-business-rules)
5. [Bước 4: Đặc tả API, Khóa Lạc Quan & 10 Mã Lỗi Xung Đột](#5-bước-4-đặc-tả-api-khóa-lạc-quan--10-mã-lỗi-xung-đột)
6. [Bước 5: Trình tự Thực hiện 12 Kịch bản Kiểm thử E2E / UAT](#6-bước-5-trình-tự-thực-hiện-12-kịch-bản-kiểm-thử-e2e--uat)
7. [Bước 6: Khởi chạy Kiểm thử Kết nối Thực tế (Local E2E)](#7-bước-6-khởi-chạy-kiểm-thử-kết-nối-thực-tế-local-e2e)
8. [Bước 7: Biên bản Nghiệm thu UAT & Tiêu chí Ký duyệt (Sign-Off)](#8-bước-7-biên-bản-nghiệm-thu-uat--tiêu-chí-ký-duyệt-sign-off)

---

## 1. HIỆN TRẠNG MÃ NGUỒN & ĐỒNG BỘ GIT BRANCH

### 1.1. Backend Repository
- **Thư mục làm việc:** `F:\AI-PMS\ai-pms-backend-be16-20261107`
- **Nhánh (Branch):** `feat/be-period-rooms-defense-workflow-20261107`
- **HEAD Commit:** `17af0a7` (Đi trước `origin/develop` 18 commit).
- **Trạng thái:** Sạch (Clean working tree). 
- **Kiểm thử đã xác nhận:** Unit tests **1.298 / 1.298 passed (100%)** bao gồm các quy tắc kiểm tra hội đồng Gate 1-3 cố định và tính kế thừa Gate 4; integration trọng tâm trên SQL Server cô lập **19/19** (Academic Tracking) và **33/33** (Review Gate).

### 1.2. Frontend Repository
- **Thư mục làm việc:** `F:\AI-PMS\ai-pms-frontend-uiux-20261106`
- **Nhánh (Branch):** `feat/fe-uiux-standardisation-20261106`
- **HEAD Commit:** `edc7bee` (Đi trước `origin/develop` 59 commit).
- **Trạng thái:** Sạch (Clean working tree).
- **Chuẩn hoá UI/UX:** Đã hoàn tất đổi nhận diện thương hiệu thành **PMS** (màu chủ đạo FAMS Orange `#C2410C`, loại bỏ hoàn toàn các class màu Teal cũ, cập nhật sơ đồ phòng 2D và lưới timetable).
- **Độ bao phủ Test & Build:** `pnpm test --run` đạt **2.085 / 2.085 tests passed trên 309 test files (100%)**. Typecheck 0 errors, Lint 0 errors, Build production hoàn tất thành công trong 1.15s.

### 1.3. Lệnh Git dành cho DEV
```bash
# Đối với Backend
cd F:\AI-PMS\ai-pms-backend-be16-20261107
git fetch origin
git checkout feat/be-period-rooms-defense-workflow-20261107
# Nhánh này hiện chưa có trên origin; kiểm tra SHA và trạng thái trước khi push.

# Đối với Frontend
cd F:\AI-PMS\ai-pms-frontend-uiux-20261106
git fetch origin
git checkout feat/fe-uiux-standardisation-20261106
# Nhánh này hiện chưa có trên origin; kiểm tra SHA và trạng thái trước khi push.
```

---

## 2. BƯỚC 1: CHẠY MÃ THẬT SCHEMA TRÊN CƠ SỞ DỮ LIỆU (SQL SERVER)

Toàn bộ 7 file script SQL nằm trong thư mục `db/changes/` của Backend. DEV bắt buộc phải chạy theo đúng thứ tự 1 -> 7 dưới đây trên database thật (SQL Server hoặc Azure SQL Database):

### 2.1. Thứ tự 7 file Migration bắt buộc:

1. `db/changes/20261107_add_project_period_calendars.sql`
2. `db/changes/20261107_add_rooms_to_defense.sql`
3. `db/changes/20261107_add_review_gate_override.sql`
4. `db/changes/20261107_allow_withdrawn_appeals.sql`
5. `db/changes/20261107_add_gate_session_scripts.sql`
6. `db/changes/20261107_add_defense_change_requests.sql`
7. `db/changes/20261108_publish_gate3_schedules.sql`

---

### 2.2. Chi tiết mã nguồn SQL từng file migration:

#### File 1: `20261107_add_project_period_calendars.sql`
```sql
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.project_period_calendars', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_period_calendars (
        id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        project_period_id bigint NOT NULL,
        working_days_of_week int NOT NULL CONSTRAINT df_period_calendar_days DEFAULT 62,
        holidays_json nvarchar(max) NOT NULL CONSTRAINT df_period_calendar_holidays DEFAULT N'[]',
        concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_period_calendar_token DEFAULT NEWID(),
        created_at datetime2(0) NOT NULL CONSTRAINT df_period_calendar_created DEFAULT SYSUTCDATETIME(),
        updated_at datetime2(0) NOT NULL CONSTRAINT df_period_calendar_updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_period_calendar_period UNIQUE(project_period_id),
        CONSTRAINT fk_period_calendar_period FOREIGN KEY(project_period_id) REFERENCES dbo.project_periods(id) ON DELETE CASCADE,
        CONSTRAINT ck_period_calendar_days CHECK(working_days_of_week BETWEEN 1 AND 127),
        CONSTRAINT ck_period_calendar_holidays CHECK(ISJSON(holidays_json) = 1)
    );
END;

COMMIT TRANSACTION;
```

#### File 2: `20261107_add_rooms_to_defense.sql`
```sql
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.rooms', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.rooms (
        id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        code nvarchar(50) NOT NULL,
        building nvarchar(50) NOT NULL,
        floor int NOT NULL,
        capacity int NOT NULL CONSTRAINT df_room_capacity DEFAULT 40,
        status varchar(20) NOT NULL CONSTRAINT df_room_status DEFAULT 'ACTIVE',
        concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_room_token DEFAULT NEWID(),
        created_at datetime2(0) NOT NULL CONSTRAINT df_room_created DEFAULT SYSUTCDATETIME(),
        updated_at datetime2(0) NOT NULL CONSTRAINT df_room_updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_room_code UNIQUE(code),
        CONSTRAINT ck_room_capacity CHECK(capacity > 0),
        CONSTRAINT ck_room_status CHECK(status IN ('ACTIVE','MAINTENANCE','INACTIVE'))
    );
    CREATE INDEX ix_room_building_floor ON dbo.rooms(building, floor);
END;

IF COL_LENGTH(N'dbo.defense_sessions', N'room_id') IS NULL
    ALTER TABLE dbo.defense_sessions ADD room_id bigint NULL;

-- Cập nhật dữ liệu cũ và gán room_id hợp lệ
INSERT INTO dbo.rooms(code, building, floor, capacity, status)
SELECT DISTINCT UPPER(LTRIM(RTRIM(s.room))), N'Chưa xác định', 0, 40, 'ACTIVE'
FROM dbo.defense_sessions s
WHERE LEN(LTRIM(RTRIM(s.room))) BETWEEN 1 AND 50
  AND NOT EXISTS (SELECT 1 FROM dbo.rooms r WHERE r.code = UPPER(LTRIM(RTRIM(s.room))));

UPDATE s SET room_id = r.id
FROM dbo.defense_sessions s
JOIN dbo.rooms r ON r.code = UPPER(LTRIM(RTRIM(s.room)))
WHERE s.room_id IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_defense_session_room')
    ALTER TABLE dbo.defense_sessions ADD CONSTRAINT fk_defense_session_room FOREIGN KEY(room_id) REFERENCES dbo.rooms(id);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.defense_sessions') AND name = N'ix_defense_session_room_start')
    CREATE INDEX ix_defense_session_room_start ON dbo.defense_sessions(room_id, start_at);

COMMIT TRANSACTION;
```

#### File 3: `20261107_add_review_gate_override.sql`
```sql
IF COL_LENGTH('dbo.project_gate_instances', 'decision_source') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD decision_source varchar(20) NOT NULL CONSTRAINT DF_project_gate_decision_source DEFAULT 'REVIEW';
IF COL_LENGTH('dbo.project_gate_instances', 'overridden_by') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD overridden_by bigint NULL;
IF COL_LENGTH('dbo.project_gate_instances', 'overridden_at') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD overridden_at datetime2(0) NULL;
IF COL_LENGTH('dbo.project_gate_instances', 'override_reason') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD override_reason nvarchar(2000) NULL;
```

#### File 4: `20261107_allow_withdrawn_appeals.sql`
```sql
IF OBJECT_ID('dbo.result_appeal_requests', 'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints
        WHERE name = N'ck_appeal_request_status' AND parent_object_id = OBJECT_ID(N'dbo.result_appeal_requests'))
        ALTER TABLE dbo.result_appeal_requests DROP CONSTRAINT ck_appeal_request_status;
    ALTER TABLE dbo.result_appeal_requests WITH CHECK ADD CONSTRAINT ck_appeal_request_status
        CHECK (status IN ('PENDING', 'UNDER_REVIEW', 'ACCEPTED', 'REJECTED', 'WITHDRAWN'));
END;
```

#### File 5: `20261107_add_gate_session_scripts.sql`
```sql
IF COL_LENGTH('dbo.review_gate_assignments', 'session_role') IS NULL
    ALTER TABLE dbo.review_gate_assignments ADD session_role varchar(20) NOT NULL CONSTRAINT DF_gate_assignment_session_role DEFAULT 'MEMBER';

IF OBJECT_ID('dbo.gate_session_scripts', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.gate_session_scripts (
        id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        project_gate_instance_id bigint NOT NULL UNIQUE,
        mode varchar(20) NOT NULL,
        room_id bigint NULL,
        live_kit_room_name nvarchar(100) NULL,
        live_kit_session_id nvarchar(100) NULL,
        scheduled_start_at datetimeoffset(0) NOT NULL,
        scheduled_end_at datetimeoffset(0) NOT NULL,
        actual_start_at datetimeoffset(0) NULL,
        actual_end_at datetimeoffset(0) NULL,
        total_duration_minutes int NULL,
        presences_json nvarchar(max) NOT NULL CONSTRAINT DF_gate_script_presences DEFAULT '[]',
        agenda_stages_json nvarchar(max) NOT NULL CONSTRAINT DF_gate_script_agenda DEFAULT '[]',
        qa_log_json nvarchar(max) NOT NULL CONSTRAINT DF_gate_script_qa DEFAULT '[]',
        remediation_items_json nvarchar(max) NOT NULL CONSTRAINT DF_gate_script_remediation DEFAULT '[]',
        general_notes nvarchar(max) NULL,
        verdict varchar(30) NOT NULL CONSTRAINT DF_gate_script_verdict DEFAULT 'PENDING',
        signed_by_secretary nvarchar(100) NULL,
        signed_by_chair nvarchar(100) NULL,
        concurrency_token uniqueidentifier NOT NULL CONSTRAINT DF_gate_script_token DEFAULT NEWID(),
        created_at datetimeoffset(0) NOT NULL CONSTRAINT DF_gate_script_created DEFAULT SYSDATETIMEOFFSET(),
        updated_at datetimeoffset(0) NOT NULL CONSTRAINT DF_gate_script_updated DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT FK_gate_script_gate FOREIGN KEY(project_gate_instance_id) REFERENCES dbo.project_gate_instances(id),
        CONSTRAINT FK_gate_script_room FOREIGN KEY(room_id) REFERENCES dbo.rooms(id),
        CONSTRAINT CK_gate_script_mode CHECK (mode IN ('ONLINE','OFFLINE')),
        CONSTRAINT CK_gate_script_mode_target CHECK (
            (mode='ONLINE' AND live_kit_room_name IS NOT NULL AND room_id IS NULL) 
            OR 
            (mode='OFFLINE' AND room_id IS NOT NULL AND live_kit_room_name IS NULL)
        )
    );
END;
```

#### File 6: `20261107_add_defense_change_requests.sql`
```sql
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.defense_committees', N'concurrency_token') IS NULL
    ALTER TABLE dbo.defense_committees ADD concurrency_token uniqueidentifier NOT NULL
        CONSTRAINT df_defense_committee_token DEFAULT NEWID();

IF COL_LENGTH(N'dbo.defense_committee_members', N'role') IS NULL
    ALTER TABLE dbo.defense_committee_members ADD role varchar(20) NOT NULL
        CONSTRAINT df_defense_committee_member_role DEFAULT 'MEMBER';

IF OBJECT_ID(N'dbo.defense_schedule_change_requests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.defense_schedule_change_requests (
        id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        defense_session_id bigint NOT NULL,
        requester_id bigint NOT NULL,
        current_room_id bigint NOT NULL,
        current_start_at datetimeoffset(0) NOT NULL,
        current_end_at datetimeoffset(0) NOT NULL,
        desired_room_id bigint NOT NULL,
        desired_start_at datetimeoffset(0) NOT NULL,
        desired_end_at datetimeoffset(0) NOT NULL,
        reason_category varchar(50) NOT NULL,
        reason_details nvarchar(2000) NOT NULL,
        evidence_file_url nvarchar(500) NULL,
        status varchar(30) NOT NULL CONSTRAINT df_defense_change_status DEFAULT 'PENDING',
        reviewer_id bigint NULL,
        review_note nvarchar(2000) NULL,
        reviewed_at datetimeoffset(0) NULL,
        concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_defense_change_token DEFAULT NEWID(),
        created_at datetimeoffset(0) NOT NULL CONSTRAINT df_defense_change_created DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT fk_defense_change_session FOREIGN KEY(defense_session_id) REFERENCES dbo.defense_sessions(id),
        CONSTRAINT fk_defense_change_requester FOREIGN KEY(requester_id) REFERENCES dbo.users(id),
        CONSTRAINT fk_defense_change_reviewer FOREIGN KEY(reviewer_id) REFERENCES dbo.users(id),
        CONSTRAINT fk_defense_change_current_room FOREIGN KEY(current_room_id) REFERENCES dbo.rooms(id),
        CONSTRAINT fk_defense_change_desired_room FOREIGN KEY(desired_room_id) REFERENCES dbo.rooms(id),
        CONSTRAINT ck_defense_change_status CHECK(status IN ('PENDING','APPROVED','REJECTED','WITHDRAWN')),
        CONSTRAINT ck_defense_change_reason CHECK(reason_category IN ('ROOM_FACILITY_ISSUE','SCHEDULE_CLASH','SPECIAL_REQUIREMENT','OTHER'))
    );
    CREATE INDEX ix_defense_change_session_status ON dbo.defense_schedule_change_requests(defense_session_id, status);
END;

COMMIT TRANSACTION;
```

#### File 7: `20261108_publish_gate3_schedules.sql`
```sql
IF COL_LENGTH('dbo.gate_session_scripts', 'published_at') IS NULL
    ALTER TABLE dbo.gate_session_scripts ADD published_at datetimeoffset(0) NULL;

IF COL_LENGTH('dbo.gate_session_scripts', 'published_by') IS NULL
    ALTER TABLE dbo.gate_session_scripts ADD published_by bigint NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_gate_script_published_by'
    AND parent_object_id = OBJECT_ID('dbo.gate_session_scripts'))
    EXEC(N'ALTER TABLE dbo.gate_session_scripts ADD CONSTRAINT FK_gate_script_published_by
        FOREIGN KEY (published_by) REFERENCES dbo.users(id)');

-- Dữ liệu Gate 3 OFFLINE cũ đã có phòng sẽ được gắn thời điểm công bố
EXEC(N'UPDATE s SET published_at = s.created_at
FROM dbo.gate_session_scripts AS s
JOIN dbo.project_gate_instances AS g ON g.id = s.project_gate_instance_id
WHERE g.sequence = 3 AND s.mode = ''OFFLINE'' AND s.room_id IS NOT NULL
    AND s.published_at IS NULL');
```

---

### 2.3. Script SQL Kiểm tra Hoàn tất Schema (Readback Verification)
DEV hãy chạy câu lệnh SQL kiểm tra sau trong SSMS / DBeaver / sqlcmd để xác nhận 100% đối tượng đã tồn tại:

```sql
SELECT 
    'project_period_calendars' AS TableName, 
    COUNT(*) AS ObjectExists 
FROM sys.tables WHERE name = 'project_period_calendars'
UNION ALL
SELECT 'rooms', COUNT(*) FROM sys.tables WHERE name = 'rooms'
UNION ALL
SELECT 'gate_session_scripts', COUNT(*) FROM sys.tables WHERE name = 'gate_session_scripts'
UNION ALL
SELECT 'defense_schedule_change_requests', COUNT(*) FROM sys.tables WHERE name = 'defense_schedule_change_requests'
UNION ALL
SELECT 'defense_sessions.room_id', COUNT(*) FROM sys.columns 
WHERE object_id = OBJECT_ID('dbo.defense_sessions') AND name = 'room_id'
UNION ALL
SELECT 'project_gate_instances.decision_source', COUNT(*) FROM sys.columns 
WHERE object_id = OBJECT_ID('dbo.project_gate_instances') AND name = 'decision_source'
UNION ALL
SELECT 'gate_session_scripts.published_at', COUNT(*) FROM sys.columns 
WHERE object_id = OBJECT_ID('dbo.gate_session_scripts') AND name = 'published_at';
```
> **Yêu cầu:** Tất cả các dòng trả về đều phải có giá trị `ObjectExists = 1`.

---

## 3. BƯỚC 2: CHUẨN BỊ DỮ LIỆU MẪU (SEED DATA & FIXTURES)

> [!CAUTION]
> **QUY TẮC BẢO MẬT & XÁC THỰC:** TUYỆT ĐỐI KHÔNG tạo tài khoản người dùng ảo (dummy accounts). Phải sử dụng hoặc seed các tài khoản thật của hệ thống theo 4 vai trò bắt buộc:
> - `ADMIN` (Quản trị viên hệ thống)
> - `DEPARTMENT_STAFF` (Cán bộ phụ trách bộ môn)
> - `LECTURER` (Giảng viên hướng dẫn / Thành viên hội đồng)
> - `STUDENT` (Sinh viên thực hiện đề tài)
> *Lưu ý: Không có vai trò `DEPARTMENT_HEAD` trong hệ thống.*

### 3.1. Danh sách Phòng mẫu UAT (Tầng 2, 3, 4)
Fixture UAT hiện có 6 phòng mẫu thuộc các tầng 2, 3, 4 ở trạng thái `ACTIVE`, sức chứa $\ge 15$. Mã `ALPHA/BETA/GAMMA` chưa được xác nhận là mã phòng vật lý của trường:
```sql
INSERT INTO dbo.rooms (code, building, floor, capacity, status)
VALUES 
    (N'ALPHA_201', N'Alpha Building', 2, 35, 'ACTIVE'),
    (N'ALPHA_202', N'Alpha Building', 2, 40, 'ACTIVE'),
    (N'BETA_301',  N'Beta Building',  3, 30, 'ACTIVE'),
    (N'BETA_302',  N'Beta Building',  3, 30, 'ACTIVE'),
    (N'GAMMA_401', N'Gamma Building', 4, 25, 'ACTIVE'),
    (N'GAMMA_402', N'Gamma Building', 4, 45, 'ACTIVE');
```

### 3.2. Danh mục 6 Ca Hành chính Chuẩn (Administrative Slots)
Quy chuẩn 6 ca thời gian hành chính (mỗi ca kéo dài chính xác 75 phút, nghỉ 15 phút giữa các ca, múi giờ Việt Nam GMT+7):

| Ca (Slot) | Giờ bắt đầu | Giờ kết thúc | Thời lượng | Nghỉ giữa ca |
| :--- | :---: | :---: | :---: | :---: |
| **Slot 1** | `08:00` | `09:15` | 75 phút | 15 phút |
| **Slot 2** | `09:30` | `10:45` | 75 phút | 15 phút |
| **Slot 3** | `11:00` | `12:15` | 75 phút | Nghỉ trưa (75p) |
| **Slot 4** | `13:30` | `14:45` | 75 phút | 15 phút |
| **Slot 5** | `15:00` | `16:15` | 75 phút | 15 phút |
| **Slot 6** | `16:30` | `17:45` | 75 phút | Kết thúc |

### 3.3. Cấu hình Lịch học kỳ & Ngày nghỉ (`project_period_calendars`)
- Fixture UAT hiện dùng kỳ `FE-ACCEPT-EVAL-FA26` (ID `20003`); DB không có kỳ mã `FALL2026` tại thời điểm kiểm tra.
- Ngày làm việc trong tuần: `working_days_of_week = 126` (Thứ 2 đến Thứ 7 theo bitmask của BE). Giá trị mặc định `62` chỉ là Thứ 2 đến Thứ 6.
- Cấu hình mảng JSON ngày nghỉ lễ (`holidays_json`) có ít nhất 1 ngày nghỉ:
```sql
UPDATE dbo.project_period_calendars
SET holidays_json = N'[{"date":"2026-11-20","name":"UAT closure (demo)","isRecurring":false}]'
WHERE project_period_id = @CurrentPeriodId;
```

---

## 4. BƯỚC 3: CÁC QUY CHUẨN NGHIỆP VỤ CỐT LÕI (CORE BUSINESS RULES)

### 4.1. Khung giờ 6 Ca Hành chính là Bất biến
- Mọi lịch xếp phiên hội đồng (Gate 3 và Gate 4) chỉ được phép rơi vào đúng 1 trong 6 ca trên.
- Không được xếp lịch lệch khung giờ (ví dụ: 08:00 - 09:15 là không hợp lệ).

### 4.2. Danh sách Loại trừ Phòng và Ca (Per-run Exclusions)
- Trong request tự động xếp lịch (`auto-schedule`), client gửi lên mảng `excludedRoomIds` và `excludedSlotIds`.
- **Ràng buộc:** Các phòng và ca này chỉ bị bỏ qua trong thuật toán phân bổ của **lần chạy đó**.
- **Cấm:** BE **tuyệt đối không được cập nhật** trường `status = 'INACTIVE'` của bảng `rooms` trong cơ sở dữ liệu khi nhận danh sách loại trừ này!

### 4.3. Điều kiện Tuần tự Gate 3 & Gate 4 (Sequential Gate Eligibility)
- **Gate 2:** Đánh giá điều kiện làm đề tài tốt nghiệp.
- **Gate 3 (Review Gate / Hội đồng Đánh giá Tiến độ):**
  - Đề tài bảo vệ trước hội đồng phản biện.
  - Kết quả `verdict` phải là `PASSED` hoặc `REMEDY_APPROVED`.
- **Gate 4 (Defense Gate / Hội đồng Bảo vệ Chính thức Tốt nghiệp):**
  - **Điều kiện tiên quyết:** Đề tài chỉ đủ điều kiện tham gia Gate 4 khi đã hoàn thành và đạt yêu cầu tại Gate 3.
  - Ngoại lệ duy nhất: Trường hợp đề tài có quyết định ghi đè kết quả hợp lệ (`decision_source = 'OVERRIDDEN'`) được Trưởng bộ môn phê duyệt kèm lý do xác thực.

### 4.4. Chống Xung đột Quyền lợi Giảng viên Hướng dẫn (Conflict of Interest)
- Giảng viên hướng dẫn (Supervisor) của một đề tài **không được phép** phân công làm Chủ tịch hội đồng, Thư ký hội đồng hoặc Uỷ viên đánh giá của chính đề tài đó tại Gate 3 và Gate 4.
- Hệ thống phải chặn ngay từ tầng Service và trả về mã lỗi `SUPERVISOR_CONFLICT`.

### 4.5. Chống Trùng Lịch Giảng viên và Phòng (Collision Prevention)
- Một giảng viên không thể ngồi 2 hội đồng trong cùng 1 ca.
- Một phòng bảo vệ không thể xếp 2 phiên hội đồng trong cùng 1 ca.
- Nếu xảy ra, BE phải trả về mã lỗi `LECTURER_DOUBLE_BOOKED` hoặc `ROOM_DOUBLE_BOOKED`.

### 4.6. Kiểm soát Phân quyền (Role Scope Guards)
- Hệ thống chỉ hỗ trợ 4 role chuẩn: `ADMIN`, `DEPARTMENT_STAFF`, `LECTURER`, `STUDENT`.
- **Thẩm quyền Override Gate 3:** Chỉ `DEPARTMENT_STAFF` thuộc bộ môn quản lý mới có quyền ghi đè (`POST /api/project-gates/{id}/override`). Quản trị viên hệ thống (`ADMIN`) không có thẩm quyền học thuật để tự ý ghi đè nếu không thuộc bộ môn.

### 4.7. Phân bổ Giảng viên Chấm: Gate 1, 2, 3 (Hội đồng Cố định) & Gate 4 (Kế thừa Tính Liên tục)
Quy chuẩn học thuật FPTU / PMS phân định rõ ràng giữa các Cổng đánh giá tiến độ (Gate 1, 2, 3) và Hội đồng bảo vệ chính thức (Gate 4):

#### 1. Nguyên tắc chống trùng lịch & xung đột lợi ích (Non-overlap & Conflict Prevention)
- **Chống trùng ca chấm (Double-booking):** Một giảng viên tại một thời điểm $Slot(d, n)$ chỉ được tham gia tối đa **một** hội đồng (kể cả phiên Gate Session Script hay phiên Defense Session). Hệ thống tự động kiểm tra giao thoa thời gian qua `DefenseSchedulingRules.Overlaps`.
- **Chống xung đột quyền lợi:** Giảng viên hướng dẫn (Primary Supervisor / Co-supervisor) của đề tài tuyệt đối **không** được tham gia chấm bất kỳ Gate nào của đề tài mình hướng dẫn:
  $$\text{Reviewers}(P) \cap \text{Supervisors}(P) = \emptyset$$

#### 2. Gate 1, Gate 2, Gate 3: Hội đồng Cố định (Fixed Reviewer Panel - 2 người)
- **Quy mô:** Đúng **2 giảng viên** từ hội đồng cố định được bộ môn phân công cho nhóm ngay từ đầu kỳ.
- **Cơ cấu chức danh:**
  - 1 Chủ tịch hội đồng (`CHAIR`)
  - 1 Thư ký hội đồng (`SECRETARY`)
- **Tính ổn định xuyên suốt (Stability):** 2 giảng viên này theo sát nhóm từ Gate 1 (Thẩm định đề cương) qua Gate 2 (Đánh giá điều kiện) đến Gate 3 (Hội đồng đánh giá tiến độ chuyên môn).
- **Ý nghĩa:** Tránh hiện tượng thay đổi hội đồng giữa chừng dẫn đến việc "bác bỏ ý kiến của người trước", đảm bảo việc giám sát các hạng mục khắc phục (`remediation_items`) được thực thi nghiêm túc và nhất quán.

#### 3. Gate 4: Hội đồng Bảo vệ Tốt nghiệp Chính thức (Final Defense Committee - 4 người)
- **Quy mô:** Đúng **4 thành viên** thuộc chuyên ngành đào tạo (`DEFENSE_MAJOR_COVERAGE_REQUIRED`).
- **Cơ cấu chức danh:**
  - 1 Chủ tịch hội đồng (`CHAIR`)
  - 1 Thư ký hội đồng (`SECRETARY`)
  - 2 Ủy viên phản biện (`MEMBER`)
- **Quy tắc chuyển tiếp kế thừa (Continuity & Objectivity Rule):**
  - **Bắt buộc có ít nhất 1 người (thông thường là 1 người)** trong 4 thành viên hội đồng Gate 4 **đã từng tham gia chấm đề tài này ở các Gate trước (Gate 1, 2 hoặc 3)**:
    $$\text{Members}(Gate_4) \cap \left(\bigcup_{i=1}^3 \text{Reviewers}(Gate_i)\right) \ge 1$$
  - **3 thành viên còn lại** là các giảng viên độc lập/mới để đảm bảo tính khách quan tuyệt đối, đa chiều và công tâm cho buổi bảo vệ tốt nghiệp.
  - Thành viên kế thừa đóng vai trò thẩm định xem nhóm đã hoàn thiện toàn bộ các khuyến nghị và biên bản sửa chữa từ Gate 3 hay chưa.
- **Triển khai kỹ thuật:**
  - Logic xác thực đã được đóng gói tại `DefenseSchedulingRules`:
    + `DefenseSchedulingRules.IsValidGateReviewerPanel(gateSequence, reviewerCount, hasChair, hasSecretary)`
    + `DefenseSchedulingRules.IsValidGate4CommitteeSize(memberCount)`
    + `DefenseSchedulingRules.HasGateContinuity(gate4MemberIds, previousGateReviewerIds)`
  - Đã được kiểm thử 100% trong `DefenseSchedulingRulesTests.cs` (1.298 / 1.298 tests pass).

### 4.8. Mô hình Đánh giá Tổng thể: Thống nhất Thành viên Hội đồng là "Evaluator" Chấm theo Quy trình, Rubrics và Phân công
Mọi thành viên tham gia hội đồng chấm (kể cả hội đồng Gate 1–3 cố định hay hội đồng bảo vệ Gate 4: Chủ tịch, Thư ký, Uỷ viên phản biện) **đều được quy chung về một thực thể thống nhất là Evaluator (Người đánh giá / Cán bộ chấm điểm)**:

```
                  ┌──────────────────────────────────────────────┐
                  │    EVALUATOR (Thành viên Hội đồng chấm)      │
                  │   - Role: CHAIR, SECRETARY, REVIEWER/MEMBER   │
                  └──────────────────────┬───────────────────────┘
                                         │
                 ┌───────────────────────┼────────────────────────┐
                 ▼                       ▼                        ▼
     ┌───────────────────────┐ ┌───────────────────┐ ┌────────────────────────┐
     │ 1. QUY TRÌNH (Process)│ │ 2. RUBRICS CÓ SẴN │ │ 3. NHÓM ĐƯỢC PHÂN CÔNG │
     │ - Các Gate: 1, 2, 3, 4│ │ - Bộ tiêu chí mẫu │ │ - Đúng Project Id      │
     │ - Phiên chấm (Script) │ │ - Trọng số (%)    │ │ - Đúng Scope:          │
     │ - Agenda, Q&A, Verdict│ │ - Quality Levels  │ │   COMMON/MAJOR/INDIVID.│
     │ - Biên bản sửa chữa   │ │ - Lưu Draft & Chốt│ │ - Cô lập bảo mật dữ liệu│
     └───────────────────────┘ └───────────────────┘ └────────────────────────┘
```

#### 1. Căn cứ Quy trình (Process / Workflow Driven)
- Evaluator thực hiện nhiệm vụ theo đúng quy trình học thuật chuẩn hóa:
  - **Theo các Gate thẩm định:** Gate 1 (Thẩm định đề cương) $\to$ Gate 2 (Đánh giá điều kiện) $\to$ Gate 3 (Hội đồng chuyên môn trước bảo vệ) $\to$ Gate 4 (Bảo vệ tốt nghiệp chính thức).
  - **Theo kịch bản phiên đánh giá (`GateSessionScript`):**
    - Điểm danh (`presences_json`).
    - Các chặng nghị sự (`agenda_stages_json`: Thuyết trình, Demo sản phẩm, Hỏi đáp phản biện Q&A, Họp kín hội đồng).
    - Biên bản hỏi đáp (`qa_log_json`) và các hạng mục sinh viên phải khắc phục (`remediation_items_json`).
    - Kết luận chung của phiên (`verdict`: `PASSED`, `REMEDY_APPROVED`, `FAILED`).

#### 2. Căn cứ Rubrics Có sẵn (Predefined Rubrics & Quality Levels)
- Mọi thao tác cho điểm đều phải gắn chặt với một **Bộ tiêu chí (Rubric)** đã được phê duyệt sẵn trên hệ thống (`rubricId`, `rubricVersion`):
  - **Cấu trúc phân tầng (Hierarchy):** Tiêu chí cha (`Parent Criterion`) quản lý các tiêu chí con (`Leaf Criterion`).
  - **Trọng số (%):** Tổng trọng số các tiêu chí con phải bằng 100% của tiêu chí cha; tổng tiêu chí gốc bằng 100% điểm đồ án.
  - **Các mức chất lượng (Quality Levels):** Xuất sắc (Exemplary), Giỏi (Proficient), Đạt (Developing), Không đạt (Unacceptable) kèm dải điểm và mô tả hướng dẫn chi tiết (`ScoringGuideDrawer`, `CriterionLevelPanel`).
  - **Cơ chế chấm:** Evaluator nhập điểm từng tiêu chí, ghi chú nhận xét, lưu bản nháp (`DRAFT`), xem trước báo cáo minh chứng (`ReportPreviewPanel`) và chỉ gửi chốt (`FINALIZED`) khi đã hoàn tất.

#### 3. Căn cứ Nhóm được Phân công (Strictly Scoped Assignment)
- Evaluator **chỉ được phép xem và chấm điểm cho đúng các nhóm đề tài được phân công chính thức** (`EvaluationAssignment` hoặc `GateReviewerAssignment`):
  - **Phạm vi đánh giá rõ ràng:**
    - `COMMON`: Đánh giá sản phẩm/báo cáo chung của toàn bộ nhóm đề tài.
    - `MAJOR_SPECIFIC`: Đánh giá các phần việc chuyên sâu theo từng ngành (trong đồ án liên ngành CNTT + Thiết kế đồ họa + Quản trị kinh doanh).
    - `INDIVIDUAL`: Đánh giá đóng góp độc lập của từng sinh viên trong nhóm.
  - **Cô lập bảo mật:** Evaluator đăng nhập vào màn hình cá nhân `/evaluator/workspace` hoặc `/evaluator/assignments` chỉ thấy các đề tài mình phụ trách, tuyệt đối không thể can thiệp hoặc xem điểm của nhóm khác khi chưa được phân công.

---

## 5. BƯỚC 4: ĐẶC TẢ API, KHÓA LẠC QUAN & 10 MÃ LỖI XUNG ĐỘT

### 5.1. Bảng 10 Mã Lỗi Xung đột & Thông điệp Tiếng Việt Chuẩn mực:

| STT | Mã lỗi (ErrorCode) | HTTP Status | Thông điệp Tiếng Việt hiển thị trên UI |
| :---: | :--- | :---: | :--- |
| 1 | `SUPERVISOR_CONFLICT` | 422 | Giảng viên hướng dẫn không được ngồi hội đồng của đề tài do mình hướng dẫn. |
| 2 | `LECTURER_DOUBLE_BOOKED` | 409 | Giảng viên đã có lịch chấm hội đồng khác trong cùng ca bảo vệ. |
| 3 | `ROOM_DOUBLE_BOOKED` | 409 | Phòng bảo vệ đã được xếp cho một hội đồng khác trong cùng ca này. |
| 4 | `ROOM_INACTIVE_OR_CAPACITY` | 422 | Phòng bảo vệ đang bảo trì/ngừng hoạt động hoặc sức chứa không đáp ứng yêu cầu. |
| 5 | `SLOT_EXCLUDED` | 422 | Ca bảo vệ nằm trong danh sách loại trừ của đợt phân bổ lịch này. |
| 6 | `STUDENT_NOT_ELIGIBLE_GATE` | 422 | Đề tài/Sinh viên chưa đạt điều kiện tại Gate trước đó để tham gia Gate này. |
| 7 | `PERIOD_HOLIDAY_CONFLICT` | 422 | Ngày xếp lịch trùng vào ngày nghỉ lễ đã được cấu hình trong học kỳ. |
| 8 | `OUTSIDE_DEFENSE_WINDOW` | 422 | Thời gian bảo vệ nằm ngoài khoảng thời gian quy định của đợt bảo vệ (Defense Window). |
| 9 | `CONCURRENT_MODIFICATION_STALE` | 409 | Dữ liệu đã bị thay đổi bởi người dùng khác. Vui lòng tải lại trang để lấy phiên bản mới nhất. |
| 10 | `UNAUTHORIZED_ROLE_ACTION` | 403 | Bạn không có quyền thực hiện thao tác nghiệp vụ này với vai trò hiện tại. |

---

### 5.2. Cơ chế Khóa Lạc Quan (Optimistic Locking & Concurrency Token)
- Mọi thực thể nhạy cảm (`rooms`, `defense_committees`, `gate_session_scripts`, `defense_schedule_change_requests`) đều có cột `concurrency_token uniqueidentifier NOT NULL DEFAULT NEWID()`.
- Khi client gửi yêu cầu cập nhật (`PUT / POST`), client bắt buộc phải gửi trường `concurrencyToken` đã đọc được trước đó.
- Nếu token gửi lên khác với token trong DB:
  - BE hủy bỏ thao tác ngay lập tức.
  - Trả về HTTP `409 Conflict` với body:
    ```json
    {
      "errorCode": "CONCURRENT_MODIFICATION_STALE",
      "message": "Dữ liệu đã bị sửa đổi bởi người dùng khác. Vui lòng làm mới trang.",
      "serverConcurrencyToken": "a3b8c9d0-1234-4567-89ab-cdef01234567"
    }
    ```

---

### 5.3. Đặc tả Các Endpoints Cốt lõi:

#### 1. Xếp lịch Tự động (Auto-Schedule)
- **Endpoint:** `POST /api/defense-sessions/auto-schedule`
- **Quyền:** `ADMIN`, `DEPARTMENT_STAFF`
- **Request Body:**
  ```json
  {
    "projectPeriodId": 12,
    "gateSequence": 3,
    "startDate": "2026-11-23",
    "endDate": "2026-11-27",
    "excludedRoomIds": [5, 6],
    "excludedSlotIds": [1, 6]
  }
  ```
- **Response Success (200 OK):**
  ```json
  {
    "scheduledSessionsCount": 14,
    "unassignedProjectsCount": 0,
    "warnings": []
  }
  ```

#### 2. Công bố Lịch Bảo vệ Gate 3 (Publish Gate 3 Schedule)
- **Endpoint:** `POST /api/gate-sessions/gate3/publish`
- **Quyền:** `ADMIN`, `DEPARTMENT_STAFF`
- **Request Body:**
  ```json
  {
    "projectPeriodId": 12,
    "publishAll": true,
    "gateSessionScriptIds": [101, 102, 103]
  }
  ```
- **Response Success (200 OK):**
  ```json
  {
    "publishedCount": 3,
    "publishedAt": "2026-11-08T18:00:00+07:00",
    "publishedBy": 45,
    "publisherName": "Nguyễn Văn A (Cán bộ bộ môn)"
  }
  ```
- *Lưu ý:* Phải cập nhật `published_at = SYSDATETIMEOFFSET()` và `published_by = @CurrentUserId` vào bảng `gate_session_scripts`, đồng thời kích hoạt gửi thông báo (Notifications) cho Giảng viên và Sinh viên liên quan.

#### 3. Ghi đè Kết quả Gate (Academic Override)
- **Endpoint:** `POST /api/project-gates/{gateId}/override`
- **Quyền:** `DEPARTMENT_STAFF`
- **Request Body:**
  ```json
  {
    "newStatus": "PASSED",
    "overrideReason": "Đã hoàn thành đầy đủ báo cáo sửa đổi và có xác nhận của hội đồng chuyên môn."
  }
  ```
- **Response Success (200 OK):**
  ```json
  {
    "gateId": 88,
    "decisionSource": "OVERRIDDEN",
    "overriddenBy": 45,
    "overriddenAt": "2026-11-08T18:05:00+07:00",
    "status": "PASSED"
  }
  ```

#### 4. Đơn Xin Đổi Lịch / Phòng Bảo vệ (Change Request)
- **Endpoint:** `POST /api/defense-schedule-change-requests`
- **Quyền:** `LECTURER`, `STUDENT`
- **Request Body:**
  ```json
  {
    "defenseSessionId": 501,
    "desiredRoomId": 3,
    "desiredStartAt": "2026-11-24T09:00:00+07:00",
    "desiredEndAt": "2026-11-24T10:15:00+07:00",
    "reasonCategory": "ROOM_FACILITY_ISSUE",
    "reasonDetails": "Phòng cũ bị hỏng máy chiếu, nhóm cần phòng có bảng thông minh.",
    "evidenceFileUrl": "https://storage.fpt.edu.vn/evidence/req_501.pdf"
  }
  ```
- **Response Success (201 Created):**
  ```json
  {
    "id": 12,
    "status": "PENDING",
    "concurrencyToken": "f47ac10b-58cc-4372-a567-0e02b2c3d479"
  }
  ```

---

## 6. BƯỚC 5: TRÌNH TỰ THỰC HIỆN 12 KỊCH BẢN KIỂM THỬ E2E / UAT

BE DEV và Tester phối hợp thực hiện tuần tự 12 kịch bản sau để đảm bảo hệ thống đạt chuẩn nghiệm thu:

### Kịch bản 1: Cấu hình Phòng Tầng 2–4 & Lịch Học Kỳ Có Ngày Nghỉ
- **Mục tiêu:** Xác nhận hệ thống quản lý đầy đủ các phòng thi tầng 2-4 và tích hợp ngày nghỉ lễ của kỳ học.
- **Thao tác:**
  1. Gọi `GET /api/rooms` -> Kiểm tra trả về danh sách có các phòng `ALPHA_201`, `BETA_301`, `GAMMA_401` với trạng thái `ACTIVE`.
  2. Gọi `GET /api/project-periods/{id}/calendar` -> Trả về `holidays_json` chứa ngày nghỉ lễ `2026-11-20`.
- **Kết quả mong đợi:** Danh sách phòng và lịch trả về đúng định dạng, không có lỗi 500.

### Kịch bản 2: Tự động Xếp Lịch với Loại Trừ Phòng & Ca (Per-run Exclusions)
- **Mục tiêu:** Đảm bảo khi gửi `excludedRoomIds: [ALPHA_202]` và `excludedSlotIds: [Slot 1]`, thuật toán không phân công vào phòng/ca này nhưng DB không bị đổi status.
- **Thao tác:**
  1. Gửi request `POST /api/defense-sessions/auto-schedule` với danh sách loại trừ.
  2. Kiểm tra các phiên được tạo ra trong `defense_sessions`.
  3. Kiểm tra lại `SELECT status FROM dbo.rooms WHERE code = 'ALPHA_202'`.
- **Kết quả mong đợi:** Không có phiên nào rơi vào `ALPHA_202` hoặc `Slot 1`. Trạng thái của `ALPHA_202` trong DB vẫn là `'ACTIVE'`.

### Kịch bản 3: Chặn Xung Đột Giảng Viên Hướng Dẫn (Supervisor Conflict)
- **Mục tiêu:** Ngăn chặn việc xếp GVHD vào hội đồng chấm chính đề tài của mình.
- **Thao tác:** Thử tạo/cập nhật hội đồng với thành viên là giảng viên hướng dẫn của đề tài đó qua `PUT /api/defense-committees/{id}`.
- **Kết quả mong đợi:** Trả về HTTP 422 với mã `SUPERVISOR_CONFLICT`.

### Kịch bản 4: Chặn Trùng Lịch Giảng Viên & Trùng Phòng (Double-Booking Prevention)
- **Mục tiêu:** Không cho phép cùng 1 phòng hoặc cùng 1 giảng viên bị xếp vào 2 hội đồng trong cùng 1 ca.
- **Thao tác:** Tạo phiên bảo vệ thứ 2 sử dụng cùng `room_id = 1` và cùng khung giờ với phiên thứ 1.
- **Kết quả mong đợi:** Trả về HTTP 409 với mã `ROOM_DOUBLE_BOOKED` hoặc `LECTURER_DOUBLE_BOOKED`.

### Kịch bản 5: Ràng Buộc Điều Kiện Tuần Tự Gate 3 -> Gate 4 (Gate Eligibility)
- **Mục tiêu:** Đề tài chưa Đạt ở Gate 3 thì không được xếp lịch tại Gate 4.
- **Thao tác:** Chọn một đề tài có kết quả Gate 3 là `FAILED` hoặc chưa có kết quả, thử phân lịch bảo vệ Gate 4.
- **Kết quả mong đợi:** Trả về HTTP 422 với mã `STUDENT_NOT_ELIGIBLE_GATE`.

### Kịch bản 6: Ghi Đè Quyền Học Thuật Gate 3 (Review Gate Academic Override)
- **Mục tiêu:** Cán bộ bộ môn có thẩm quyền giải quyết trường hợp đặc biệt để đề tài đủ điều kiện sang Gate 4.
- **Thao tác:** Đăng nhập tài khoản `DEPARTMENT_STAFF`, gọi `POST /api/project-gates/{gateId}/override` với lý do cụ thể.
- **Kết quả mong đợi:** Trường `decision_source` đổi thành `'OVERRIDDEN'`, `overridden_by` ghi nhận ID người dùng. Sau đó, đề tài có thể xếp lịch Gate 4 thành công.

### Kịch bản 7: Rút Đơn Phúc Khảo (Withdrawn Appeals Workflow)
- **Mục tiêu:** Cho phép sinh viên rút đơn phúc khảo trước khi hội đồng tiếp nhận xem xét.
- **Thao tác:** Sinh viên gửi `POST /api/result-appeals/{appealId}/withdraw`.
- **Kết quả mong đợi:** Trạng thái đơn trong bảng `result_appeal_requests` chuyển sang `'WITHDRAWN'`. Ràng buộc `ck_appeal_request_status` chấp thuận trạng thái này.

### Kịch bản 8: Yêu Cầu Thay Đổi Lịch / Phòng Bảo Vệ (Defense Change Request)
- **Mục tiêu:** Sinh viên/Giảng viên nộp đơn xin đổi phòng/ca, Cán bộ phê duyệt và cập nhật lịch tự động.
- **Thao tác:**
  1. Tạo đơn đổi qua `POST /api/defense-schedule-change-requests`.
  2. Cán bộ bộ môn duyệt đơn qua `PUT /api/defense-schedule-change-requests/{id}/review` với `status: 'APPROVED'`.
- **Kết quả mong đợi:** Trạng thái đơn thành `'APPROVED'`, phiên bảo vệ tương ứng được tự động cập nhật sang phòng và giờ mới.

### Kịch bản 9: Công Bố Lịch Bảo Vệ Gate 3 (Publish Gate 3 Schedule)
- **Mục tiêu:** Xác nhận audit trail khi công bố lịch và thông báo được gửi đi.
- **Thao tác:** Gọi `POST /api/gate-sessions/gate3/publish`.
- **Kết quả mong đợi:** Cột `published_at` và `published_by` trong `gate_session_scripts` được điền đầy đủ. Lịch hiển thị trạng thái "ĐÃ CÔNG BỐ" trên UI sinh viên và giảng viên.

### Kịch bản 10: Xung Đột Sửa Đổi Đồng Thời (Optimistic Locking Concurrency 409)
- **Mục tiêu:** Kiểm tra xử lý xung đột khi 2 người cùng chỉnh sửa 1 phiên bảo vệ.
- **Thao tác:**
  1. Người dùng A và B cùng mở màn hình chỉnh sửa phiên hội đồng.
  2. Người dùng A lưu thay đổi thành công (Token cập nhật).
  3. Người dùng B nhấn lưu với token cũ.
- **Kết quả mong đợi:** Request của B bị từ chối với HTTP 409 `CONCURRENT_MODIFICATION_STALE`. Giao diện hiển thị thông báo yêu cầu tải lại dữ liệu mới nhất.

### Kịch bản 11: Kiểm Tra Phân Quyền Vai Trò (Role Scope Guards)
- **Mục tiêu:** Đảm bảo `ADMIN` không thể can thiệp vào thẩm quyền riêng của `DEPARTMENT_STAFF` và ngược lại.
- **Thao tác:** Đăng nhập tài khoản `ADMIN`, cố tình gọi endpoint `POST /api/project-gates/{gateId}/override`.
- **Kết quả mong đợi:** Trả về HTTP 403 `UNAUTHORIZED_ROLE_ACTION`.

### Kịch bản 12: Nghiệm Thu Toàn Trọn Vẹn Luồng E2E Gate 3 -> Gate 4
- **Mục tiêu:** Thực hiện một luồng hoàn chỉnh từ lúc xếp lịch Gate 3, bảo vệ, nhập điểm, công bố, chuyển tiếp Gate 4 và kết thúc đợt bảo vệ.
- **Kết quả mong đợi:** Toàn bộ dữ liệu hiển thị đồng nhất trên giao diện Web PMS (FAMS UI), không có bất kỳ lỗi JavaScript Console hay HTTP 500 nào.

---

## 7. BƯỚC 6: KHỞI CHẠY KIỂM THỬ KẾT NỐI THỰC TẾ (LOCAL E2E)

### Khởi chạy Backend (.NET 8):
```bash
cd F:\AI-PMS\ai-pms-backend-be16-20261107\src\AiPms.Api
dotnet run
# Mặc định lắng nghe tại: https://localhost:7154 hoặc http://localhost:5154
```

### Khởi chạy Frontend (Vite + React):
```bash
cd F:\AI-PMS\ai-pms-frontend-uiux-20261106
pnpm install
pnpm dev
# Mặc định mở tại: http://localhost:5173
```

---

## 8. BƯỚC 7: BIÊN BẢN NGHIỆM THU UAT & TIÊU CHÍ KÝ DUYỆT (SIGN-OFF)

| Vai trò nghiệm thu | Đại diện kiểm thử | Tiêu chí đánh giá | Trạng thái ký duyệt |
| :--- | :--- | :--- | :---: |
| **Quản trị viên (Admin)** | Đại diện IT / Hệ thống | Schema chạy đủ 7 file, không phát sinh lỗi dữ liệu, cấu hình phòng và kỳ học ổn định. | `[   ] CHẤP THUẬN` |
| **Cán bộ bộ môn (Staff)** | Đại diện Văn phòng Bộ môn | Auto-scheduling chuẩn 6 ca, loại trừ phòng/ca đúng nghiệp vụ, duyệt đổi lịch và override Gate 3 thông suốt. | `[   ] CHẤP THUẬN` |
| **Giảng viên (Lecturer)** | Đại diện Hội đồng chấm | Nhận thông báo lịch bảo vệ, không bị trùng lịch, không ngồi hội đồng nhóm mình hướng dẫn, xem biên bản phiên chấm đầy đủ. | `[   ] CHẤP THUẬN` |
| **Sinh viên (Student)** | Đại diện Nhóm làm Đồ án | Xem lịch bảo vệ Gate 3/4 sau khi công bố, nộp đơn xin đổi lịch/phúc khảo mượt mà. | `[   ] CHẤP THUẬN` |
| **Kỹ sư Đảm bảo Chất lượng (QA)** | Lead QA / Test Engineer | 12 kịch bản E2E kiểm thử đạt 100%, FE 2.085 tests pass, BE 1.285 tests pass, không lỗi HTTP 500. | `[   ] CHẤP THUẬN` |

---
*Cập nhật 08/11/2026: đã xác nhận migration, đăng nhập 4 vai trò và smoke test FE–BE cho quản lý phòng/lập lịch xem trước; nghiệm thu đủ 12 kịch bản và ký duyệt UAT còn chờ fixture học thuật hợp lệ cùng kiểm thử thực tế.*
