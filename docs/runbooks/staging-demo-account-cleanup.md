# Runbook: Dọn dẹp 6 tài khoản Demo Design Department trên Staging (`TK-W0-06`)

> **Bối cảnh:** Script `20261024_add_design_department.sql` (chứa 6 tài khoản demo với mật khẩu yếu `Aipms@123`) đã được di chuyển khỏi `db/changes/` sang `db/demo/seed_design_department_demo.sql` để không bao giờ tự chạy lên Staging/Production.

## 1. Danh sách 6 email demo cần rà soát
1. `staff.design@fpt.edu.vn`
2. `primary.design@fe.edu.vn`
3. `mentor.design@fe.edu.vn`
4. `student.design1@fpt.edu.vn`
5. `student.design2@fpt.edu.vn`
6. `student.design3@fpt.edu.vn`

## 2. Bước 1 — Truy vấn chỉ đọc (Read-only Audit)
```sql
SELECT u.id, u.email, u.full_name, u.status, u.department_id, u.created_at
FROM dbo.users u
WHERE u.email IN (
    'staff.design@fpt.edu.vn',
    'primary.design@fe.edu.vn',
    'mentor.design@fe.edu.vn',
    'student.design1@fpt.edu.vn',
    'student.design2@fpt.edu.vn',
    'student.design3@fpt.edu.vn'
);
```

Kiểm tra các tài khoản này đã phát sinh dữ liệu ràng buộc FK chưa (`teams`, `team_members`, `supervisor_profiles`, `audit_logs`, `evaluations`):
```sql
SELECT u.id, u.email,
       (SELECT COUNT(*) FROM dbo.team_members tm WHERE tm.user_id = u.id) AS team_members_cnt,
       (SELECT COUNT(*) FROM dbo.supervisor_profiles sp WHERE sp.user_id = u.id) AS sup_profiles_cnt,
       (SELECT COUNT(*) FROM dbo.audit_logs al WHERE al.actor_user_id = u.id) AS audit_logs_cnt
FROM dbo.users u
WHERE u.email IN (
    'staff.design@fpt.edu.vn',
    'primary.design@fe.edu.vn',
    'mentor.design@fe.edu.vn',
    'student.design1@fpt.edu.vn',
    'student.design2@fpt.edu.vn',
    'student.design3@fpt.edu.vn'
);
```

## 3. Bước 2 — Vô hiệu hóa an toàn (khi đã có dữ liệu FK hoặc không muốn xóa cứng)
```sql
BEGIN TRAN;

UPDATE dbo.users
SET status = 'INACTIVE',
    password_hash = CONCAT('LOCKED_DEMO_', LOWER(CONVERT(varchar(36), NEWID()))),
    password_changed_at = SYSUTCDATETIME(),
    updated_at = SYSUTCDATETIME()
WHERE email IN (
    'staff.design@fpt.edu.vn',
    'primary.design@fe.edu.vn',
    'mentor.design@fe.edu.vn',
    'student.design1@fpt.edu.vn',
    'student.design2@fpt.edu.vn',
    'student.design3@fpt.edu.vn'
);

-- Thu hồi toàn bộ refresh token đang hoạt động (nếu có)
UPDATE rt
SET revoked_at = SYSUTCDATETIME()
FROM dbo.refresh_tokens rt
INNER JOIN dbo.users u ON u.id = rt.user_id
WHERE rt.revoked_at IS NULL
  AND u.email IN (
    'staff.design@fpt.edu.vn',
    'primary.design@fe.edu.vn',
    'mentor.design@fe.edu.vn',
    'student.design1@fpt.edu.vn',
    'student.design2@fpt.edu.vn',
    'student.design3@fpt.edu.vn'
  );

COMMIT;
```

## 4. Bước 3 — Xác nhận sau khi dọn
1. Chạy lại truy vấn ở Bước 1 → xác nhận mọi dòng đều `status = 'INACTIVE'` (hoặc 0 dòng nếu đã xóa sạch ở môi trường chưa có FK).
2. Thử đăng nhập `POST /api/v1/auth/login` với `staff.design@fpt.edu.vn` + `Aipms@123` → phải trả `401 Unauthorized` / `403 Forbidden`.
