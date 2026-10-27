# Hướng Dẫn Dữ Liệu Seed Đồ Án Đơn Ngành (Single-Major Active Capstone Project)

Tài liệu hướng dẫn triển khai và kiểm thử bộ dữ liệu mẫu đồ án tốt nghiệp **ĐƠN NGÀNH (Single-Major)** trên hệ thống quản lý đồ án thông minh **AI-PMS** (FPT University Đà Nẵng).

---

## 1. Tổng Quan Kịch Bản Nghiệp Vụ

Dữ liệu seed đại diện cho **duy nhất 01 nhóm đồ án mẫu** thuộc chuyên ngành **Kỹ thuật Phần mềm (Software Engineering - SE)**, học kỳ **FA26 (Fall 2026)**, hiện đang trong giai đoạn thực hiện chính thức (**`Project.State = ACTIVE`**, chu kỳ **`SEP490-FA26-EXEC`**).

### Bảng Thông Tin Định Danh

| Chỉ mục | Thông tin chi tiết |
| :--- | :--- |
| **Định danh Seed** | `DEMO-SINGLE-MAJOR-ACTIVE-V1` |
| **Mã đồ án (Project Code)** | `SEP490-FA26-SE01` (ID: `3`) |
| **Tên đề tài (Tiếng Việt)** | **Nền tảng Tự động hóa Kiểm thử và Đánh giá Chất lượng Phần mềm Phân tán** |
| **Tên đề tài (Tiếng Anh)** | **Distributed QA Automation Platform** |
| **Chuyên ngành (Major)** | **Kỹ thuật Phần mềm (Software Engineering - SE)** (`major_id = 1`) |
| **Mã nhóm (Team Code)** | `SEP490_G03` (ID: `3`, trạng thái `LOCKED`) |
| **Trạng thái đồ án** | **`ACTIVE`** (Giai đoạn thực hiện `EXECUTION`) |
| **Học kỳ (Semester)** | `FA26` (Fall 2026, ID: `1`) |
| **Khoa / Bộ môn chủ quản** | Bộ môn Kỹ thuật Phần mềm — Khoa Công nghệ Thông tin (`IT`, ID: `1`) |
| **Giảng viên Hướng dẫn chính** | **ThS. Đỗ Quang Huy** (`lecturer.se02@fe.edu.vn` - Profile ID: `5`) |
| **Cán bộ Bộ môn thẩm định** | **Lê Thu Hà** (`sep490.coordinator@fpt.edu.vn` - IT Department Staff) |

---

## 2. Đặc Thù Luồng Đơn Ngành (Single-Major Workflow)

So với các đề tài liên ngành (Interdisciplinary) như Project 1 (`SEP490-FA26-AIPMS`) và Project 2 (`SEP490-FA26-LOGISTICS`), đề tài đơn ngành SE tuân thủ nghiêm ngặt các quy tắc học thuật:

1. **Roster sinh viên đồng nhất**: 100% thành viên nhóm (4 sinh viên) đều thuộc chuyên ngành Kỹ thuật Phần mềm (`SE`).
2. **Cấu hình học thuật nhóm (`team_academic_configurations`)**:
   - `project_mode = 'SINGLE_MAJOR'`
   - `primary_major_id = 1` (SE)
   - `lead_department_id = 1` (IT)
3. **Liên kết chuyên ngành (`project_majors`)**:
   - Chỉ chứa **duy nhất 01 bản ghi** liên kết đến `major_id = 1` (SE). Không tồn tại cấu hình đa ngành.
4. **Giảng viên hướng dẫn (`supervisor_assignments` & `supervisor_requests`)**:
   - Chỉ có **01 Giảng viên Hướng dẫn chính (PRIMARY)** chuyên môn SE.
   - `assignment_type = 'PRIMARY'`, `is_primary = 1`, `major_id = NULL`.
   - **Tuyệt đối không có Mentor bộ môn chéo (`DISCIPLINE_MENTOR`)** do toàn bộ khối lượng công việc tập trung vào chuẩn đầu ra chuyên sâu ngành SE.

---

## 3. Lịch Sử Phê Duyệt Học Thuật (Audit Trail - 6 Bước)

Bảng `dbo.project_status_history` lưu vết đầy đủ chu trình xét duyệt từ sinh viên đến bộ môn:

| Bước | Chuyển dịch trạng thái | Tác nhân thực hiện | Lý do nghiệp vụ | Thời gian |
| :---: | :--- | :--- | :--- | :--- |
| **1** | `START` &rarr; `DRAFT` | Nguyễn Văn Hùng *(Leader)* | Trưởng nhóm khởi tạo hồ sơ đăng ký đề tài đồ án tốt nghiệp SE. | 21/08/2026 09:00 |
| **2** | `DRAFT` &rarr; `SUBMITTED` | Nguyễn Văn Hùng *(Leader)* | Nhóm hoàn thiện đề cương chi tiết đề tài và gửi Bộ môn xét duyệt. | 28/08/2026 15:00 |
| **3** | `SUBMITTED` &rarr; `UNDER_REVIEW` | Lê Thu Hà *(Staff/Coordinator)* | Bộ môn tiếp nhận hồ sơ, phân công hội đồng chuyên môn thẩm định. | 02/09/2026 10:00 |
| **4** | `UNDER_REVIEW` &rarr; `APPROVED` | Lê Thu Hà *(Staff/Coordinator)* | Hội đồng Bộ môn thông qua đề tài, xác nhận thỏa mãn chuẩn đầu ra SE. | 12/09/2026 10:00 |
| **5** | `APPROVED` &rarr; `SUPERVISOR_PENDING` | Lê Thu Hà *(Staff/Coordinator)* | Hệ thống mở cổng kết nối và gửi yêu cầu hướng dẫn đến GVHD chính. | 12/09/2026 10:30 |
| **6** | `SUPERVISOR_PENDING` &rarr; `ACTIVE` | Lê Thu Hà *(Staff/Coordinator)* | GVHD (ThS. Đỗ Quang Huy) chấp thuận; Bộ môn kích hoạt vào giai đoạn thực hiện. | 15/09/2026 16:00 |

---

## 4. Dữ Liệu Thực Thi Đang Diễn Ra (In-Progress Execution State)

Dự án được khởi tạo với trạng thái đang phát triển thực tế, phục vụ preview giao diện sinh viên, giảng viên và bộ môn:

- **Thỏa ước làm việc nhóm (Working Agreement)**: Version 1 (`ACTIVE`), được **ký duyệt điện tử bởi toàn bộ 4 thành viên**.
- **Giai đoạn đồ án (Milestones)**:
  - *M1*: Phân tích Yêu cầu, Đặc tả Kịch bản Kiểm thử & Thiết kế Cơ sở (`COMPLETED`)
  - *M2*: Thiết kế Kiến trúc Hệ thống, API Orchestrator & Worker Agents (`COMPLETED`)
  - *M3*: Tự động hóa Thực thi Kịch bản Kiểm thử & Trực quan Báo cáo (`IN_PROGRESS` - *Đang thực hiện*)
  - *M4*: Tối ưu Hiệu năng, Đánh giá Bảo mật & Bảo vệ Đồ án (`PLANNED`)
- **Chu kỳ Agile Sprint**:
  - *Sprint 1*: Thiết kế Database, API Orchestrator & Core Authentication (`COMPLETED`)
  - *Sprint 2*: Triển khai Worker Agent Kiểm thử & Dashboard Giám sát (`ACTIVE` - *Đang chạy*)
  - *Sprint 3*: Tích hợp CI/CD Pipeline, Đo kiểm Tải & Báo cáo IEEE (`PLANNED`)
- **12 Nhiệm vụ Kanban & Story Points**: Phân bổ đều cho 4 thành viên theo các cột `DONE` (Sprint 1), `IN_PROGRESS`, `IN_REVIEW` (Sprint 2), và `TODO` (Sprint 3) kèm Story Points theo chuẩn Fibonacci (1, 2, 3, 5, 8).
- **Cuộc họp & Biên bản (Meetings)**: 3 cuộc họp đã diễn ra (`COMPLETED`) có điểm danh 100%, ghi nhận quyết định kỹ thuật và việc cần làm (action items); 1 cuộc họp sắp diễn ra (`SCHEDULED`).
- **Báo cáo tiến độ & Nhận xét GVHD (Reports & Feedbacks)**: 2 báo cáo tiến độ tuần/tháng đã được ThS. Đỗ Quang Huy đánh giá và cho nhận xét chi tiết; 1 báo cáo tuần 6 đang chờ duyệt (`SUBMITTED`).
- **Sản phẩm bàn giao & Tệp tin**: SRS v2.0 (`ACCEPTED`), Tài liệu kiến trúc v1.0 (`ACCEPTED`), Gói mã nguồn v1.0 (`SUBMITTED`) kèm mock file metadata.

---

## 5. Danh Mục Tài Khoản Thử Nghiệm

> [!IMPORTANT]
> Mật khẩu đăng nhập mặc định cho toàn bộ các tài khoản thử nghiệm: **`Aipms@123`**

