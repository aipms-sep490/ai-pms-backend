# Phân tích lịch Gate và phân phòng toàn trường

Ngày: 2026-11-08. Trạng thái: phân tích nghiệp vụ; điều kiện `đạt Gate trước` và chỉ R3/R4 offline đã được chủ dự án xác nhận. Hợp đồng FE–BE và luồng phân phòng R3/R4 toàn trường **chưa triển khai**.

## Phạm vi và cách hiểu

- Mỗi kỳ đồ án có danh sách nhóm thuộc toàn bộ các khoa của trường. Chỉ Gate 3 (`R3`, review lần 3) và Gate 4 (`R4`, defense) tổ chức OFFLINE và cần phân phòng tại trường. R1/R2 theo luồng ONLINE, không chiếm phòng vật lý. Mỗi Gate có cửa sổ lịch được công bố, có thể kéo dài nhiều ngày.
- R3 dùng phiên review gắn `ProjectGateInstance` và hội đồng reviewer Gate. R4 **chính là** buổi bảo vệ cuối kỳ `DefenseSession` và dùng `DefenseCommittee`; không tạo một buổi offline R4 độc lập để tránh hai lịch/phòng cho cùng nhóm. Hai loại phiên dùng chung kho phòng và kiểm tra xung đột.
- R3 chỉ nhận nhóm đã đạt R2; R4 chỉ nhận nhóm đã đạt R3 (kể cả quyết định ghi đè hợp lệ), còn đăng ký/hồ sơ hợp lệ và đã có hội đồng phù hợp. R1/R2 không thuộc tác vụ phân phòng tại trường. Đây là quyết định đã được xác nhận; không đòi nhóm đạt chính Gate đang chờ xếp lịch.
- Nhóm chưa đủ điều kiện giữ trạng thái `CHUA_DU_DIEU_KIEN` và lý do cụ thể; không chiếm phòng. Nhóm rớt Gate hiện tại không được xếp Gate kế tiếp. Một buổi Gate đã diễn ra vẫn giữ lịch sử.
- Trạng thái sẵn sàng **để xếp R4** phải xét R3 và các điều kiện tiền bảo vệ, không yêu cầu R4 đã `PASSED`. Sau phiên R4, kết quả defense và trạng thái Gate R4 cần được đồng bộ theo một quy tắc rõ ràng, tránh hai nguồn kết quả mâu thuẫn.

## Quy trình đề xuất

1. Admin công bố cửa sổ ngày/giờ, các phòng, ca, thời lượng, sức chứa dự phòng và thời điểm chốt danh sách cho R3 và R4 của kỳ. Chế độ được cố định: R1/R2 ONLINE, R3/R4 OFFLINE. Nếu nhiều ngành có lịch khác nhau, cấu hình theo `period + gate + scope`; vẫn dùng chung kho phòng toàn trường.
2. Khi đến thời điểm chốt hoặc khi kết quả Gate trước thay đổi, hệ thống gom tất cả nhóm hợp lệ toàn trường. Một lần chạy có khóa/idempotency cho `period + gate + version`; tính cả những nhóm vừa đủ điều kiện nhưng chưa có lịch.
3. Hệ thống tính bản nháp, hiển thị `đủ điều kiện / đã xếp / thiếu phòng / thiếu hội đồng / xung đột / chưa đủ điều kiện` cùng lý do theo nhóm. Nếu không xếp hết trong cửa sổ, không tự vượt ngày cuối; Admin bổ sung ngày/phòng/ca hoặc xử lý ngoại lệ rồi chạy lại.
4. Admin rà soát và duyệt công bố. Lúc công bố, BE kiểm tra lại trạng thái Gate, phiên bản dữ liệu, phòng và lịch người tham dự trong một giao dịch; phiên hợp lệ mới thành lịch chính thức.
5. Sau khi công bố thành công, thông báo trong ứng dụng và email theo cấu hình tới toàn bộ thành viên nhóm, người hướng dẫn và người tham gia phiên. Thông báo chứa R3/R4, ngày, giờ, phòng, thành phần và đường dẫn xem lịch. Đổi/hủy lịch phải tạo bản cập nhật riêng, thông báo lại và lưu dấu vết; không lặng lẽ thay lịch đã công bố.

