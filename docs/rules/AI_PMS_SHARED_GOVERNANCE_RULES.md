# AI-PMS Quy tắc Làm việc và Quản trị Kỹ thuật Chung (FE & BE)
# AI-PMS Shared Governance and Engineering Rules

Tài liệu này xác lập bộ quy tắc làm việc chuẩn mực, bắt buộc và có thể truy vết đồng thời trên toàn bộ mã nguồn Frontend (`feat/fe-fresearch-reference-20261113`) và Backend (`feat/be-access-audit-20261113`).
Mọi thành viên, kỹ sư và agent khi tham gia phát triển hệ thống AIPMS bắt buộc phải tuân thủ nghiêm ngặt 8 quy tắc nền tảng dưới đây.

---

## 1. Thứ tự Nguồn Thẩm quyền (Source Authority Hierarchy)
Mọi quyết định về nghiệp vụ, logic và giao diện phải tuân theo thứ tự ưu tiên giảm dần:
1. **Yêu cầu trực tiếp từ Người dùng (User Explicit Instructions):** Chỉ thị cấp cao nhất, các override hoặc chỉ định nhánh/phạm vi cụ thể.
2. **Quy tắc Nghiệp vụ / Cơ sở Dữ liệu / Backend Contract (BE Authority):** Backend sở hữu thẩm quyền tối cao về mô hình dữ liệu, phân quyền, trạng thái máy (state machines), công thức điểm số, và tính toàn vẹn thông tin. Frontend không bao giờ được tự ý định nghĩa lại nghiệp vụ.
3. **Đặc tả Giao diện & Hệ thống Thiết kế (FE Representation):** Các quy ước UI/UX, token màu sắc, typography và luồng tương tác tại `design-system/ai-pms/MASTER.md` và `docs/uiux/02-INTERACTION-RULES.md`.
4. **Tư liệu Tham khảo (Reference Systems - FAMS / F-Research):** FAMS và F-Research chỉ là nguồn tham khảo về cách tổ chức bố cục, mật độ thông tin, phân tầng điều hướng và tương tác. Tuyệt đối **không sao chép nguyên mẫu**, không mang nghiệp vụ LMS, lớp học, điểm danh, mật khẩu lớp học sang hệ thống quản lý đồ án tốt nghiệp Capstone của AIPMS.

---

## 2. Phân Quyền Đa Chiều & Vai Trò Thực Tế (Role + Persisted State + Scoped Access)
Hệ thống AIPMS quản lý quyền dựa trên ma trận đa chiều:
- **Vai trò:** Sinh viên (Student), Trưởng nhóm (Leader), Giảng viên hướng dẫn (Supervisor), Mentor doanh nghiệp/chuyên môn (Mentor), Cán bộ phản biện/đánh giá (Reviewer/Evaluator), Cán bộ quản lý Khoa/Bộ môn (Department Staff), Quản trị viên (Admin), Chuyên gia doanh nghiệp (Industry Expert).
- **Phạm vi (Scope):** Không chỉ dựa vào Global Role, quyền hạn được xác định chặt chẽ bởi:
  - Tổ chức & Bộ môn quản lý (Organization & Lead/Participating Department).
  - Chuyên ngành tham gia (Participating Majors trong đề tài liên ngành).
  - Phân công đồ án cụ thể (Project Assignment: Leader, Member, Primary Supervisor, Co-Supervisor, Assigned Mentor, Assigned Gate Reviewer).
  - Trạng thái lưu trữ thực tế (Persisted State): Trạng thái đề tài, đợt đăng ký (Period window), Review Gate status.
- **Ranh giới FE Guard:** Route Guard và Component Visibility ở FE chỉ phục vụ trải nghiệm người dùng (UX) nhằm ngăn chặn điều hướng lỗi. **Backend Authorization là rào chắn an ninh duy nhất có hiệu lực thẩm quyền**.

---

## 3. Cấm Tự Tạo Dữ Liệu và Trạng Thái Suy Diễn (Strict Truthfulness in State and Data)
1. **Không bịa đặt:** Tuyệt đối không tự bịa API endpoint, trạng thái hệ thống, quyền hạn, công thức điểm, hoặc dữ liệu giả lập.
2. **Trạng thái Hoạt động (Presence):** Tuyệt đối không suy diễn người dùng "đang online" chỉ từ sự kiện đăng nhập gần nhất (`LastLoginAt` hay login access log). Nếu Backend chưa có hệ thống presence realtime/session, trạng thái online phải để trống hoặc ghi rõ "Chưa hỗ trợ".
3. **Dữ liệu Vị trí Địa lý (Geo-location):** Không suy luận vị trí cá nhân hay tọa độ GPS từ địa chỉ IP. Không vẽ marker giả trên bản đồ. Địa chỉ IP chỉ được xem là bản ghi mạng và phải tuân thủ nghiêm ngặt chính sách bảo mật/quyền riêng tư.

