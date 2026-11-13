# Quy tắc làm việc do người dùng yêu cầu (AI-PMS Backend)

## Quy tắc Quản trị Kỹ thuật Chung (Bắt buộc cho cả FE & BE)
Toàn bộ kỹ sư và agent khi làm việc trên Backend bắt buộc phải tuân thủ nghiêm ngặt **8 Quy tắc Quản trị Kỹ thuật Chung** được đồng bộ tại:
[AI_PMS_SHARED_GOVERNANCE_RULES.md](file:///F:/AI-PMS/ai-pms-backend-access-audit-20261113/docs/rules/AI_PMS_SHARED_GOVERNANCE_RULES.md)

### Tóm lược 8 quy tắc cốt lõi:
1. **Thứ tự nguồn thẩm quyền:** Yêu cầu người dùng → Business rule/DB/BE contract → FE representation → Ảnh tham khảo. Backend là nguồn thẩm quyền tối cao về mô hình dữ liệu, quyền, state machine và công thức điểm.
2. **Phân quyền đa chiều:** Role + Persisted state + Scope (Organization/Department/Major/Assignment); BE thực thi quyền ở tầng query và command; không phụ thuộc vào client filters.
3. **Liêm chính dữ liệu:** Tuyệt đối không tự tạo endpoint, trạng thái, quyền, điểm số, mock data thành production, “online” hay vị trí địa lý. Không suy diễn "đang online" từ `LastLoginAt`. Không vẽ marker giả hay dùng GPS cá nhân từ IP.
4. **Ma trận trạng thái:** Cung cấp HTTP status code và payload chuẩn xác (200, 201, 204, 400, 401, 403, 404, 409, 422, 500) để FE xử lý đầy đủ các trạng thái tương ứng.
5. **Bảo toàn dữ liệu & Concurrency:** Sử dụng RowVersion/ConcurrencyToken để phát hiện 409 Conflict, bảo vệ dữ liệu khi ghi đồng thời; trả về metadata cho phép FE đọc lại (readback) sau khi ghi.
6. **Thương hiệu & A11y:** Phối hợp cùng chuẩn nhận diện AIPMS; cung cấp message lỗi có ý nghĩa bằng tiếng Việt cho người dùng cuối.
7. **Phân định hoàn thiện:** Phân biệt minh bạch IMPLEMENTED, TESTED, RUNTIME VERIFIED, PROPOSED, BLOCKED.
8. **Truy vết kỹ thuật:** Mỗi thay đổi có Source → Vấn đề → Giải pháp → Test (Unit/Integration) → Giới hạn; không hạ chuẩn test hoặc che lỗi bằng mock.

---

## Nhánh cho từng task & Ngoại lệ chỉ định trực tiếp
- **Chỉ định trực tiếp cho Task Hiện tại:**
  - Nhánh BE được chỉ định duy nhất: `feat/be-access-audit-20261113`.
  - Đây là ngoại lệ trực tiếp của người dùng đối với quy tắc "task mới tạo nhánh mới". Tuyệt đối không tạo nhánh thay thế.
  - Chỉ commit và push lên đúng nhánh `feat/be-access-audit-20261113`.
  - Không tạo PR, không merge, không rebase/reset hoặc đưa code lên `develop`.
- **Ranh giới PR #145 (BE-16):**
  - Không âm thầm mang code từ PR #145 (phòng/lịch bảo vệ) sang nhánh này.
  - Không gọi API branch-only như thể đã có trên `develop`.