## Ràng buộc

**Bắt buộc:** không hai phiên R3/R4 chồng thời gian trong cùng phòng (kiểm tra khoảng `[start,end)` chứ không chỉ số ca); không trùng lịch người đánh giá hoặc nhóm; phòng ACTIVE, đủ sức chứa và phù hợp thiết bị; trong cửa sổ Gate, giờ làm việc và lịch nghỉ của kỳ; một nhóm có tối đa một phiên đang hiệu lực cho mỗi R3/R4; không phân phòng cho nhóm thiếu điều kiện. Xung đột phải tính với các kỳ khác cùng dùng phòng.

**Tối ưu:** gom ngành/hội đồng vào khu vực gần nhau, giảm khoảng trống giữa các phiên, phân bố số nhóm theo ngày, ưu tiên nhóm có ràng buộc đặc biệt. Không hy sinh ràng buộc bắt buộc để tối ưu.

Ví dụ năng lực: 120 nhóm, 4 phòng, 6 ca/ngày cho tối đa 24 nhóm/ngày; cần ít nhất 5 ngày làm việc nếu mỗi nhóm dùng một ca và không có xung đột người tham gia. Nếu cửa sổ chỉ 3 ngày, hệ thống báo thiếu ít nhất 48 ca trước khi công bố.

## Quyền và màn hình

| Vai trò | Được xem/làm gì | Hiển thị chính |
| --- | --- | --- |
| Admin | Cấu hình cửa sổ Gate và kho phòng toàn trường; xem dashboard, chạy/xem nháp, sửa ngoại lệ, duyệt/công bố, đổi/hủy lịch; xem audit và thống kê | Bộ lọc kỳ/Gate/khoa/ngành/ngày, lưới phòng × ca, số nhóm từng trạng thái, bảng xung đột và hàng chờ duyệt |
| DEPARTMENT_STAFF khoa chủ trì | Xem nhóm khoa mình; kiểm tra điều kiện, bổ sung hồ sơ/hội đồng theo quyền hiện hữu, đề xuất điều chỉnh lịch; chỉ sửa lịch trực tiếp nếu chính sách giao quyền rõ ràng | Danh sách nhóm của khoa và lý do chưa được xếp; lịch công bố; yêu cầu đổi lịch |
| Giảng viên/hội đồng | Xem phiên được phân, báo bận hoặc gửi yêu cầu đổi có lý do; ghi biên bản/điểm danh và đánh giá theo quyền Gate | Lịch cá nhân, nhóm phụ trách, phòng/link Online, thay đổi lịch |
| Sinh viên/nhóm | Xem trạng thái đủ điều kiện và nguyên nhân thiếu; chỉ xem lịch của nhóm sau công bố; nhận thông báo và xác nhận đã xem (nếu cần) | Thẻ `Gate kế tiếp`, thời gian, phòng, hội đồng, hướng dẫn chuẩn bị |

Admin được sửa hoặc duyệt **lịch**, theo yêu cầu mới của chủ dự án. Quyền ghi đè **kết quả Review Gate** vẫn thuộc `DEPARTMENT_STAFF` của khoa chủ trì; không suy rộng quyền đó sang Admin.

## Hợp đồng dữ liệu/API cần bổ sung

