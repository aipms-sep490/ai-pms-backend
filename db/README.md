# AI-PMS Database Schema & Migration Rules

## Quy tắc đồng bộ 3 tệp bắt buộc (`TK-W0-12`)
Mọi thay đổi cấu trúc CSDL (DDL) **bắt buộc** phải cập nhật đồng bộ cả 3 vị trí trong cùng một Pull Request:
1. **Migration idempotent:** `db/changes/YYYYMMDD_<mo_ta>.sql` (phải chạy lại được nhiều lần không lỗi).
2. **Danh sách manifest:** đăng ký tên tệp vào `db/e2e/migrations.json` đúng thứ tự thực thi.
3. **Bản dựng gốc sạch:** cập nhật tương ứng vào `db/schema.sql` để dựng CSDL mới chỉ bằng `db/schema.sql` khớp 100% với `db/schema.sql` + toàn bộ `db/e2e/migrations.json`.

## Bảo vệ dữ liệu Demo & Chẩn đoán (`TK-W0-06`)
- Thư mục `db/changes/` chỉ chứa **100% script migration DDL/chuẩn** khớp 1–1 với `db/e2e/migrations.json`. Không bao giờ đặt tài khoản demo/mật khẩu yếu vào `db/changes/`.
- Script seed demo phát triển cục bộ đặt tại `db/demo/`.
- Script truy vấn chẩn đoán chỉ đọc đặt tại `db/diagnostics/`.

## Bảng đã đánh dấu DEPRECATED
- 6 bảng `dbo.industry_*` (`industry_experts`, `industry_invitations`, `industry_challenges`, `industry_assignments`, `industry_evidence_shares`, `industry_feedback`) nằm ngoài phạm vi lõi V4 và đã được gắn `MS_Description = 'DEPRECATED: ...'` — **không tham chiếu trong mã mới**.

## Kiểm thử bắt buộc
Chạy `SchemaParityTests` (`FreshSchemaSqlAlone_MatchesSchemaPlusAllMigrations_AndIndustryTablesDeprecated`, `Fresh_upgrade_and_rerun_have_identical_schema_and_preserve_snapshots`, `DbChangesDirectory_MatchesMigrationsManifest_AndContainsNoDemoPasswords`) trước khi merge.
