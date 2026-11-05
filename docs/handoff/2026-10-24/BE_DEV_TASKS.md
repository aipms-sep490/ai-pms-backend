# NHIỆM VỤ BACKEND — AI-PMS · giao ngày 24/10/2026

> **Đọc hết DECISIONS.md trước khi sửa bất cứ dòng nào.**

---

## TỔNG HỢP 23 TASK BACKEND

| Phase | Task | Tên | Mức độ | Scope | Trạng thái |
|---|---|---|---|---|---|
| **Phase 0** | **BE-01** | Capability chặn mời Mentor (FUNC-08) | **BLOCKER** | **S/M** | ★ Ưu tiên số 1 |
| Phase 0 | **BE-02** | Dịch vụ email chưa cấu hình (ENV-04) | **BLOCKER** | M | Chờ cấu hình STMP staging |
| Phase 0 | **BE-03** | Google login bị chặn origin (ENV-02) | **BLOCKER-config** | S | Cần thêm localhost:5175 |
| **Phase 1** | **BE-04** | Timestamp UTC thiếu offset (Z) toàn cục | **HIGH** | M | Sửa tại gốc JsonConverter |
| Phase 1 | **BE-05** | AuthUserDto thiếu Title/Phone (FUNC-04) | **HIGH** | S | FE1 F5 đang chờ |
| Phase 1 | **BE-22** | TeamInvitationDto thiếu tên nhóm/người mời (UX-02) | **MEDIUM** | S | FE1 F6 đang chờ |
| Phase 1 | **BE-11** | Rate limit xác thực làm nghẽn kiểm thử (ENV-05) | **MEDIUM** | S/M | Tách policy (email, IP) |
| Phase 1 | **BE-21** | i18n notification: BE trả titleKey + params (UX-03) | **MEDIUM** | M | Hỗ trợ đa ngôn ngữ |
| Phase 1 | **BE-23** | Ghim capability ↔ enforcement qua test | **HIGH** | M/L | Phòng ngừa drift |
| **Phase 2** | **BE-06** | Member được tạo báo cáo tiến độ nháp (SPEC-02) | **MEDIUM** | XS | ✅ Đã chốt Q1: CÓ |
| Phase 2 | **BE-07** | Thứ tự mời GVHD (SPEC-01) | **MEDIUM** | XS | ✅ Đã chốt Q2: Giữ nguyên |
| Phase 2 | **BE-19** | Seed bộ môn và ngành Design | **MEDIUM** | M | Chuẩn bị kịch bản 3 ngành |
| **Phase 3** | **BE-08** | Generate Risk Alert (UC-108) | **MEDIUM** | L | Background worker |
| Phase 3 | **BE-09** | Auto-prefill Progress Data (UC-076) | **MEDIUM** | M | Endpoint prefill |
| Phase 3 | **BE-10** | Deadline Reminder (UC-107) | **MEDIUM** | S | Bật cờ cấu hình |
| Phase 3 | **BE-15** | Phân công Evaluator đa phạm vi (UC-123) | **MEDIUM** | M/L | FE2 G3 chờ formula |
| Phase 3 | **BE-16** | Công bố điểm chính thức (UC-127) | **MEDIUM** | M/L | Nghiệm thu luồng thật |
| **Phase 4** | **BE-13** | Academic AI Assistant | **HIGH** | L | ✅ Đã merge PR #132 |
| Phase 4 | **BE-12** | Refresh Authentication Session (UC-003) | **MEDIUM** | S/M | Bổ sung test case |
| Phase 4 | **BE-14** | Contribution Snapshot & Trend (UC-098/101) | **MEDIUM** | M | Nghiệm thu snapshot |
| Phase 4 | **BE-17** | Update User Account (UC-013) | **LOW** | S | Partial update test |
| Phase 4 | **BE-18** | Bật & nghiệm thu 14 module v5 trên staging | **MEDIUM** | L | Đồng bộ cờ tính năng |
| Phase 4 | **BE-20** | Endpoint /system trả commit SHA | **LOW** | S | Truy vết commit staging |