- Cấu hình `GateSchedulePlan(periodId, gateCode, scope, windowStart, windowEnd, slotDuration, breakDuration, roomIds, cutoffAt, status, version)` với `gateCode` chỉ nhận R3/R4; xác định múi giờ `Asia/Ho_Chi_Minh` ở ranh giới API.
- Phiên R3 `Gate3SessionAllocation(gateInstanceId, projectId, roomId, startAt, endAt, reviewerIds, status, planVersion, concurrencyToken, publishedAt)`; unique một phiên R3 đang hiệu lực cho mỗi Gate instance. Phiên R4 dùng `DefenseSession` hiện có, cần liên kết ngược với R4 để đồng bộ lịch/kết quả. Cả hai dùng một kho lịch chiếm phòng chung.
- API đọc thống kê/nhóm đủ điều kiện và lý do; chạy preview (danh sách phân bổ + chưa xếp + mã lỗi); công bố theo `planVersion/concurrencyToken`; liệt kê lịch theo kỳ/Gate/khoa/ngày; đổi/hủy lịch và xem audit. Danh sách trả về có phân trang/bộ lọc cho quy mô toàn trường.
- Kết quả `409` khi lịch/phòng/Gate đã đổi; FE yêu cầu tải lại preview. Thông báo phát sinh sau giao dịch công bố, có khóa chống gửi trùng.

## Khoảng cách với nhánh BE/FE hiện tại

- `AutoScheduleService` hiện lọc `ProjectRegistrationSnapshot.LeadDepartmentId` theo một khoa, xếp `DefenseSession` R4 khi đã có `DefenseCommittee`; API nhận khoảng ngày và phòng do người dùng chọn, chưa tự lấy cửa sổ R4, kiểm tra R3 `PASSED` hoặc chạy cho toàn trường.
- `ProjectGateInstance` đã có `WindowStart/WindowEnd`, `Status`, `BlocksDefense`. `GateSessionScript` mô tả phiên review nhưng chưa có lịch/phân phòng R3 tự động cấp kỳ. `GateSessionScriptService` hiện chấp nhận ONLINE/OFFLINE ở bất kỳ Gate nào, trái quy tắc cố định R1/R2 ONLINE và R3/R4 OFFLINE.
- `DefenseReadiness` hiện có thể coi R4 `BlocksDefense` là điều kiện phải `PASSED` trước khi bảo vệ; nếu R4 chính là defense thì tạo vòng điều kiện sai. Cần tách `ready to schedule R4` khỏi `R4 passed`.
- Xung đột của auto schedule hiện chỉ đọc `DefenseSession`; chưa dùng kho chiếm phòng chung với phiên review R3. Lưu script offline R3 hiện kiểm tra phòng ACTIVE nhưng chưa chặn trùng phòng theo thời gian.
- Auto schedule `Apply` hiện ghi audit nhưng chưa gửi thông báo; thông báo chỉ có ở luồng xếp/hủy `DefenseSession` thủ công.
- FE `DefenseCoordinatorView` và `AutoScheduleModal` đang dùng dữ liệu mẫu, ngày cố định và thao tác mô phỏng. `ReviewGateModeConfigPanel` còn cho đổi R1/R2 sang OFFLINE hoặc R3/R4 sang ONLINE; cần bỏ nút đổi chế độ và có trang điều phối R3/R4 toàn trường nối API thật trước E2E/UAT.
- Kiểm thử BE đã dùng SQL Server cục bộ với database cô lập bị xóa sau test; chưa chứng minh migration/staging hoặc E2E FE–BE trên database ứng dụng thật.

## UAT tối thiểu

1. R1/R2 không tạo booking phòng vật lý; API và FE không cho đổi chế độ các Gate.
2. R3 chỉ xếp nhóm `R2 PASSED`; R4 chỉ xếp nhóm `R3 PASSED`, không đòi `R4 PASSED`. Nhóm chưa đạt/rớt bị giữ lại với lý do.
3. R4 chỉ tạo một `DefenseSession`, không thêm một phiên R4 trùng. Cửa sổ nhiều ngày, thiếu năng lực phòng, ngày nghỉ, phòng bảo trì, trùng người/phòng trên hai khoa và giữa R3/R4.
4. Hai Admin công bố đồng thời: chỉ một kết quả hợp lệ, không trùng phòng và không gửi thông báo lặp.
5. Đổi/hủy sau công bố: lịch cũ giữ audit, người liên quan nhận bản cập nhật, không lộ lịch nhóm khác cho sinh viên.