---

## 4. Ma trận Trạng thái Giao diện Bắt buộc (Comprehensive State Matrix)
Mỗi màn hình, bảng dữ liệu và panel tương tác bắt buộc phải xử lý đầy đủ các trạng thái độc lập, không dùng biến boolean đơn giản:
1. **Initial Loading:** Skeleton loader hoặc Mascot loading năng lượng AIPMS phù hợp ngữ cảnh, thông báo tiến trình rõ ràng.
2. **Genuine Empty:** Trạng thái chưa có dữ liệu ban đầu, hướng dẫn người dùng bước khởi tạo tiếp theo.
3. **Filtered Empty:** Bộ lọc không tìm thấy kết quả phù hợp, cung cấp nút đặt lại (Reset Filter) rõ ràng.
4. **Blocked:** Bị chặn do điều kiện nghiệp vụ, hạn ngạch (quota) hoặc ngoài khung thời gian (registration window), hiển thị rõ lý do và điều kiện mở khóa.
5. **401 Unauthorized:** Phiên làm việc hết hạn, chuyển hướng đăng nhập an toàn và bảo toàn điểm đến mong muốn.
6. **403 Forbidden:** Không có quyền truy cập phạm vi này, hiển thị thông báo ranh giới quyền hạn, tuyệt đối không để lộ dữ liệu nhạy cảm.
7. **404 Not Found:** Tài nguyên không tồn tại hoặc đã bị thu hồi.
8. **409 Conflict:** Xung đột phiên bản (stale concurrency token) hoặc trạng thái đã thay đổi; giữ nguyên bản nháp (draft) của người dùng và thông báo tải lại dữ liệu mới nhất.
9. **Network Error / 5xx:** Lỗi kết nối mạng hoặc lỗi máy chủ, cung cấp nút Thử lại (Retry) an toàn, không gửi trùng lặp yêu cầu ghi.
10. **Read-only / Frozen:** Dữ liệu đã đóng băng (đề tài đã phê duyệt, hồ sơ đã nộp cuối kỳ, điểm đã công bố), hiển thị chỉ đọc kèm thông tin người duyệt và phiên bản.
11. **Success / Ready:** Hiển thị dữ liệu xác thực từ Backend đầy đủ và chính xác.

---

## 5. Bảo Toàn Bản Nháp và Xác Thực Mutation (Draft Preservation & Strict Mutation Readback)
1. **Bảo toàn dữ liệu người dùng:** Khi gặp lỗi validation (400/422), xung đột phiên bản (409) hoặc mất kết nối mạng, form nhập liệu phải giữ nguyên toàn bộ nội dung người dùng đã điền, không tự ý reset form làm mất công sức người dùng.
2. **Xác nhận Mutation:** Giao diện chỉ được hiển thị thông báo thành công (Success state/toast) sau khi nhận được phản hồi HTTP xác nhận thành công từ Backend (200/201/204).
3. **Đọc lại sau khi ghi (Readback):** Sau mọi thao tác thêm/sửa/xóa hoặc chuyển trạng thái, hệ thống bắt buộc phải refetch (đọc lại) dữ liệu tài nguyên, concurrency token mới nhất và danh sách hành động hợp lệ (`allowedActions`) từ server để cập nhật đồng bộ toàn bộ view và dashboard.

---

## 6. Tiêu Chuẩn Thiết Kế, Thương Hiệu và Tiếp Cận (AIPMS Brand & Accessibility Standards)
1. **Bản sắc Thương hiệu:** Sử dụng thống nhất bộ nhận diện AIPMS: Logo chính thức, Mascot cáo học thuật năng động, bảng màu Academic Light (`#F6FAFE` canvas, `#0868B8` primary, `#F17F06` secondary, `#107C41` success).
2. **Kỷ luật Design Tokens:**
   - Tuyệt đối không hardcode mã màu hex hoặc `rgba()` vào CSS/TSX khi đã có token trong design system.
   - Sử dụng CSS variables: `var(--color-*)` và `color-mix(in oklab, var(--token) N%, transparent)`.
   - Cấm sử dụng kích thước chữ dưới 12px (không dùng text dưới `text-xs`).
