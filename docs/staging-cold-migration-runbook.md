# HƯỚNG DẪN THỰC THI MIGRATION COLD SCHEMA TRÊN AI-PMS STAGING

Tài liệu này dành riêng cho Quản trị viên / DBA / DevOps để tự thực hiện cập nhật schema bảng `dbo.cold_report_templates` trên môi trường Staging SQL Server `AI_PMS`.

> **NGUYÊN TẮC AN TOÀN:**
> - Toàn bộ script đã được thiết kế mang tính **Additive** (chỉ thêm, không xóa), **Idempotent** (có thể chạy lại nhiều lần an toàn).
> - Tuyệt đối không chứa `DROP TABLE`, `TRUNCATE`, không xóa dữ liệu học thuật hiện hữu.
> - Không hardcode thông tin đăng nhập hoặc mật khẩu trong bất kỳ tài liệu/script nào.

---

## 1. TỔNG QUAN GÓI MIGRATION

| Tên File | Đường dẫn | Mục đích |
|---|---|---|
| **Preflight Diagnostic** | `db/changes/preflight_cold_template_scope.sql` | Kiểm tra trước migration (chỉ đọc, kiểm tra server, DB, bảng, cột, kiểu dữ liệu, quyền, xung đột dữ liệu). |
| **Migration DDL** | `db/changes/20261030_cold_template_common_scope.sql` | Thêm cột `scope NVARCHAR(32) NULL`, sửa `major_id BIGINT NULL`, thêm 2 check constraints. |
| **Postflight Verification** | `db/changes/postflight_cold_template_scope.sql` | Xác minh sau migration (kiểm tra kiểu dữ liệu, ràng buộc hoạt động, kiểm tra câu truy vấn EF Core). |
| **Rollback DDL** | `db/changes/rollback/20261030_cold_template_common_scope_rollback.sql` | Script hoàn tác an toàn (chỉ dùng khi phát sinh sự cố nghiêm trọng). |
| **Migration Runner** | `scripts/apply-academic-schema-release.ps1` | Runner tự động có backup `COPY_ONLY`, checksum verification và ghi nhận ledger sha256. |

---

## 2. ĐIỀU KIỆN TIÊN QUYẾT TRƯỚC KHI CHẠY (PREREQUISITES)

1. **Xác nhận Target Server & Database:**
   - Server: SQL Server Staging (Container hoặc Host chạy database Staging `AI_PMS`).
   - Database Name: `AI_PMS` (Không thực thi trên `master`, `tempdb`, hoặc database khác).
2. **Quyền hạn SQL Account:**
   - Yêu cầu quyền `ALTER` trên database `AI_PMS` để thêm cột và check constraints.
   - Yêu cầu quyền `BACKUP DATABASE` nếu sử dụng PowerShell Runner tự động.
3. **Sao lưu trước khi thực hiện (Backup):**
   - Trước khi thực hiện bất kỳ DDL nào, tạo một bản sao lưu `COPY_ONLY`:
     ```sql
     BACKUP DATABASE [AI_PMS] 
     TO DISK = '/var/opt/mssql/data/AI_PMS_pre_cold_scope_backup.bak' -- hoặc đường dẫn Windows tương ứng
     WITH COPY_ONLY, CHECKSUM, INIT, COMPRESSION;

     -- Kiểm tra tính toàn vẹn của bản sao lưu
     RESTORE VERIFYONLY 
     FROM DISK = '/var/opt/mssql/data/AI_PMS_pre_cold_scope_backup.bak' 
     WITH CHECKSUM;
     ```

---

## 3. BƯỚC 1 — CHẠY PREFLIGHT AUDIT

Trước khi áp dụng thay đổi, chạy script chẩn đoán `preflight_cold_template_scope.sql`. Script này **hoàn toàn chỉ đọc (READ-ONLY)**.

### Cách chạy với SSMS / Azure Data Studio:
1. Kết nối tới SQL Server Staging.
2. Chọn Database `AI_PMS`.
3. Mở file `db/changes/preflight_cold_template_scope.sql`.
4. Nhấn **Execute (F5)**.

