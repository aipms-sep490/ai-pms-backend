# BE-16 bổ sung: lịch offline Gate 3 và Gate 4 — bàn giao FE E2E/UAT

Ngày 2026-11-08. Nhánh BE: `feat/be-period-rooms-defense-workflow-20261107`, tách từ `origin/develop` `2df464ba`. Tham chiếu FE UI/UX: `feat/fe-uiux-standardisation-20261106`; nhánh FE sơ đồ tầng và khóa phòng/ca: `feat/fe-room-floor-locks-20261108` tại `c1a06da`.

## Quyết định nghiệp vụ

- Gate 1, Gate 2 họp online. Gate 3 review và Gate 4 defense họp offline tại trường.
- Mỗi lần điều phối theo một Project Period. Admin thấy và công bố lịch toàn trường; `DEPARTMENT_STAFF` chỉ thao tác nhóm có khoa chủ trì trong snapshot đăng ký. Không tạo role `DEPARTMENT_HEAD`.
- Gate 3 chỉ được xếp khi Gate 2 `PASSED`; Gate 4 chỉ được xếp khi Gate 3 `PASSED`. Cửa sổ thời gian phải lấy từ `ProjectGateInstance.WindowStart/WindowEnd` của Gate tương ứng. Trạng thái Gate đích đã kết thúc không được xếp mới.
- Admin hoặc nhân viên khoa chọn phòng tầng 2–4, ngày và khóa phòng/ca **cho đúng lần preview/apply này** bằng `exclusions`. Khóa không lưu vào DB. Preview chỉ đề xuất; `apply` là bước duyệt và công bố lịch. Không có tác vụ tự chạy ngầm khi Gate mở.
- Tất cả lịch dùng 6 ca 75 phút theo giờ Việt Nam, ngày làm việc của kỳ, phòng đang hoạt động, đủ sức chứa nhóm + người chấm + 5 ghế dự phòng. Phòng, nhóm và giảng viên/phản biện không được trùng giờ. `apply` khóa lịch trong transaction, kiểm lại revision và mọi ràng buộc.
- Gate 3 cần hai người được gán, có `CHAIR` và `SECRETARY` khác nhau, còn hoạt động và không phải người hướng dẫn của nhóm. BE lưu lịch đã công bố vào `gate_session_scripts` với `publishedAt/publishedBy`. Chủ tọa/thư ký sửa nội dung biên bản nhưng không sửa phòng/giờ; Admin hoặc nhân viên khoa chủ trì đổi phòng/ca trước khi phiên bắt đầu, có lý do và token.
- Gate 4 cần hội đồng đã gán. Chủ tọa/thư ký gửi yêu cầu đổi lịch, Admin hoặc nhân viên khoa chủ trì duyệt/từ chối. Duyệt kiểm lại toàn bộ điều kiện và gửi thông báo. Admin không có quyền ghi đè kết quả Review Gate; quyền đó vẫn thuộc `DEPARTMENT_STAFF` khoa chủ trì.
- Khi công bố/đổi lịch, BE tạo thông báo trong app và hàng đợi email cho thành viên nhóm, người đánh giá và người hướng dẫn đang hoạt động. FE cần đọc lại lịch từ API sau thao tác; không tự coi bản preview là lịch chính thức.

## API FE cần nối

| Mục đích | API | Quyền |
| --- | --- | --- |
| Danh mục phòng | `GET /api/v1/rooms` | actor được cấp quyền danh mục |
| Gate 3 preview | `POST /api/v1/project-periods/{periodId}/review-gates/r3/schedule/preview` | Admin toàn kỳ; staff khoa chủ trì |
| Gate 3 công bố | `POST /api/v1/project-periods/{periodId}/review-gates/r3/schedule/apply` | như preview |
| Gate 3 lịch đã công bố | `GET /api/v1/project-periods/{periodId}/review-gates/r3/schedule` | Admin toàn kỳ; staff khoa chủ trì |
| Gate 3 đổi phòng/ca | `PUT /api/v1/project-periods/{periodId}/review-gates/r3/schedule/{gateInstanceId}` | Admin; staff khoa chủ trì |
| Gate 3 lịch/biên bản của nhóm | `GET /api/v1/review-gates/{gateInstanceId}/session-script` | sinh viên nhóm, người hướng dẫn, reviewer và staff đúng scope |
| Gate 4 preview | `POST /api/v1/project-periods/{periodId}/defense-sessions/auto-schedule/preview` | Admin toàn kỳ; staff khoa chủ trì |
| Gate 4 công bố | `POST /api/v1/project-periods/{periodId}/defense-sessions/auto-schedule/apply` | như preview |
| Gate 4 lịch toàn kỳ | `GET /api/v1/project-periods/{periodId}/defense-sessions` | theo scope BE |
| Gate 4 yêu cầu đổi lịch | `POST /api/v1/defense-sessions/{sessionId}/change-requests` | chair/secretary hội đồng |
| Gate 4 duyệt yêu cầu | `POST /api/v1/defense-sessions/change-requests/{requestId}/decision` | Admin; staff khoa chủ trì |
| Gate 4 danh sách yêu cầu | `GET /api/v1/project-periods/{periodId}/defense-sessions/change-requests?status=PENDING` | Admin toàn kỳ; staff khoa chủ trì |

