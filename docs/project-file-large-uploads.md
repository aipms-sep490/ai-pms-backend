# Kho tệp đồ án: tải tối đa 500 MiB

## Phạm vi

Nguồn yêu cầu: người dùng chọn mức 200–500 MiB cho Kho tệp đồ án. Giới hạn mới
là 524.288.000 byte/file, áp dụng tệp gắn với TASK, REPORT, MEETING. PDF, DOCX,
ZIP, PNG, JPG, JPEG được giữ nguyên. Chatbot, hạng mục nộp phiên bản, chứng chỉ,
avatar và các chính sách khác giữ giới hạn riêng.

`PROJECT_FILE` công bố `maxBytes=500 MiB`, `chunkSizeBytes=4 MiB`,
`singleRequestMaxBytes=20 MiB`. Route multipart cũ vẫn nhận tối đa 20 MiB/file,
22 MiB/request. Client phải dùng phiên tải cho file lớn; không nâng trần HTTP
toàn hệ thống lên 500 MiB.

## Luồng và hợp đồng

Base: `/api/v1/files/upload-sessions`, bắt buộc đăng nhập.

| Thao tác | Kết quả |
|---|---|
| POST base với parentType, parentId, fileName, contentType, sizeBytes | 201, tạo phiên thuộc tài khoản hiện tại |
| GET /{sessionId} | 200, offset/trạng thái hiện tại; kiểm tra lại quyền đọc |
| PUT /{sessionId}/chunks?offset=N với application/octet-stream | 200, nhận tối đa 4 MiB; xác nhận nextOffset |
| POST /{sessionId}/complete | 202, chuyển sang PROCESSING hoặc trả trạng thái đã xử lý |
| DELETE /{sessionId} | 204, hủy phiên UPLOADING/FAILED; không hủy khi đang lưu hoặc đã hoàn tất |

Trạng thái công khai: UPLOADING → PROCESSING → COMPLETED hoặc FAILED.
Gửi đủ byte chưa phải thành công: phải có COMPLETED và file metadata. GET/complete
đọc lại file thật, không trả thành công dựa trên metadata phiên đã lỗi thời.

Trả 400 cho dữ liệu sai, 401 khi chưa đăng nhập, 403 khi mất quyền, 404 với phiên
khác tài khoản/đã hết hạn, 409 khi offset/trạng thái/quota không phù hợp, 413 khi
vượt giới hạn. Retry phần đã nhận chỉ thành công nếu nội dung trùng khớp.

## Bảo toàn dữ liệu và vận hành

- Phiên và offset lưu trên đĩa; dữ liệu được flush trước khi thay metadata nguyên
  tử. Sau restart, phần đuôi chưa xác nhận bị cắt trước khi nhận lại.
- Worker xử lý tuần tự, có khóa phiên; lưu file ra storage ngoài transaction SQL.
  Tài khoản, quyền và trạng thái được kiểm tra lại dưới khóa đồ án khi chốt metadata.
- Storage key cố định theo phiên và SHA-256 toàn file giúp phục hồi sau crash/lost
  commit acknowledgement, tránh tạo file/audit trùng. Không xóa object khi SQL
  chưa xác nhận kết quả commit; cleanup đối chiếu tham chiếu DB trước khi xóa.
- Mặc định 2 phiên hoạt động/tài khoản, 8 GiB tổng dung lượng đăng ký, hạn 24 giờ.
  Kiểm tra đĩa trống có dự phòng cho cả file tạm và bản sao tại local storage.
  File đã lưu thành công vẫn tồn tại khi metadata phiên hết hạn.
- `FileStorage:UploadSessionsPath` mặc định dưới `FileStorage:RootPath/.upload-sessions`.
  Thư mục phải nằm trên volume riêng tư, có quyền ghi, được giữ khi recreate API.
  Đổi quota bằng `FileStorage:UploadSessionQuotaBytes` (tối thiểu 500 MiB).
- Metadata hỏng được cách ly thành `.quarantined`, giữ nội dung để đối chiếu thủ
  công, không làm dừng các phiên khác. Không tự xóa file remote có nguồn không rõ.
- ZIP tối đa 10.000 entry, directory 16 MiB, tổng kích thước giải nén khai báo 2 GiB;
  DOCX tối đa 200 MiB giải nén. Không nhận ZIP64 hoặc archive nhiều volume.
  Đây là kiểm tra định dạng/giới hạn, không thay thế quét mã độc.
- Google Drive có deadline truyền riêng, tải xuống dự án theo luồng có backpressure;
  các luồng cần seek dùng spool đĩa có giới hạn. Xem `google-drive-email.md`.
- Runtime đã kiểm tra đang chọn Local; không đổi provider hoặc chuyển tệp cũ sang Drive.
  Cần kế hoạch di chuyển dữ liệu riêng nếu đổi provider vì metadata chưa ghi provider từng file.

## Kiểm chứng ngày 18/11/2026

IMPLEMENTED: nhánh `feat/project-files-500mib` ở cả BE/FE; chưa merge/deploy.

TESTED:

- 1.431 backend unit tests đạt; 60 integration tests liên quan đạt trên SQL thật
  trong database cô lập, bao gồm file 200 MiB và đúng 500 MiB, tải xuống đối chiếu
  SHA-256, sai quyền, khóa parent, retry chunk/complete, vượt 500 MiB.
- 2.291 frontend tests đạt; 48 test liên quan chạy lại sau chỉnh sửa cuối đạt.
- Backend Release build 0 warning/error; frontend production build đạt.
- Cấu hình nginx được kiểm tra bằng nginx thật trong Docker; proxy không buffer
  toàn bộ download lớn và vẫn chỉ nhận các request multipart/chunk có giới hạn.

RUNTIME VERIFIED tại môi trường thử riêng: API Kestrel thật + SQL + local storage,
đăng nhập bằng mật khẩu/JWT, upload 500 MiB theo chunk, restart API giữa chừng,
tiếp tục cùng offset, từ chối chunk quá cỡ bằng 413, complete và download có
Content-Length/SHA-256 khớp. Script tái hiện:

```powershell
# Cấp connection qua biến môi trường riêng; script chỉ tạo/xóa catalog E2E sở hữu.
./scripts/test-v5-api-login.ps1 -LargeProjectFileMiB 500 -OutputPath <evidence.json>
```

Chưa xác minh Google Drive thật hoặc upload trên website đang chạy. Trình duyệt
không hỗ trợ Save File Picker dùng Blob fallback khi download, có thể tốn bộ nhớ.
Resume sau reload cần chọn lại file gốc; nếu mất phản hồi tạo phiên ban đầu trước
khi nhận ID thì phiên giữ chỗ chỉ được giải phóng khi hết hạn hoặc đối chiếu thủ công.
