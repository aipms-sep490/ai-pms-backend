# Kế hoạch trợ lý hỗ trợ nghiệp vụ đồ án

Ngày: 03/11/2026. Nhánh BE/FE: `feat/assistant-workflow-guidance`.
Baseline develop: BE `01ec826`, FE `d56d497`; tích hợp phần tự điền đã triển khai (`ddb10e3`, `78d04f0`).

## Mục tiêu và nguyên tắc

Người dùng mở chatbot ở đồ án, chọn tab Hỗ trợ nghiệp vụ, chọn nhu cầu và nhận kết quả có nguồn, thời điểm kiểm tra và đường dẫn phù hợp vai trò. AI điền sẵn biểu mẫu; người dùng kiểm tra rồi lưu. Không tự nộp, gửi lời mời, đổi task, điểm, trạng thái hay deadline.

Backend quyết định điều kiện và quyền. Các phép kiểm tra nghiệp vụ chạy xác định, hoạt động cả khi model không có key/lỗi; không dùng văn bản tự do của model để cấp quyền hoặc quyết định sẵn sàng nộp. Nội dung báo cáo/biên bản là dữ liệu, không phải chỉ dẫn hệ thống.

## Các phần triển khai

| Nhu cầu | Dữ liệu và xử lý | Kết quả |
|---|---|---|
| Bước tiếp theo | Trạng thái đồ án, capabilities hiện hành, task quá hạn/blocked trong phạm vi | Tối đa 5 ưu tiên; vai trò xử lý; mở trang nguồn |
| Vì sao bị chặn | Tái sử dụng my-capabilities; lý do backend được dịch sang tiếng Việt | Chọn thao tác, xem điều kiện thiếu và người xử lý; mã lạ không lộ ID và không đoán |
| Kiểm tra trước khi nộp | Gọi checklist bàn giao chính thức, gồm kiểm tra phiên bản/file; không tính lại điều kiện | Thiếu hạng mục nào, deadline, CanSubmit tại thời điểm kiểm tra; chỉ thành viên có quyền xem checklist |
| Chuẩn bị gặp GVHD | Task mở/quá hạn/blocked, báo cáo gần nhất được phép xem | Bản nháp agenda; câu hỏi cần trao đổi; chưa tự đặt lịch/người tham gia |
| Việc sau cuộc họp | Chọn cuộc họp từ danh sách được phép xem, đọc biên bản/quyết định đã ghi | Bản nháp task để xác nhận; không khẳng định đã phân công, không tự đoán người/hạn |
| Đối chiếu tiến độ | Task DONE chưa có file minh chứng; task còn mở được nhắc nguyên tên trong mục đã hoàn thành của báo cáo | Gắn nhãn cần đối chiếu, dẫn đến nguồn; không kết luận sai phạm hoặc bắt buộc mọi task phải có file |
| Phối hợp liên ngành | Dependency thực tế, ngành gắn task và người phụ trách; cả hai đầu dependency phải thuộc scope | Task đang chờ việc gì, thuộc ngành nào; không suy dependency từ tên ngành |
| Tóm tắt cho GVHD/Khoa | Tổng hợp dữ liệu hiện hành của đồ án đang mở theo quyền | Việc cần hỗ trợ, báo cáo có vướng mắc, deadline; không suy ra xếp hạng toàn khoa từ một đồ án |

## Contract và kiến trúc