Request/response mẫu: [`gate3-auto-schedule-preview.json`](../contracts/cib-v4/examples/review-gates/gate3-auto-schedule-preview.json), [`gate3-auto-schedule-apply.json`](../contracts/cib-v4/examples/review-gates/gate3-auto-schedule-apply.json), [`gate3-schedule-change.json`](../contracts/cib-v4/examples/review-gates/gate3-schedule-change.json), và các mẫu Gate 4 trong `docs/contracts/cib-v4/examples/defense/`.

OpenAPI đầy đủ đã xuất từ BE của nhánh này bằng host offline (không kết nối shared DB): [`openapi.json`](../contracts/cib-v4/openapi.json). FE dùng file này để đối chiếu DTO/path; các JSON ví dụ là kịch bản minh họa, không phải ID/tài khoản thật.

`preview` trả `allocations`, `conflicts`, `concurrencyToken`. FE phải đưa nguyên `allocations` và token sang `apply`; nếu user khóa phòng/ca sau preview, chạy preview lại. `exclusions` gồm `{roomId,date,slotNumber}`: `date=null,slotNumber=null` khóa cả phòng trong khoảng ngày đã chọn; một ngày và một ca khóa đúng ô đó. Thời gian truyền dưới dạng ISO 8601 có offset `+07:00`. `publishedAt`/`publishedBy` trong Gate 3 DTO là dấu hiệu lịch chính thức.

## Lỗi validation FE phải hiển thị

| Mã/HTTP | Xử lý UI |
| --- | --- |
| 400 `errors.reviewGate3Schedule` hoặc `errors.autoSchedule` | giữ form, chỉ rõ phạm vi ngày, phòng, ca, khóa trùng/không hợp lệ |
| 403 | ẩn hành động không thuộc vai trò/scope; nếu nhận 403 khi đang mở màn hình thì thông báo và tải lại quyền |
| 200 `conflicts[].reason`: `REVIEW_GATE_2_NOT_PASSED`, `REVIEW_GATE_3_NOT_PASSED` | đưa nhóm vào danh sách chưa đủ điều kiện, không gán phòng; đường ghi thủ công/đổi lịch trả 409 tương ứng |
| 200 `conflicts[].reason`: `REVIEW_GATE_3_WINDOW_REQUIRED`, `DEFENSE_GATE_WINDOW_REQUIRED` | yêu cầu cấu hình Gate window; đường ghi ngoài cửa sổ trả 409 `REVIEW_GATE_3_OUTSIDE_WINDOW` hoặc `DEFENSE_OUTSIDE_GATE_WINDOW` |
| 200 `conflicts[].reason`: `REVIEW_GATE_3_REVIEWERS_REQUIRED`, `DEFENSE_COMMITTEE_REQUIRED`, `SUPERVISOR_CONFLICT_OF_INTEREST` | đưa vào bảng cần hoàn thiện nhân sự |
| 409 `REVIEW_GATE_3_ROOM_UNAVAILABLE`, `DEFENSE_ROOM_UNAVAILABLE`, `DEFENSE_ROOM_CAPACITY` | kiểm tra phòng đang hoạt động, tầng 2–4 khi tự xếp, và sức chứa |
| 200 `conflicts[].reason=NO_ROOM_OR_CONFLICT_FREE_SLOT`; 409 `REVIEW_GATE_3_SCHEDULE_CONFLICT`, `DEFENSE_SCHEDULE_CONFLICT`, `DEFENSE_LECTURER_CONFLICT` | giữ nhóm chưa xếp; cho Admin mở thêm ngày/phòng hoặc bỏ khóa ca |
| 409 `REVIEW_GATE_3_SCHEDULE_STALE`, `DEFENSE_SCHEDULE_STALE`, `DEFENSE_CHANGE_STALE` | bỏ bản preview cũ, đọc lại lịch và preview lại |
| 409 `REVIEW_GATE_3_SCHEDULE_LOCKED`, `REVIEW_GATE_3_SCHEDULE_NOT_PUBLISHED` | chair/secretary không được đổi phòng/ca hoặc nhập biên bản trước khi công bố |