3. **Tiếp cận & Khả dụng (A11y & Responsiveness):**
   - Đáp ứng chuẩn WCAG 2.1 AA.
   - Hỗ trợ đầy đủ bàn phím (Tab/Shift+Tab, Enter/Space, Esc đóng dialog), focus ring nhìn rõ.
   - Trạng thái phải được diễn đạt bằng văn bản rõ ràng (`role="status"`, `aria-live="polite"`), không phụ thuộc độc nhất vào màu sắc.
   - Tôn trọng tùy chọn giảm chuyển động (`prefers-reduced-motion: reduce`).
   - Tương thích tốt trên 3 breakpoint tiêu chuẩn: Mobile 390px, Tablet 768px, Desktop 1440px.

---

## 7. Phân Định Mức Độ Hoàn Thiện Minh Bạch (Maturity Tiers & Evidence Discipline)
Mọi tài liệu, báo cáo tiến độ và commit log phải phân định minh bạch tình trạng tính năng theo các cấp độ chuẩn:
- **IMPLEMENTED:** Mã nguồn đã được hiện thực trong repo/branch.
- **TESTED:** Đã có test tự động (unit, integration, fixture test) bao phủ và chạy thành công trong phiên làm việc.
- **RUNTIME VERIFIED:** Đã được kiểm chứng thực tế tại runtime thông qua browser UAT hoặc authenticated API readback với tài khoản thật/dữ liệu thật.
- **PROPOSED:** Ý tưởng, đề xuất giải pháp đang chờ duyệt, chưa có mã chạy thực tế.
- **BLOCKED:** Bị chặn do thiếu dependency, thiếu API Backend, hoặc chưa có quyết định chính sách từ BA/Product Owner.

*Quy tắc đạo đức kỹ thuật:* Tuyệt đối không mô tả một đề xuất (PROPOSED) hoặc dữ liệu mock như một tính năng đã vận hành thực tế (RUNTIME VERIFIED).

---

## 8. Kỷ Luật Truy Vết Thay Đổi & Liêm Chính Kiểm Thử (Traceability & Test Integrity)
1. **Cấu trúc mỗi thay đổi:** Mọi Pull Request, commit hoặc lát cắt triển khai đều phải thể hiện rõ:
   $$\text{Nguồn thẩm quyền (Source)} \longrightarrow \text{Vấn đề thực tế (Issue)} \longrightarrow \text{Giải pháp kỹ thuật (Solution)} \longrightarrow \text{Kiểm thử chứng minh (Test)} \longrightarrow \text{Giới hạn phạm vi (Limitations)}$$
2. **Liêm chính kiểm thử (Test Integrity):**
   - Không được hạ tiêu chuẩn kiểm thử (relax assertions), không xóa bỏ test cases để che giấu lỗi.
   - Test mock chỉ được gọi chính xác là "kiểm thử fixture", không đánh tráo khái niệm thành "đã test E2E/UAT".
   - Mọi thay đổi logic đều phải đi kèm regression tests tương ứng để bảo vệ tính ổn định lâu dài của hệ thống.

---

## Quy trình Kiểm tra Đồng bộ Hai Repo (Drift Check)
Để đảm bảo bản rule này luôn đồng nhất 100% giữa FE và BE, sử dụng lệnh kiểm tra hash trong PowerShell:
```powershell
$feHash = (Get-FileHash -Algorithm SHA256 "F:\AI-PMS\ai-pms-frontend-fresearch-20261113\docs\rules\AI_PMS_SHARED_GOVERNANCE_RULES.md").Hash
$beHash = (Get-FileHash -Algorithm SHA256 "F:\AI-PMS\ai-pms-backend-access-audit-20261113\docs\rules\AI_PMS_SHARED_GOVERNANCE_RULES.md").Hash
if ($feHash -eq $beHash) {
    Write-Host " [PASS] Shared Governance Rules are strictly synchronized ($feHash)" -ForegroundColor Green
} else {
    Write-Error " [FAIL] DRIFT DETECTED between FE and BE Governance Rules!"
}
```
Mọi thay đổi trên bản quy tắc này phải được cập nhật đồng thời lên cả hai repo và commit kèm theo.
