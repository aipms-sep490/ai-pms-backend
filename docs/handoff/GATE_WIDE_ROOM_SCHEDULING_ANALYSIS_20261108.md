# Phân tích lịch Gate và phân phòng toàn trường

Ngày: 2026-11-08. Trạng thái: phân tích nghiệp vụ; điều kiện `đạt Gate trước` đã được chủ dự án xác nhận. Hợp đồng FE–BE và luồng phân phòng theo Gate **chưa triển khai**.

## Phạm vi và cách hiểu

- Mỗi kỳ đồ án có danh sách nhóm thuộc toàn bộ các khoa của trường. Mỗi Gate có một cửa sổ lịch được công bố, có thể kéo dài nhiều ngày. Lịch của Gate R1–R4 và lịch bảo vệ tốt nghiệp là hai loại phiên riêng, dù cùng dùng danh mục phòng và kiểm tra xung đột.
- Với Gate kế tiếp, `đủ điều kiện` nghĩa là đã đạt Gate trước đó (kể cả quyết định ghi đè hợp lệ), đã có hồ sơ/đăng ký còn hiệu lực, và Gate hiện tại đã mở cho nhóm. Gate đầu tiên dùng điều kiện đầu vào đã được duyệt. Đây là quyết định đã được xác nhận; không đòi nhóm đạt chính Gate đang chờ xếp lịch.
- Nhóm chưa đủ điều kiện giữ trạng thái `CHUA_DU_DIEU_KIEN` và lý do cụ thể; không chiếm phòng. Nhóm rớt Gate hiện tại không được xếp Gate kế tiếp. Một buổi Gate đã diễn ra vẫn giữ lịch sử.
- Gate đánh giá hồ sơ không cần phiên trực tiếp thì không chiếm phòng. Gate `ONLINE` dùng phòng họp trực tuyến và kiểm tra xung đột người tham dự; chỉ Gate `OFFLINE` mới phân phòng vật lý.

## Quy trình đề xuất

1. Admin công bố cửa sổ ngày/giờ, các phòng, ca, thời lượng, sức chứa dự phòng, thời điểm chốt danh sách và chế độ ONLINE/OFFLINE cho từng Gate của kỳ. Nếu nhiều ngành có lịch khác nhau, cấu hình theo `period + gate + scope`; vẫn dùng chung kho phòng toàn trường.
2. Khi đến thời điểm chốt hoặc khi kết quả Gate trước thay đổi, hệ thống gom tất cả nhóm hợp lệ toàn trường. Một lần chạy có khóa/idempotency cho `period + gate + version`; tính cả những nhóm vừa đủ điều kiện nhưng chưa có lịch.
3. Hệ thống tính bản nháp, hiển thị `đủ điều kiện / đã xếp / thiếu phòng / thiếu hội đồng / xung đột / chưa đủ điều kiện` cùng lý do theo nhóm. Nếu không xếp hết trong cửa sổ, không tự vượt ngày cuối; Admin bổ sung ngày/phòng/ca hoặc xử lý ngoại lệ rồi chạy lại.
4. Admin rà soát và duyệt công bố. Lúc công bố, BE kiểm tra lại trạng thái Gate, phiên bản dữ liệu, phòng và lịch người tham dự trong một giao dịch; phiên hợp lệ mới thành lịch chính thức.
5. Sau khi công bố thành công, thông báo trong ứng dụng và email theo cấu hình tới toàn bộ thành viên nhóm, người hướng dẫn và người tham gia phiên. Thông báo chứa Gate, ngày, giờ, phòng hoặc link Online, thành phần và đường dẫn xem lịch. Đổi/hủy lịch phải tạo bản cập nhật riêng, thông báo lại và lưu dấu vết; không lặng lẽ thay lịch đã công bố.

## Ràng buộc