Không ghép trạng thái `scheduledCount` với số nhóm toàn kỳ để suy ra “tất cả đã xếp”: các nhóm không đủ điều kiện hiện trong `conflicts`; các nhóm đã có lịch được đọc từ endpoint danh sách.

## Tác động giao diện và role

| Vai trò | FE hiển thị | Hành động |
| --- | --- | --- |
| Admin | sơ đồ FAMS tầng 2/3/4, tất cả nhóm của kỳ, phân loại đã xếp/chưa đủ điều kiện/chưa có chỗ, lịch và yêu cầu đổi lịch | chọn ngày/phòng, khóa ca cho lần chạy, preview, xem xung đột, duyệt công bố, đổi Gate 3, duyệt Gate 4 |
| `DEPARTMENT_STAFF` khoa chủ trì | cùng công cụ nhưng chỉ nhóm khoa mình; không nhìn/đổi nhóm khoa khác | phân công nhóm trong scope; duyệt yêu cầu Gate 4 thuộc scope |
| Reviewer/chair/secretary Gate 3 | phòng, ca, nhóm, biên bản; lịch công bố chỉ đọc | chair/secretary ghi biên bản, điểm danh, ký; không sửa phòng/giờ |
| Chair/secretary hội đồng Gate 4 | lịch defense của hội đồng và trạng thái yêu cầu đổi lịch | gửi/thu hồi yêu cầu, theo dõi duyệt |
| Sinh viên nhóm, người hướng dẫn | lịch Gate 3/4 của nhóm và thông báo | xem phòng/giờ, không chỉnh lịch |

## E2E/UAT giao FE

Chuẩn bị fixture **riêng trong môi trường test**: một kỳ có ít nhất 3 khoa, 6 nhóm; Gate 2/3/4 có cửa sổ thời gian, nhóm A/B đạt Gate 2, C chưa đạt; sau Gate 3 cho A đạt, B chưa đạt; hai phòng đang hoạt động tầng 2 và 3, một phòng bị khóa toàn lần chạy, một ca bị khóa; một phòng nhỏ không đủ ghế; hai reviewer chung giữa hai nhóm; một ngày nghỉ trong period calendar. Dùng tài khoản thật theo role/scope đã cấp; không seed tài khoản giả vào staging.

| ID | Thao tác FE | Kết quả cần đối chiếu API/DB |
| --- | --- | --- |
| E2E-01 | Admin mở sơ đồ tầng 2–4, chọn kỳ và ngày Gate 3 | phòng lấy từ catalog, không dựng ID giả; lịch đã công bố lấy từ GET |
| E2E-02 | Khóa cả phòng và một ca rồi preview Gate 3 | ô khóa không có allocation; nhóm C có `REVIEW_GATE_2_NOT_PASSED`; preview không ghi DB |
| E2E-03 | Mở thêm ngày trong Gate 3 window rồi preview | nhóm đủ điều kiện chuyển sang ca/ngày khác; không vượt window, ngày nghỉ, sức chứa |
| E2E-04 | Hai Admin preview cùng lịch, Admin thứ nhất apply | Admin thứ hai nhận 409 stale; không có hai lịch/phòng trùng ca |
| E2E-05 | Admin apply Gate 3 và tải lại | GET trả `publishedAt`, nhóm/reviewer/supervisor nhận một notification; không có thông báo trước apply |
| E2E-06 | Chair sửa biên bản Gate 3 rồi thử đổi giờ trong form biên bản | biên bản lưu; đổi giờ bị 409; Admin đổi lịch bằng PUT có lý do và token, thông báo lịch mới |
| E2E-07 | Staff khoa A mở kỳ và thử sửa nhóm khoa B | chỉ thấy nhóm khoa A; ghi khác khoa 403 |
| E2E-08 | Trước khi Gate 3 đạt, preview và tạo lịch Gate 4 thủ công | nhóm bị loại `REVIEW_GATE_3_NOT_PASSED`/409; không có DefenseSession |
| E2E-09 | Cho nhóm A đạt Gate 3, preview Gate 4 với khóa phòng/ca | lịch chỉ trong R4 window, không trùng Gate 3 hay phòng/hội đồng khác; phòng nhỏ bị loại |
| E2E-10 | Apply Gate 4, tải lại lịch và thông báo | DB có đúng một DefenseSession SCHEDULED/nhóm; sinh viên, hội đồng, người hướng dẫn nhận lịch |
| E2E-11 | Chair gửi đổi phòng/ca Gate 4; Admin xem, duyệt | lịch đổi atomically, token cũ 409, thông báo thay đổi; một yêu cầu PENDING duy nhất |
| E2E-12 | Kiểm tra mobile/keyboard | sơ đồ tầng cuộn và đọc được, ô khóa có tên/phòng/ca dạng text, trạng thái xung đột không chỉ dùng màu |