- Thêm GET `/api/v1/projects/{projectId}/ai/workflow-assistance?mode=...&meetingId=...`.
- Mode: NEXT_STEPS, BLOCKERS, SUBMISSION_CHECK, MEETING_PREP, MEETING_FOLLOWUP, CONSISTENCY, COORDINATION, SUPERVISOR_BRIEF.
- Response: projectId, mode, generatedAt, summary, status, items (title/detail/owner/source/target), limitations, optional draft, meeting options.
- Target là loại trang/resource do backend tạo; FE dùng allowlist và route theo vai trò, không điều hướng tới URL do model hoặc nội dung tài liệu cung cấp.
- Authorization: dùng AiActorScope; task theo assignment/ngành. Biên bản chỉ cuộc họp có người hỏi tham gia. Checklist dùng nguyên workflow hiện có, không mở rộng quyền đọc.
- Giới hạn truy hồi được ghi rõ: task 100, báo cáo 5, cuộc họp 5, dependency 100. Kết quả không có dữ liệu không được coi là đã sẵn sàng.
- Không thêm schema/migration. Snapshot được tải lại khi đổi nhu cầu/đồ án; audit chỉ ghi mode/count, không ghi nội dung sinh viên. Không lưu snapshot này vào hội thoại cũ để tránh quyền đã đổi vẫn xem được.
- Giữ nguyên chức năng chat và tự điền đã triển khai. Model có thể tiếp tục soạn thảo ở tab trò chuyện; kết luận nghiệp vụ tại tab này dựa trên dịch vụ backend.

## UI và hành vi

1. Thêm tab Hỗ trợ nghiệp vụ trong chatbot ở trang đồ án.
2. Dropdown 8 nhu cầu, nút Kiểm tra lại, trạng thái đang tải/lỗi/rỗng.
3. Mỗi mục có nguồn, vai trò xử lý và nút mở trang khi route của vai trò tồn tại.
4. Biên bản cần chọn cuộc họp; không mặc định lấy nhầm cuộc họp.
5. Bản nháp agenda/task mở form đang có, không gọi API tạo dữ liệu.
6. Chuyển project hủy request và bỏ kết quả cũ; chặn request trùng.
7. Hiển thị rõ: kiểm tra tại thời điểm nào; hành động lưu/nộp vẫn được backend kiểm tra lại.

## Kiểm thử và nghiệm thu

- Unit: mapping lý do có suffix ID, mã chưa biết, ưu tiên next steps; không có dữ liệu; biên bản trống; không suy người/hạn.
- Integration SQL riêng: không truy cập project ngoài scope; mentor không đọc task/ngành khác hoặc dependency đầu bên ngoài; meeting ngoài project hoặc không tham gia bị từ chối; task/bàn giao không bị thay đổi; checksum/version sử dụng workflow chuẩn.
- FE: loading/error/retry; mode mới; chọn cuộc họp; link allowlist theo vai trò; mở draft chưa mutation; chuyển project không giữ kết quả cũ.
- Build BE/FE, cập nhật OpenAPI, chạy regression AI/forms.
- Deploy staging cùng cấu hình hiện có; xác minh SHA, health, assets và auth. Không sửa SQL chung.

## Phạm vi nghiệm thu cần dữ liệu thật

Tổng hợp GVHD/Khoa trong lần này là từng đồ án đang được cấp quyền, không phải dashboard xếp hạng nhiều nhóm. Nhận xét nội dung ngữ nghĩa sâu và tự trích xuất người/hạn từ biên bản được giữ dưới dạng bản nháp cần kiểm tra. Checklist ở đây là bàn giao cuối kỳ; không khẳng định nó thay thế toàn bộ điều kiện từng review gate, hồ sơ đăng ký hoặc từng báo cáo.

## Kết quả triển khai

- Đã có endpoint và tab Hỗ trợ nghiệp vụ với đủ 8 chế độ.
- Bổ sung lý do nộp/nộp lại đề xuất, mời GVHD, cam kết nhóm, thao tác thực thi và chấm/công bố.
- Biên bản được chọn đọc tối đa 6.000 ký tự, tối đa 8 gợi ý từ dòng ghi chú; người dùng xác nhận nội dung và chống trùng task trước khi tạo.
- Kiểm thử SQL riêng: 11 integration test qua (8 mode, scope mentor/dependency, biên bản được mời, đối chiếu và không mutation).
- Regression FE: 133 test qua; sau bổ sung cuối, nhóm kiểm thử tab/dock/điều hướng: 14 test qua.
- 4 unit test mapping lý do qua. Kiểm tra trình duyệt Edge ở 1280x900 và 390x844: không lỗi trang, không tràn ngang.
- Không cần migration. Không thay cấu hình hoặc dữ liệu SQL dùng chung để nghiệm thu.