**Bắt buộc:** không hai phiên chồng thời gian trong cùng phòng (kiểm tra khoảng `[start,end)` chứ không chỉ số ca); không trùng lịch người đánh giá hoặc nhóm; phòng ACTIVE, đủ sức chứa và phù hợp thiết bị; trong cửa sổ Gate, giờ làm việc và lịch nghỉ của kỳ; một nhóm có tối đa một phiên đang hiệu lực cho cùng Gate; không phân phòng cho nhóm thiếu điều kiện. Xung đột phải tính với mọi kỳ/Gate và lịch bảo vệ cùng dùng phòng.

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

- Cấu hình `GateSchedulePlan(periodId, gateCode, scope, windowStart, windowEnd, slotDuration, breakDuration, roomIds, deliveryMode, cutoffAt, status, version)`; xác định múi giờ `Asia/Ho_Chi_Minh` ở ranh giới API.
- Phiên `GateSessionAllocation(gateInstanceId, projectId, roomId?, startAt, endAt, reviewerIds, status, planVersion, concurrencyToken, publishedAt)`; unique một phiên đang hiệu lực cho mỗi Gate instance. Dùng một kho lịch chiếm phòng chung với `DefenseSession`.
- API đọc thống kê/nhóm đủ điều kiện và lý do; chạy preview (danh sách phân bổ + chưa xếp + mã lỗi); công bố theo `planVersion/concurrencyToken`; liệt kê lịch theo kỳ/Gate/khoa/ngày; đổi/hủy lịch và xem audit. Danh sách trả về có phân trang/bộ lọc cho quy mô toàn trường.
- Kết quả `409` khi lịch/phòng/Gate đã đổi; FE yêu cầu tải lại preview. Thông báo phát sinh sau giao dịch công bố, có khóa chống gửi trùng.

## Khoảng cách với nhánh BE/FE hiện tại

- `AutoScheduleService` hiện lọc `ProjectRegistrationSnapshot.LeadDepartmentId` theo một khoa, xếp `DefenseSession` cuối kỳ khi đã có `DefenseCommittee`; API nhận khoảng ngày và phòng do người dùng chọn, chưa tự lấy cửa sổ Gate hoặc chạy cho toàn trường.
- `ProjectGateInstance` đã có `WindowStart/WindowEnd`, `Status`, `BlocksDefense`. `GateSessionScript` mô tả phiên thẩm định nhưng chưa có lịch/phân phòng tự động cấp kỳ và chưa có dữ liệu phân bổ cho từng Gate.
- Xung đột của auto schedule hiện chỉ đọc `DefenseSession`; chưa dùng kho chiếm phòng chung với phiên Gate.
- Auto schedule `Apply` hiện ghi audit nhưng chưa gửi thông báo; thông báo chỉ có ở luồng xếp/hủy `DefenseSession` thủ công.
- FE `DefenseCoordinatorView` và `AutoScheduleModal` đang dùng dữ liệu mẫu, ngày cố định và thao tác mô phỏng. Cần trang điều phối Gate toàn trường và API thực trước E2E/UAT.
- Kiểm thử BE đã dùng SQL Server cục bộ với database cô lập bị xóa sau test; chưa chứng minh migration/staging hoặc E2E FE–BE trên database ứng dụng thật.

## UAT tối thiểu

1. Gate đầu: nhóm hợp lệ được xếp, nhóm thiếu đầu vào không chiếm phòng.
2. Gate sau: nhóm đạt Gate trước được xếp; nhóm chưa đạt/rớt bị giữ lại với lý do.
3. Cửa sổ nhiều ngày, thiếu năng lực phòng, ngày nghỉ, phòng bảo trì, trùng người/phòng trên hai khoa và hai Gate.
4. Hai Admin công bố đồng thời: chỉ một kết quả hợp lệ, không trùng phòng và không gửi thông báo lặp.
5. Đổi/hủy sau công bố: lịch cũ giữ audit, người liên quan nhận bản cập nhật, không lộ lịch nhóm khác cho sinh viên.