UAT cần ký bởi Admin điều phối, một nhân viên khoa, reviewer Gate 3, chair Gate 4 và một nhóm sinh viên. Đối chiếu readback API sau mỗi lần ghi; browser mock/intercept chỉ là kiểm thử UI, không được ghi là UAT liên thông.

## Trạng thái xác minh và DB

- Build BE Release sạch (0 warning, 0 error), 1.285/1.285 unit tests, 24/24 ca AcademicTracking + Composite Major và 33/33 ca ReviewGate đã qua trên SQL Server cục bộ cô lập. Lần kiểm tra bổ sung đạt 19/19 AcademicTracking + ReviewGate3 và 1/1 rút phúc khảo trên DB cô lập. Đây không thay thế nghiệm thu đầy đủ trên DB UAT.
- Lần chạy toàn bộ integration repo được dừng sau hơn 10 phút vì chiếm gần 5 GB bộ nhớ và chưa hoàn tất; không tính lần đó là pass.
- Ngày 08/11/2026, 7 migration BE-16 đã chạy trong một giao dịch trên DB UAT `AI_PMS`; kết nối mới đọc lại đủ 7/7 bảng/cột bắt buộc. `20261108_publish_gate3_schedules.sql` thêm `published_at/published_by` vào `gate_session_scripts`; script offline Gate 3 cũ được đánh dấu công bố với `publishedAt=createdAt`, `publishedBy=null` nếu có lịch sử. DB hiện chưa có phiên Gate 3 cũ.
- Đã tạo qua API 6 phòng **mẫu UAT** tầng 2–4 (`ALPHA/BETA/GAMMA`, chưa phải mã phòng vật lý đã xác nhận). Kỳ `FE-ACCEPT-EVAL-FA26` có lịch Thứ 2–Thứ 7 (`workingDaysOfWeek=126`) và ngày đóng cửa mẫu 20/11. BE login bằng tài khoản thật cho 4 vai trò đều 200; FE chạy với BE mới cho thấy Admin/Staff vào sơ đồ phòng, Lecturer/Student về workspace đúng role. Khóa phòng `ALPHA_202` được FE gửi trong `exclusions` khi preview và DB vẫn giữ `ACTIVE`.
- Preview Gate 3 trả 3 nhóm `REVIEW_GATE_2_NOT_PASSED`; Gate 4 trả 3 nhóm `REVIEW_GATE_3_NOT_PASSED`, không xếp lịch. Cả 3 nhóm ở kỳ UAT còn Gate 2 `PLANNED`; Gate 3 chưa có window, DB chưa có hội đồng, phiên bảo vệ, kết quả hoặc đơn phúc khảo. Do đó 12 kịch bản E2E/UAT và ký duyệt 5 vai trò **chưa đạt**. Sổ tay ngoài repo `SO_TAY_THUC_HIEN_BE_DEV_E2E_UAT.md` ghi tình trạng từng kịch bản và đính chính URL minh họa.
- Một lần khởi động BE ban đầu khoảng 11 giây với worker mặc định trùng thời điểm 2 email delivery (notification recipient `70014`, `70015`) được SMTP chấp nhận và 28 recipient được đánh dấu chuyển realtime. Tiến trình đã dừng; lần chạy UAT sau đó tắt email/realtime workers. Chưa xác nhận email đến hộp thư, không tự đảo trạng thái.
- Những test hiện có kiểm chứng logic và regression nên được giữ. Không phát hiện file test tạm/không cần thiết thuộc thay đổi BE này để xóa.
