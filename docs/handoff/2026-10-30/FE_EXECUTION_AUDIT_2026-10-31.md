# FE execution audit — 31/10/2026

## Phạm vi và nguồn đối chiếu

Đã đọc `FE_DEV_TASKS.md`, `RUBRIC_DESIGN_2026-10-30.md`, `COVERAGE_MATRIX_2026-10-30.md`, `CAU_HOI_BO_MON_2026-10-30.md`, `README.md` và `PROMPT_KICKOFF.md`. Các câu lệnh trong tài liệu được xem là đề xuất bàn giao; quyết định triển khai dựa trên FE `origin/develop` tại `c67f42e` và BE `origin/develop` tại `14eecc7` sau khi fetch ngày 31/10/2026. Không suy ra rằng mô tả “BE ready” trong tài liệu là bằng chứng API hiện hành.

Ma trận coverage ghi 24/32 wireframe dùng route/modal hiện có, 3 gộp, 5 route mới đều phụ thuộc BE chưa có. Đợt này không dựng 5 route rỗng. Quy tắc nghiệp vụ giữ nguyên: điểm và quyền do BE quyết định; COLD khác DEFENSE; theo dõi tuần không thành điểm; `null` không được đổi thành `0`; không gắn nhãn chính thức khi thiếu trạng thái phê duyệt và số văn bản; không tự định nghĩa quyết định của bộ môn.

## Tiến độ theo task

| Task | Kết quả tại FE | Trạng thái nghiệm thu |
|---|---|---|
| F1 | Thêm chọn stage từng thành phần, nhãn COLD/DEFENSE/ON_GOING và tổng trọng số theo stage để tham khảo. Stage `null` hoặc lạ được hiển thị nhưng chặn lưu, yêu cầu chọn rõ. | **PARTIAL**: BE hiện bác mọi `Stage = null`; không đạt yêu cầu tài liệu về sửa tên bản nháp legacy mà giữ null. |
| F2 | Rubric chỉ đọc hiển thị trọng số trong nhóm và `effectiveWeightPercent` do BE trả về; dữ liệu phẳng/legacy chỉ hiện một trọng số. Màn chấm gọi trọng số hiện có là trọng số hiệu lực. | **PARTIAL**: BE chưa giao example JSON rubric trong `docs/contracts`; draft chấm chưa có cả hai trọng số từ BE. |
| F3 | Báo lỗi trọng số theo từng nhóm anh em, kể cả nhóm lồng sâu và thứ tự trùng; giữ nháp khi 409; hiện lỗi field từ 422. BE vẫn là nơi validate cuối. | **PARTIAL**: test và browser fixture xanh; chưa có readback trên staging xác thực. |
| F4 | Chưa sửa UI vì không thể xác định official/demo từ dữ liệu mà evaluator được phép đọc. | **BLOCKED** bởi projection/quyền đọc BE. |
| F5 | Chưa bật nút production vì API cập nhật scope cần concurrency token ở lần sửa, nhưng matrix không trả token/đủ scope/capability. Bật cờ sẽ tạo đường thao tác lỗi. | **BLOCKED** bởi contract BE. |
| F6 | Gọi endpoint prefill chỉ khi tạo báo cáo mới; áp dụng từng gợi ý không null, giữ phần người dùng đã chạm, cho bỏ gợi ý, hủy request chậm. | **PARTIAL**: fixture xanh; chưa kiểm với dữ liệu dự án xác thực. |
| F7 | Chưa bỏ giới hạn upload hardcode vì chưa có endpoint policy `BE-05`. | **BLOCKED**. |
| F8 | Chưa dựng mức chất lượng, scope, quy trình gửi duyệt vì thiếu `BE-06`–`BE-08`, `Q-A10`, `Q-N02`. | **BLOCKED**. |
| F9 | Chưa dựng stage/chất lượng/hai trọng số trên phiếu chấm vì thiếu `BE-09`. | **BLOCKED**. |
| F10 | Sửa target mồ côi, lưu riêng `IN_PROGRESS`/`DISMISSED`/`COMPLETED` và bước dở, fallback khi target thiếu, kiểm tra target tĩnh; thêm `guideId` làm đường nối cho hướng dẫn theo trang. | **PARTIAL**: test và browser fixture xanh; nội dung hướng dẫn từng trang chưa thuộc ticket này. |
| F11 | Chưa dựng fixture/CI từ hợp đồng vì BE chưa giao example JSON rubric và bản mới của evaluator assignment; tự viết fixture sẽ không phát hiện lệch contract. | **BLOCKED** bởi Contract Card BE. |