| Vai trò trong đồ án | Họ và tên | Mã số (MSSV / Mã NV) | Email đăng nhập |
| :--- | :--- | :--- | :--- |
| **Trưởng nhóm (SE Leader / Backend)** | Nguyễn Văn Hùng | `SE180201` | `hungnvse180201@fpt.edu.vn` |
| **Thành viên (SE Member / Frontend)** | Trần Thị Mai Phương | `SE180202` | `phuongttmse180202@fpt.edu.vn` |
| **Thành viên (SE Member / DevOps)** | Lê Quang Huy | `SE180203` | `huylqse180203@fpt.edu.vn` |
| **Thành viên (SE Member / QA & Testing)** | Phạm Hải Đăng | `SE180204` | `dangphse180204@fpt.edu.vn` |
| **Giảng viên Hướng dẫn chính (PRIMARY)** | ThS. Đỗ Quang Huy | `GV-SE02` | `lecturer.se02@fe.edu.vn` |
| **Cán bộ Bộ môn CNTT (Coordinator)** | Lê Thu Hà | `STAFF-IT01` | `sep490.coordinator@fpt.edu.vn` |

---

## 6. Bộ Script Vận Hành (Scripts Suite)

Tất cả các script đều được lưu trữ trong thư mục `db/` và hỗ trợ đầy đủ tiếng Việt có dấu chuẩn Unicode (`N'...'`), encoding UTF-8 có BOM:

### 1. Kịch bản chạy thử nghiệm không ghi dữ liệu (Dry-Run)
Kiểm tra cú pháp, xác thực cấu hình học thuật và ràng buộc FK trong transaction, sau đó rollback an toàn 100%:
```powershell
sqlcmd -S localhost -d AI_PMS -E -i db/seed_single_major_active_project_dry_run.sql
```

### 2. Kịch bản khởi tạo an toàn (Safe Seed - Idempotent)
Thực hiện ghi dữ liệu nhóm và đồ án đơn ngành. Có cơ chế guard tự động dừng an toàn nếu đồ án đã tồn tại:
```powershell
sqlcmd -S localhost -d AI_PMS -E -i db/seed_single_major_active_project.sql
```

### 3. Kịch bản kiểm tra sau seed (Post-Seed Validation)
Truy vấn tự động kiểm tra toàn bộ 7 tiêu chí học thuật và dữ liệu thực thi:
```powershell
sqlcmd -S localhost -d AI_PMS -E -i db/validate_single_major_active_project.sql
```

### 4. Kịch bản gỡ bỏ dữ liệu an toàn (Surgical Cleanup / Rollback)
Chỉ xóa đúng các bản ghi thuộc bộ seed `SEP490-FA26-SE01` và 4 tài khoản sinh viên demo. **Tuyệt đối không ảnh hưởng đến Project 1 và Project 2**:
```powershell
sqlcmd -S localhost -d AI_PMS -E -i db/cleanup_single_major_active_project.sql
```

---

## 7. Cam Kết An Toàn Dữ Liệu & Hướng Dẫn Preview

1. **Bảo toàn dữ liệu hiện có (Zero Regression)**:
   - Toàn bộ dữ liệu của Project 1 (`SEP490-FA26-AIPMS`) và Project 2 (`SEP490-FA26-LOGISTICS`), cùng toàn bộ sinh viên/giảng viên hiện có trong DB hoàn toàn không bị thay đổi.
2. **Chuẩn hóa font chữ & Tiếng Việt**:
   - 100% dữ liệu chuỗi ký tự đều dùng kiểu `NVARCHAR` với tiền tố `N'...'`.
   - File script được mã hóa UTF-8 có BOM (`EF BB BF`), đảm bảo khi xem trên SQL Server Management Studio (SSMS), giao diện Web React hay REST API đều hiển thị tiếng Việt sắc nét, không bị lỗi `?` hay mã hóa lỗi.
3. **Triển khai môi trường Preview**:
   - Khi triển khai môi trường preview trên Docker hoặc Test Server, chạy script `seed_single_major_active_project.sql` sau khi hoàn tất migration schema cơ sở.
   - Đăng nhập bằng tài khoản `hungnvse180201@fpt.edu.vn` để xem giao diện sinh viên (Team Workspace, Sprint Kanban, Deliverables).
   - Đăng nhập bằng tài khoản `lecturer.se02@fe.edu.vn` để xem giao diện giảng viên (Đánh giá báo cáo tiến độ, Lịch họp, Phản hồi đồ án).
