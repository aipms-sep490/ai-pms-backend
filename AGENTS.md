# Quy tắc làm việc do người dùng yêu cầu (AI-PMS Backend)

## Quy tắc Quản trị Kỹ thuật Chung (Bắt buộc cho cả FE & BE)
Toàn bộ kỹ sư và agent khi làm việc trên Backend bắt buộc phải tuân thủ nghiêm ngặt **8 Quy tắc Quản trị Kỹ thuật Chung** được đồng bộ tại:
[AI_PMS_SHARED_GOVERNANCE_RULES.md](docs/rules/AI_PMS_SHARED_GOVERNANCE_RULES.md)

### Tóm lược 8 quy tắc cốt lõi:
1. **Thứ tự nguồn thẩm quyền:** Yêu cầu người dùng → Business rule/DB/BE contract → FE representation → Ảnh tham khảo. Backend là nguồn thẩm quyền tối cao về mô hình dữ liệu, quyền, state machine và công thức điểm.
2. **Phân quyền đa chiều:** Role + Persisted state + Scope (Organization/Department/Major/Assignment); BE thực thi quyền ở tầng query và command; không phụ thuộc vào client filters.
3. **Liêm chính dữ liệu:** Tuyệt đối không tự tạo endpoint, trạng thái, quyền, điểm số, mock data thành production, “online” hay vị trí địa lý. Không suy diễn "đang online" từ `LastLoginAt`. Không vẽ marker giả hay dùng GPS cá nhân từ IP.
4. **Ma trận trạng thái:** Cung cấp HTTP status code và payload chuẩn xác (200, 201, 204, 400, 401, 403, 404, 409, 422, 500) để FE xử lý đầy đủ các trạng thái tương ứng.
5. **Bảo toàn dữ liệu & Concurrency:** Sử dụng RowVersion/ConcurrencyToken để phát hiện 409 Conflict, bảo vệ dữ liệu khi ghi đồng thời; trả về metadata cho phép FE đọc lại (readback) sau khi ghi.
6. **Thương hiệu & A11y:** Phối hợp cùng chuẩn nhận diện AIPMS và palette FAMS-inspired sáng (`#F8F9FB` canvas, `#C2410C` action, `#F37021` accent); cung cấp message lỗi có ý nghĩa bằng tiếng Việt cho người dùng cuối.
7. **Phân định hoàn thiện:** Phân biệt minh bạch IMPLEMENTED, TESTED, RUNTIME VERIFIED, PROPOSED, BLOCKED.
8. **Truy vết kỹ thuật:** Mỗi thay đổi có Source → Vấn đề → Giải pháp → Test (Unit/Integration) → Giới hạn; không hạ chuẩn test hoặc che lỗi bằng mock.

---

## Quy trình nhánh và merge
- Bắt đầu task mới từ `origin/develop` đã fetch, trừ khi người dùng chỉ định nhánh khác.
- Kiểm tra trạng thái checkout, commit gốc, nhánh remote và CI trước khi push hoặc merge; giữ nguyên thay đổi chưa commit của người khác.
- Chỉ merge khi người dùng yêu cầu. Xác minh đúng head, kiểm tra phụ thuộc nhánh/PR, giải quyết xung đột và chạy test trên kết quả tích hợp trước khi cập nhật `develop`.
- Không force push, reset hay stash để ép checkout sạch. Không mô tả API chỉ có trên nhánh tính năng như đã có trên `develop`.