## Contract gap có bằng chứng

1. **F1 legacy stage:** `EvaluationSchemeService.SaveAsync` từ chối `input.Components` nếu stage không thuộc `ON_GOING`, `COLD`, `DEFENSE`; `EvaluationSchemeRules` cũng bác null. FE không tự đổi null sang stage mặc định. BE cần quyết định migration/luồng sửa legacy hoặc sửa tiêu chí nghiệm thu F1.
2. **F4 official/demo:** `EvaluationAssignmentDetailDto` chỉ có `Assignment`, `CanScore`, `LegacyReadOnly`, `DenialReason`. `EvaluationSchemeService.ListAsync` gọi `Manage(projectId)` và không cấp quyền đọc cho evaluator thường. Cần BE trả `isDemo`, `approvalStatus`, `approvalReference` trong projection được evaluator phép đọc, hoặc endpoint riêng có RBAC rõ.
3. **F5 scope decision:** `ReviewGateService.UpdateScope` yêu cầu `ConcurrencyToken` khi sửa scope đã có. `ProjectReviewMatrixRowDto` chỉ trả `HybridScopes` trạng thái và `HybridScopeLabels`; chưa có token per scope, danh sách scope bắt buộc chưa có quyết định, hoặc capability cập nhật. FE hiện gửi chỉ `newStatus` và bị cờ mock chặn ở production. Cần BE trả projection và quyền thao tác trước khi bật đường ghi.
4. **F2/F11 examples:** `docs/contracts/cib-v4/examples` của BE có ví dụ evaluation assignment cũ, không có JSON rubric làm nguồn fixture cho F2. Cần Contract Card gồm example JSON, nullable fields, error codes và test names trước khi bật CI drift gate.
5. **F7–F9:** xác nhận trên `origin/develop` chưa có `/upload-policies`, rubric quality levels/approval workflow, và projection chấm BE-09. `Q-A10` (4 hay 5 mức) và `Q-N02` (người sở hữu/người duyệt) vẫn là quyết định của bộ môn/hội đồng.

## Worktree và kiểm chứng

Mỗi task sửa code nằm trong worktree tách từ FE `origin/develop` tại `c67f42e`; checkout FE chính, BE, `.env`, DB và staging không bị sửa. Không commit, push, mở PR hoặc merge.

| Task | Worktree / branch | Kiểm chứng cục bộ |
|---|---|
| F1 | `F:\AI-PMS\ai-pms-frontend-f1-stage` / `feat/fe-f1-stage-20261031` | 36 test liên quan, Playwright 1/1, build và diff check xanh |
| F2 | `F:\AI-PMS\ai-pms-frontend-f2-rubric` / `feat/fe-f2-rubric-effective-weight-20261031` | 6 test, Playwright 1/1, build và diff check xanh |
| F3 | `F:\AI-PMS\ai-pms-frontend-f3-weights` / `fix/fe-f3-rubric-weight-groups-20261031` | 10 test, Playwright 1/1, build và diff check xanh |
| F6 | `F:\AI-PMS\ai-pms-frontend-f6-prefill` / `feat/fe-f6-progress-prefill-20261031` | 51 test, Playwright 1/1, build và diff check xanh |
| F10 | `F:\AI-PMS\ai-pms-frontend-f10-tour` / `fix/fe-f10-tour-20261031` | 13 test, Playwright 1/1, build và diff check xanh |

Các Playwright test dùng trang component thật với API fixture, kiểm tra các cỡ 320/768/1024/1440 và lỗi runtime. Đây là **controlled fixture evidence**, không chứng minh BE, DB, SignalR, RBAC hoặc vòng đời staging. F4 và F5 có worktree/branch riêng nhưng không có thay đổi code vì contract gap ở trên.
