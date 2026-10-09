# AI-PMS — Kiểm kê chức năng (đã có / chưa có)

Ngày: 09/10/2026 · Mã nguồn: FE `develop` @ `9612e3c` (gồm PR #53 mới merge), BE `develop` @ `4c61c98` · DB `AI_PMS` đọc lúc 09/10/2026.

**Cách đọc trạng thái**
- ✅ **Chạy được**: có API BE, có màn hình FE, có dữ liệu thật trên DB.
- 🟢 **Có code đủ 2 phía, chưa có dữ liệu thật**: chưa ai chạy luồng này trên DB dùng chung, cần nghiệm thu.
- 🟡 **Một phía**: chỉ BE hoặc chỉ FE, hoặc đang chặn tạm vì lỗi phân quyền.
- ❌ **Chưa có**.

Tổng quan số liệu: BE có **56 controller, 331 endpoint**. FE gọi gần như toàn bộ; chỉ 3 nhóm endpoint chưa có màn hình (import khung chương trình, xuất danh sách nhóm, chụp snapshot đóng góp). FE có **66 trang**, khoảng 95 route.

---

## A. Chức năng theo tài liệu (SRS UC-001 … UC-134, CIB v4)

### A1. Tài khoản & bảo mật (UC-001 … UC-017)
| Chức năng | Trạng thái | Ghi chú |
|---|---|---|
| Đăng nhập, đăng xuất, làm mới phiên | ✅ | 384 refresh token trên DB, cần job dọn |
| Quên mật khẩu, đặt lại mật khẩu | 🟢 | Có hàng đợi gửi email; bảng 0 dòng |
| Hồ sơ cá nhân, đổi mật khẩu | ✅ | `/profile`, `/profile/security` |
| Quản lý user: tạo, khóa/mở, kích hoạt, gán role | ✅ | `/admin/access` |
| Import nhiều user (JSON) | ✅ | Màn nhập JSON thô; nên đổi sang Excel |
| Quản lý role & permission | 🟡 | Có API + màn `/admin/access/rbac`, nhưng bảng `permissions` rỗng |

### A2. Cấu trúc học vụ & kỳ đồ án (UC-018 … UC-031)
| Chức năng | Trạng thái | Ghi chú |
|---|---|---|
| Tổ chức → Khoa/Bộ môn → Ngành | ✅ | `/academic` |
| Gán user vào khoa/ngành, xác minh hồ sơ học vụ | ✅ | 11 bản ghi xác minh |
| Học kỳ, kỳ đồ án (đăng ký, thẩm định, thực thi, bàn giao) | ✅ | 2 học kỳ, 5 kỳ |
| Policy kỳ có version (mode, kích thước nhóm, số ngành, quota GVHD) | ✅ | 2 version |
| Policy chứng chỉ sinh viên theo kỳ | 🟢 | 0 dòng |
| Mẫu mốc đồ án có version | 🟢 | 0 dòng; đang đặt ở Admin, tài liệu giao cho Khoa |
| Rubric có version, cây tiêu chí | ✅ | 3 rubric, 8 tiêu chí |
| Scheme đánh giá (COMMON / MAJOR / INDIVIDUAL) | 🟢 | Màn tạo nháp an toàn mới có trong PR #53; 0 dòng |

### A3. Nhóm & đăng ký đồ án (UC-032 … UC-051)
| Chức năng | Trạng thái | Ghi chú |
|---|---|---|
| Tạo nhóm, chọn mode đơn ngành / liên ngành | ✅ | 4 nhóm |
| Mời, nhận/từ chối, hủy lời mời, rời nhóm, xóa thành viên | ✅ | |
| Chuyển trưởng nhóm (qua yêu cầu có duyệt) | 🟢 | 0 yêu cầu |
| Yêu cầu theo ngành, trách nhiệm từng ngành | 🟢 | `team_major_responsibilities` 0 dòng |
| Kiểm tra & khóa eligibility, lịch sử | 🟢 | `team_eligibility_checks` 0 dòng |
| Chứng chỉ sinh viên (nộp, xác minh, từ chối) | 🟢 | Upload multipart mới có trong PR #52 |
| Danh mục đề tài của khoa, chọn đề tài | ✅ | 2 đề tài |
| Tạo / sửa / validate / nộp đề cương (snapshot) | ✅ | 1 snapshot |
| Thẩm định: bắt đầu, yêu cầu sửa, duyệt, từ chối | ✅ | 12 bản ghi lịch sử trạng thái |
| Quyết định của khoa tham gia (liên ngành) | 🟢 | 0 dòng |

### A4. GVHD & thực thi (UC-052 … UC-073)
| Chức năng | Trạng thái | Ghi chú |
|---|---|---|
| Danh sách GV, chuyên môn, capacity | ✅ | 3 hồ sơ, 10 chuyên môn |
| Gửi / nhận / từ chối / hủy yêu cầu hướng dẫn | ✅ | 3 yêu cầu |
| Phân công Primary, Discipline Mentor, thay GV, kết thúc | ✅ | 2 phân công |
| **Gợi ý GVHD bằng AI (UC-054/116)** | ❌ | Không có code |
| Khởi tạo milestone khi ACTIVE, CRUD milestone, sắp xếp | ✅ | 5 milestone |
| Task: tạo, gán, trạng thái, phụ thuộc, lịch sử, quá hạn/bị chặn | ✅ | 11 task, 9 phụ thuộc |
| Ngành của task (PRIMARY / SUPPORTING) | 🟢 | 0 dòng |
| Lọc task theo ngành có phân trang | ❌ | BE chưa có `majorId` |
| Bình luận task | 🟢 | 0 dòng |
| Gantt / timeline | ✅ | |
| Quyền thao tác theo resource (`execution-actions`) | 🟡 | BE coi mentor như manager (lỗi BE-AW-001) |

### A5. Tiến độ, họp, sản phẩm, đóng góp (UC-074 … UC-103)
| Chức năng | Trạng thái | Ghi chú |
|---|---|---|
| Báo cáo tiến độ: nháp, nộp, phản hồi GV, trễ hạn | ✅ | 6 báo cáo |
| Chu kỳ báo cáo do khoa đặt | 🟢 | 0 dòng |
| Lịch họp, thành viên, biên bản, hủy/hoàn tất | ✅ | 16 cuộc họp |
| Quyết định & action item của cuộc họp | 🟡 | 1 action item; có 2 bảng trùng nghĩa |
| Deliverable có version, review | ✅ | 3 deliverable, 4 version |
| Kho tệp, tải xuống có kiểm quyền | ✅ | 6 tệp |
| Sổ minh chứng theo ngành | 🟢 | 0 dòng |
| **Đóng góp thành viên** | 🟡 **Lỗi** | Bảng `contribution_snapshots` không có trên DB nên API lỗi |

### A6. Thông báo & AI (UC-104 … UC-119)
| Chức năng | Trạng thái | Ghi chú |
|---|---|---|
| Thông báo trong app, đếm chưa đọc, đọc tất cả | ✅ | 18 thông báo |
| Email thông báo (outbox), nhắc hạn định kỳ | ✅ | 60 email |
| Phân tích tiến độ & rủi ro (rule-based) | 🟡 | Tính lại mỗi lần, không lưu, FE sau feature flag |
| Tóm tắt báo cáo, hỏi đáp AI trong phạm vi project | 🟡 | Dạng template "grounded", chưa dùng LLM |
| **Dự báo trễ hạn có đánh giá, giải thích yếu tố, xem lại kết quả AI đã lưu** | ❌ | Không có bảng lưu |

### A7. Bàn giao, chấm điểm, kết quả (UC-120 … UC-127)
| Chức năng | Trạng thái | Ghi chú |
|---|---|---|
| Yêu cầu bàn giao, checklist, nháp, khóa gói | 🟢 | Tất cả bảng 0 dòng |
| Phân công người chấm theo phạm vi | 🟢 | 0 dòng |
| Chấm nháp → hoàn tất (idempotent) | 🟢 | 0 dòng |
| Preview & công bố ProjectResult / StudentResult | 🟢 | 0 dòng; **chủ thể công bố liên khoa đang là Admin, cần đổi sang Lead Department** |
| Sinh viên xem kết quả của mình | 🟢 | Chưa có breakdown thành phần |
| **Hội đồng bảo vệ, lịch bảo vệ** | ❌ | |
| **Phúc khảo / đính chính kết quả** | ❌ | |

### A8. Báo cáo, lưu trữ (UC-128 … UC-134)
| Chức năng | Trạng thái | Ghi chú |
|---|---|---|
| Dashboard theo role (SV, GV, Khoa, Admin) | ✅ | |
| Danh mục đồ án, xuất CSV/XLSX/PDF | ✅ | |
| Nhật ký audit | ✅ | 664 bản ghi |
| Lưu trữ đồ án, xem bản lưu trữ | ✅ | |

### A9. Theo CIB v4 nhưng chưa có
| Chức năng | Trạng thái |
|---|---|
| Checkpoint / Review Gate G1–G6 | ❌ |
| Working Agreement của nhóm | ❌ |
| Industry Expert (chuyên gia doanh nghiệp) | ❌ |
| Peer evaluation | ❌ |
| `my-capabilities` thống nhất cho mọi resource | 🟡 (đang có `execution-actions` và `actions` riêng lẻ) |

---

## B. Chức năng đã làm thêm (ngoài SRS gốc)

| Chức năng | BE | FE | Dữ liệu | Ghi chú |
|---|---|---|---|---|
| **Chat realtime** (SignalR, chat 1-1, nhóm, project, sửa/thu hồi tin, đã đọc) | ✅ | ✅ `/messages` (bật bằng flag) | 3 hội thoại, 16 tin | Có outbox đảm bảo gửi |
| **Họp video** (LiveKit: bắt đầu, vào phòng, kết thúc, điểm danh có mặt, webhook) | ✅ | ✅ `/project/meetings/:id/video` (flag) | 8 phiên, 17 lượt có mặt | Có job dọn phòng |
| **Đăng nhập Google**, liên kết / hủy liên kết | ✅ | ✅ | 45 challenge | |
| **Lịch tổng hợp** (task, mốc, họp, deliverable, hạn bàn giao) | ✅ | ✅ `/calendar` cho SV | – | GV/Mentor bị chặn vì BE trả phạm vi quá rộng |
| **Tìm nhanh chức năng** (Ctrl+K) | – | ✅ | – | |
| **Chu kỳ báo cáo & action item cấp project** | ✅ | ✅ | 0 dòng | |
| **Quyết định & action item cuộc họp** | ✅ | ✅ | 1 dòng | Trùng với action item project |
| **Yêu cầu đổi trưởng nhóm có mentor duyệt** | ✅ | ✅ | 0 dòng | |
| **Thay GVHD** (gợi ý ứng viên thay, kết thúc phân công) | ✅ | ✅ | – | |
| **Import khung chương trình từ Excel/CSV** (preview → commit) | ✅ PR #106 | ✅ PR FE đợt 1 (mới) | – | |
| **Xuất danh sách nhóm Excel** | ✅ PR #107 | ✅ PR FE đợt 1 (mới) | – | |
| **Admin mở màn scheme, phân công chấm, gói bàn giao, kết quả** | ✅ | ✅ PR #53 | – | Mâu thuẫn với quyết định "Lead Department công bố"; xử lý cùng BE-03 |
| **Chụp snapshot đóng góp** | ✅ | ❌ chưa có nút | ❌ thiếu bảng | |
| **Trạng thái hệ thống** (`/system`) | ✅ | ✅ panel Admin | – | |
| **Hệ thống chuyển động (motion)**, skeleton loading | – | ✅ | – | |
| **Email nhắc lịch họp** | ✅ | – | 5 lần nhắc | |

---

## C. Việc tiếp theo suy ra từ kiểm kê
1. Chạy script `contribution_snapshots` (KhaiNQ, BE-01). Không có bước này, trang Đóng góp lỗi.
2. Chạy hết 3 kịch bản demo trên dữ liệu seed riêng để chuyển các dòng 🟢 thành ✅ (KhaiNQ BE-02 + DongVV + FE).
3. Đổi chủ thể công bố kết quả sang Lead Department (DongVV BE-03), sau đó FE gỡ CTA công bố trên route Admin.
4. Các dòng ❌ đi theo sprint 3–4 và backlog P2 trong file kế hoạch chính.