### Cách chạy qua CLI (`sqlcmd` hoặc docker exec):
```bash
# Nếu chạy trên Docker Container:
docker exec -i <container_name> /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U <db_user> -P "<db_password>" -d AI_PMS -C \
  -i /path/to/db/changes/preflight_cold_template_scope.sql

# Nếu chạy trên Windows CLI:
sqlcmd -S "<server_name>" -d AI_PMS -E -i "D:\AI_PMS\ai-pms-backend\db\changes\preflight_cold_template_scope.sql"
```

### Kỳ vọng kết quả Preflight:
- `CurrentDatabase`: Phải là `AI_PMS`.
- `HasDatabaseAlterPermission`: Phải là `1`.
- `Table dbo.cold_report_templates exists`: Phải là `OK`.
- Kết luận: `VERDICT: READY_TO_APPLY` (nếu cột `scope` chưa có) hoặc `VERDICT: ALREADY_APPLIED` (nếu cột đã có sẵn).

---

## 4. BƯỚC 2 — THỰC HIỆN MIGRATION

Có 2 phương án thực hiện tùy thuộc vào quy trình vận hành của bạn:

---

### PHƯƠNG ÁN A — CHẠY BẰNG MIGRATION RUNNER TỰ ĐỘNG (KHUYẾN NGHỊ CAO NHẤT)

PowerShell runner `scripts/apply-academic-schema-release.ps1` được trang bị cơ chế bảo vệ cấp doanh nghiệp:
1. Xác thực chính xác tên Server và Database mục tiêu.
2. Tự động tạo bản backup `COPY_ONLY` có `CHECKSUM` và chạy `RESTORE VERIFYONLY`.
3. Kiểm tra mã băm SHA256 của từng file script.
4. Ghi nhận nhật ký di trú vào bảng `dbo.academic_schema_migration_ledger`.
5. Ngăn chặn việc chạy đè hoặc sửa đổi script đã áp dụng.

**Cách chạy trên PowerShell:**
```powershell
# 1. Đặt biến môi trường connection string (không ghi mật khẩu vào script hoặc git)
$env:AIPMS_MIGRATION_CONNECTION = "Server=<server_staging>;Database=AI_PMS;User Id=<user>;Password=<password>;TrustServerCertificate=True"

# 2. Điều hướng tới thư mục backend
cd D:\AI_PMS\ai-pms-backend

# 3. Chạy script runner với tham số xác nhận đích danh
.\scripts\apply-academic-schema-release.ps1 -ExpectedServer "<TÊN_SERVER_STAGING>" -ExpectedDatabase "AI_PMS"
```

**Đầu ra kỳ vọng:**
```text
BACKUP_VERIFIED <đường_dẫn_backup>
ALREADY_APPLIED 20261030_add_rubric_criterion_levels.sql
ALREADY_APPLIED 20261030_add_rubric_scope_fields.sql
APPLIED 20261030_cold_template_common_scope.sql
...
```

---

### PHƯƠNG ÁN B — CHẠY TRỰC TIẾP QUA SSMS HOẶC SQLCMD

Nếu không sử dụng runner tự động, bạn có thể thực thi trực tiếp file SQL:

1. **Mở file migration:** `db/changes/20261030_cold_template_common_scope.sql`.
2. **Kiểm tra kỹ Context:** Đảm bảo connection đang trỏ vào database `AI_PMS`.
3. **Thực thi (Execute):** Toàn bộ script nằm trong một transaction (`BEGIN TRY ... BEGIN TRANSACTION ... COMMIT ... CATCH ... ROLLBACK`).

#### QUAN TRỌNG VỀ ĐỒNG BỘ LEDGER KHI CHẠY THỦ CÔNG:
Nếu bạn chạy file SQL trực tiếp bằng SSMS/sqlcmd, bảng `dbo.academic_schema_migration_ledger` của hệ thống sẽ chưa được cập nhật dòng ghi nhận cho script này. Để runner PowerShell sau này nhận biết script đã được áp dụng (tránh cảnh báo hoặc áp dụng lại), hãy chạy thêm đoạn SQL sau:

```sql
IF OBJECT_ID(N'dbo.academic_schema_migration_ledger', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.academic_schema_migration_ledger WHERE script_name = N'20261030_cold_template_common_scope.sql')
    BEGIN
        INSERT INTO dbo.academic_schema_migration_ledger (script_name, sha256, applied_at, applied_by)
        VALUES (
            N'20261030_cold_template_common_scope.sql',
            -- Checksum chuẩn của file script (SHA256 tính theo UTF-8 không BOM, LF line endings)
            N'7c6954cca46af57a9d21cc7bd56e822e3f94ccef1824da257e3acfad77bcf251',
            SYSUTCDATETIME(),
            ORIGINAL_LOGIN()
        );
        PRINT '>> Synced migration ledger for 20261030_cold_template_common_scope.sql successfully.';
    END
END;
```

---

## 5. BƯỚC 3 — CHẠY POSTFLIGHT VERIFICATION

Sau khi chạy migration xong, chạy file `db/changes/postflight_cold_template_scope.sql`.

### Cách chạy:
Chạy bằng SSMS hoặc CLI tương tự như Bước 1.

### Kỳ vọng kết quả Postflight:
```text
  [PASS] Column [scope] exists: NVARCHAR(32) NULL (storage: 64 bytes).
  [PASS] Column [major_id] exists: BIGINT NULL.
  [PASS] Check constraint [ck_cold_template_scope] exists and is ACTIVE.
  [PASS] Check constraint [ck_cold_template_scope_major] exists and is ACTIVE.
  [PASS] EF Core cold_report_templates SELECT query executed successfully without SQL exceptions.
--------------------------------------------------------------------------------
 POSTFLIGHT VERDICT: SUCCESS (PASS)
 The Cold Report Template schema is 100% compatible with AI-PMS Backend Cold API.
 Expected API behavior: GET /api/v1/cold-report-templates returns HTTP 200 OK.
================================================================================
```

---

## 6. BƯỚC 4 — XÁC MINH COLD API TRÊN STAGING

Sau khi postflight đạt `PASS`, hãy kiểm tra API Staging trực tiếp bằng `curl` hoặc PowerShell:

```bash
# Đăng nhập lấy Bearer token của Staff IT:
# POST https://api-staging.khaidz.com/api/v1/auth/login
# Body: {"email": "staff.it@demo.aipms.invalid", "password": "<password>"}

# Kiểm tra endpoint Cold Report Templates:
curl -X GET "https://api-staging.khaidz.com/api/v1/cold-report-templates?projectPeriodId=30006&page=1&pageSize=20" \
  -H "Authorization: Bearer <STAFF_ACCESS_TOKEN>" \
  -H "Content-Type: application/json"
```

**Kỳ vọng:** Trả về **HTTP 200 OK** với cấu trúc JSON `PagedResult<ColdTemplateDto>` thay vì lỗi HTTP 500 `Invalid column name 'scope'`.

---

## 7. XỬ LÝ SỰ CỐ VÀ HƯỚNG DẪN ROLLBACK

### 1. Nếu migration báo lỗi khi đang chạy:
- Script đã được bọc trong khối `BEGIN TRANSACTION` và `ROLLBACK TRANSACTION` trong `CATCH`. Bất kỳ lỗi cú pháp hoặc vi phạm nào đều tự động hủy bỏ giao dịch, không làm thay đổi dở dang cấu trúc database.

### 2. Khi nào được phép dùng Rollback (`20261030_cold_template_common_scope_rollback.sql`):
- **Điều kiện an toàn:** Rollback script sẽ **từ chối thực thi** nếu trong database đã xuất hiện các bản ghi template mới có `major_id IS NULL` (thuộc scope `COMMON`). Điều này nhằm bảo vệ dữ liệu không bị lỗi `NOT NULL` constraint violation.
- Nếu thực sự cần rollback, phải xóa hoặc cập nhật các template có `major_id IS NULL` trước, sau đó mới chạy `db/changes/rollback/20261030_cold_template_common_scope_rollback.sql`.
- Nếu có sự cố bất khả kháng, khôi phục lại database từ bản sao lưu `AI_PMS_pre_cold_scope_backup.bak` đã tạo tại Bước 2.
