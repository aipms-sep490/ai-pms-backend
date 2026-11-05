# QUYẾT ĐỊNH NGHIỆP VỤ — chốt ngày 24/10/2026

**Người chốt:** chủ dự án · **Theo đề xuất:** phân tích source BE/FE + package self-test 24/10/2026
**Hiệu lực:** áp dụng ngay cho cả 3 dev. Không dev nào được tự đổi các quyết định dưới đây.

> **Đọc file này trước khi bắt đầu task của bạn.** Nó gỡ chặn 6 câu hỏi mà các file nhiệm vụ trước đây ghi là *"chờ quyết định nghiệp vụ"*.
>
> Mỗi quyết định có **căn cứ** và **mức độ chắc chắn**. Mức "cần xác nhận lại" nghĩa là: cứ làm theo, nhưng nếu sau này đọc spec gốc thấy khác thì đổi — và lúc đó chi phí đổi đã được ghi rõ.

---

## Q1 · Member có được tạo báo cáo tiến độ nháp không?

### ✅ QUYẾT ĐỊNH: **CÓ** — Member được tạo draft. Chỉ Leader được nộp.

| | |
|---|---|
| **Tạo** draft | Leader **và** Member (thành viên đang hoạt động của project) |
| **Sửa** draft | Leader và Member — *đã đúng, không đổi* |
| **Nộp** | **Chỉ Leader** — không đổi |

### Căn cứ

1. **Report 3 UC-075** ghi actor là `Student Leader, Student Member`. Đây là mô tả tường minh, không phải suy diễn.
2. **Người làm việc là người có thông tin.** Member thực hiện task biết rõ việc đang làm, vướng mắc ở đâu. Bắt Leader ngồi gõ hộ nội dung của 4 người là tạo điểm tắc không có lý do nghiệp vụ.
3. **Rủi ro trùng đã được BE chặn sẵn.** `ProgressReportRepository.cs:195` — `ConflictException("A progress report has already been created for this reporting cycle.")`. Không thể có hai báo cáo cho cùng một kỳ, bất kể ai tạo.
4. **Tạo draft không phải hành vi có thẩm quyền.** Điểm chịu trách nhiệm là lúc **nộp**, và nộp vẫn chỉ Leader. Nên nới quyền tạo không làm mất tính chịu trách nhiệm.
5. Ca **EXE-17 PASS** đã chứng minh Member sửa được draft. Cho sửa mà không cho tạo là bất đối xứng không có lý do.

### Việc phải làm

- **BE-06**: `ExecutionCapabilityService.cs:38` đổi điều kiện `create_progress_report` từ `manager && active` sang `(thành viên đang hoạt động của project) && active`. Giữ nguyên reason code khi bị chặn.
- Giữ nguyên `submit_progress_report` chỉ cho Leader.
- **FE**: không cần sửa logic — FE đọc capability từ BE. Nhưng **FE1 F4** nên hiện **ai tạo draft** để Leader biết trước khi nộp.

### Mức độ chắc chắn: **CAO**

Spec nói rõ, guard chống trùng đã có, rủi ro thấp, chi phí 1 dòng. Nếu sai thì đổi lại cũng 1 dòng.

---

## Q2 · Thứ tự mời GVHD: `DRAFT` (hệ thống hiện tại) hay `SUPERVISOR_PENDING` (Report 3)?

### ✅ QUYẾT ĐỊNH: **GIỮ HỆ THỐNG HIỆN TẠI** — mời GVHD khi còn `DRAFT`, GVHD xác nhận rồi mới nộp đề cương. **Cập nhật Report 3 cho khớp thực tế.**

### Căn cứ

**1. Thứ tự thẩm quyền của chính dự án cho phép điều này.** `.claude/rules/ai-pms-ui-ux.md` và `ai-pms-product-experience.md` đều ghi cùng một thứ tự:

```
Quy định trường đã phê duyệt
  > Policy ProjectPeriod đang hiệu lực (+ snapshot đã đóng băng)
  > phần v5.0 team đã chọn          ← luồng đang chạy nằm ở đây
  > SRS / SDD (Report 3/4)          ← mô tả SUPERVISOR_PENDING nằm ở đây
  > đề xuất lịch sử
  > mockup
```

→ **Phần v5.0 đã triển khai xếp TRÊN SRS.** Nên giữ luồng hiện tại **không phải** vi phạm tài liệu — mà đúng theo thứ tự thẩm quyền mà dự án tự đặt ra. Việc cần làm là cập nhật SRS cho khớp, không phải sửa code theo SRS.

**2. Nghiệp vụ hợp lý hơn.** Bộ môn thẩm định một đề cương **đã có GVHD cam kết** thì đánh giá được tính khả thi: có người đủ chuyên môn nhận hướng dẫn, có capacity. Nếu duyệt trước rồi mới tìm GVHD, có thể duyệt xong mà không ai nhận — đề cương đã APPROVED nhưng không triển khai được, và không có đường lùi sạch.

**3. Chi phí đổi rất cao, lợi ích bằng không.** Đổi sang (A) đòi: thêm state `SUPERVISOR_PENDING` vào lifecycle · chuyển `SUPERVISOR_NOT_CONFIRMED`/`MENTOR_NOT_CONFIRMED` khỏi `proposalIssues` · thiết kế lại điều kiện `submit_project` · **thiết kế lại BE-01** · viết migration cho project đang chạy. Scope L/XL, rủi ro migration thật.

**4. Đã có dữ liệu thật đi theo luồng hiện tại.** `baseline` của self-test: project `#50003` ở `FINAL_SUBMISSION`, `#50004` ở `ACTIVE` — đều hình thành theo luồng B. Đổi luật nghĩa là phải xử lý dữ liệu lịch sử.

**5. Enforcement đã nhất quán với luồng B.** `SupervisorRequestWorkflow.RequireEligibilityAsync:155` — `validPrimaryStatus` gồm `DRAFT`/`REVISION_REQUIRED`/`APPROVED`/`ACTIVE`, và chỉ chấp nhận `SUPERVISOR_PENDING` khi **accepting**. Tức BE đã có một "cửa hậu tương thích" cho `SUPERVISOR_PENDING` nhưng luồng chính là B.

### Việc phải làm

- **BE-07**: **không sửa code.** Cập nhật Report 3 §3.8.3 / UC-056 cho khớp: *"Leader gửi yêu cầu hướng dẫn khi đề cương ở `DRAFT`/`REVISION_REQUIRED`; đề cương chỉ nộp được sau khi GVHD chính (và mọi Mentor theo ngành, với project liên ngành) đã xác nhận."*
- **BE-01**: tiến hành theo thiết kế hiện tại, **không chờ gì nữa**. Rủi ro "phải làm lại" đã được loại bỏ bằng quyết định này.
- Ghi vào bảng đối soát UC rằng SPEC-01 đã đóng bằng cách sửa tài liệu, không phải sửa code.

### Mức độ chắc chắn: **CAO về hướng, CẦN XÁC NHẬN LẠI về chi tiết câu chữ**

Hướng (giữ code, sửa tài liệu) rất chắc — có thứ tự thẩm quyền của dự án hậu thuẫn. Nhưng **tôi không có Report 3 bản 24/10** (SHA không khớp bản ở `F:/AI-PMS`), nên câu chữ chính xác cần người có file đó viết lại. **Việc này không chặn BE-01.**

---

## Q3 · Admin có được xem Ma trận Tiến độ Gates?

### ✅ QUYẾT ĐỊNH: **KHÔNG** — bỏ `ADMIN` khỏi registry. Menu không hiện mục đó cho Admin nữa.

### Căn cứ

1. **Admin trong hệ thống này là quản trị nền tảng, không phải giám sát học vụ.** Registry ghi `/admin/access` là *"Quản trị nền tảng"* — quyền, tài khoản, cấu hình. Ma trận Tiến độ Gates là công cụ giám sát **theo phạm vi bộ môn**.
2. **Admin đã có đường khác cho nhu cầu tổng quan.** `/admin/portfolio` — *"Tổng quan đồ án"* (ca ADM-19 PASS: dữ liệu thật + tìm kiếm). Nhu cầu "Admin xem được tình hình đồ án" **đã được đáp ứng**.
3. **Route bị bọc trong `DepartmentAcademicScopeRoute`.** Route này resolve phạm vi học vụ của Department Staff. Admin **không có** phạm vi bộ môn cụ thể. Thêm `'admin'` vào `RoleRoute` sẽ cho Admin vào một route không biết hiển thị phạm vi nào → trắng trang hoặc dữ liệu sai phạm vi. **Đó là nới quyền mù.**
4. **Menu không nên hứa cái không có.** Hiện tại menu hứa rồi guard từ chối — tệ hơn cả việc không hiện. Bỏ khỏi menu là trung thực.

### Việc phải làm

- **FE2 G1**: bỏ `'ADMIN'` khỏi `identityRoles` tại `workspace-route-registry.ts:89`. **Không** sửa router.
- **Vẫn làm test tổng quát registry↔router** — đó là phần giá trị nhất của G1, quét đủ 46 entry.
- Nếu sau này cần Admin giám sát Gates toàn trường: đó là **feature mới có thiết kế riêng** (bộ chọn bộ môn, hoặc một màn tổng hợp cấp trường), không phải sửa guard.

### Mức độ chắc chắn: **CAO**

Có đường thay thế sẵn (`/admin/portfolio`), có lý do kiến trúc rõ (không có department scope), và lựa chọn này không đóng cửa tương lai.

---

## Q4 · `issuesAndRisks` (cũ) còn dùng hay đã bị `blockers` + `risks` thay thế?

### ✅ QUYẾT ĐỊNH: `blockers` + `risks` **THAY THẾ** `issuesAndRisks`. Form mới không còn ô `issuesAndRisks`. Dữ liệu cũ vẫn đọc được.

Bố cục nội dung báo cáo chốt như sau — **7 ô nhập**:

| Nhóm | Field | Ghi chú |
|---|---|---|
| Tổng quan | `summary` | **bắt buộc** (BE là `string` non-nullable) |
| Đã làm / đang làm | `completedWork` | giữ |
| | `inProgressWork` | **mới** — trước đây thiếu hẳn |
| Vướng mắc & rủi ro | `blockers` | **mới** — việc đang bị chặn, cần người khác xử lý |
| | `risks` | **mới** — nguy cơ có thể xảy ra, chưa xảy ra |
| Kế hoạch | `nextActions` | **mới** |
| | `plannedWork` | giữ |
| *(không hiện trong form)* | `issuesAndRisks` | **chỉ đọc**, hiện khi bản ghi cũ có dữ liệu |

### Căn cứ

1. **`issuesAndRisks` gộp hai thứ khác bản chất.** *Vướng mắc* = "đang bị chặn, cần ai đó hành động ngay". *Rủi ro* = "có thể xảy ra, cần theo dõi". GVHD xử lý hai thứ này **khác nhau hoàn toàn**: vướng mắc cần can thiệp, rủi ro cần giám sát. Gộp lại làm mất khả năng phân loại.
2. **Thứ tự thêm field trên BE cho thấy ý định.** `ProgressReportDtos.cs:66-77` — 4 field mới đứng **sau** `ProgressReportPeriodId`, tất cả đều `= null`: đây là bổ sung tương thích ngược, và nhóm mới là bản chi tiết hơn của nhóm cũ.
3. **`inProgressWork` lấp một lỗ thật.** Nhóm cũ có "đã làm" và "sẽ làm" nhưng **không có** "đang làm" — chính là thông tin GVHD cần nhất để biết tiến độ hiện tại.
4. **Không cần BE đổi gì.** Mọi field trừ `summary` đều nullable. Đây thuần là quyết định thiết kế form của FE.

### Việc phải làm

- **FE1 F4**: `ReportContent` gồm đủ 8 field (7 ô nhập + `issuesAndRisks` để đọc). Form hiện 7 ô theo nhóm trên. Khi mở báo cáo cũ có `issuesAndRisks` → hiện **chỉ đọc** kèm nhãn *"Trường cũ, đã thay bằng Vướng mắc và Rủi ro"*.
- **Không** tự copy giá trị giữa `issuesAndRisks` và `blockers`/`risks`. Không đoán hộ người dùng.
- Khi tạo báo cáo mới: **không** gửi `issuesAndRisks` (để `undefined`, không gửi `null`).

### Mức độ chắc chắn: **CAO**

Lý do nghiệp vụ rõ, không cần BE đổi, dữ liệu cũ không mất.

---

## Q5 · BE trả `formula.components` hay `formula.description`?

### ✅ QUYẾT ĐỊNH: **`components`** — dữ liệu có cấu trúc. FE dựng câu từ dữ liệu. BE **không** gửi câu tiếng Việt.

Hợp đồng chốt:

```json
"formula": {
  "components": [
    { "type": "COMMON",     "weightPercent": 40 },
    { "type": "MAJOR",      "weightPercent": 40 },
    { "type": "INDIVIDUAL", "weightPercent": 20 }
  ]
}
```

- `type` ∈ `COMMON` | `MAJOR` | `INDIVIDUAL` — mã ổn định, không đổi theo ngôn ngữ.
- `weightPercent` là số nguyên, tổng các thành phần của điểm đồ án = 100.
- **Không** có field `description`. **Không** có câu tiếng Việt nào từ BE.

### Căn cứ

1. **FUNC-07 xảy ra chính vì FE tự viết câu.** `ProjectResultBreakdown.tsx:35` hard-code *"Điểm đồ án = 50% điểm chung + 50% tổng hợp theo ngành"* trong khi scheme là 100% COMMON. Nếu BE gửi `description`, ta chỉ chuyển chỗ lỗi: BE sẽ hard-code câu, và câu đó sai khi scheme đổi.
2. **Nhất quán với BE-21.** Cùng một nguyên tắc đang áp cho notification: BE trả **mã + tham số**, FE dựng câu. Không để BE giữ chuỗi hiển thị. Hai quyết định trái nhau trong cùng hệ thống là nợ kỹ thuật.
3. **Dịch được, trình bày được.** Có `components`, FE hiện được dạng câu, dạng bảng, hoặc biểu đồ tỷ trọng — tùy màn hình. Có `description` thì chỉ đọc được đúng một cách, và không dịch được.
4. **Kiểm chứng được.** FE test được "scheme 40/40/20 → hiện đúng ba trọng số". Với một chuỗi text thì chỉ test được "có hiện chuỗi BE gửi", vô nghĩa.

### Việc phải làm

- **BE-15**: trả `formula.components`. Ghi vào OpenAPI. **Không** thêm `description`.
- **FE2 G3**: xóa câu hard-code ở `ProjectResultBreakdown.tsx:35` và comment dòng 14. Render từ `components`. **BE chưa trả `formula`** → hiện *"Cách tính điểm theo phương án đánh giá đã công bố."*, **không** khẳng định con số nào.
- Thành phần chưa chấm → `PENDING`, **tuyệt đối không** hiện `0`. Tổng điểm lấy từ server.

### Mức độ chắc chắn: **CAO**

---

## Q6 · Vì sao đợt self-test tắt 19 feature flag?

### ✅ QUYẾT ĐỊNH: **Không chờ ai trả lời. Đây là việc kiểm tra, không phải câu hỏi chính sách.**

Chia 19 flag thành hai nhóm:

### Nhóm 1 — 3 flag **đã có lý do, giữ TẮT**

`.env.local` ghi tường minh: *"Tắt có chủ ý: chờ duyệt học vụ / cấu hình Google"*

| Flag | Lý do | Bật khi nào |
|---|---|---|
| `PEER_EVAL` | chờ duyệt học vụ | sau khi bộ môn phê duyệt chính sách đánh giá đồng đội |
| `RESULT_CORRECTION` | chờ duyệt học vụ | sau khi có chính sách phúc khảo được chốt |
| `INDUSTRY` | chờ cấu hình Google | sau BE-03 (CORS) + có Google client id thật + `InvitationUrl` deployed |

→ **Không bật.** 3 flag này không phải khoảng trống nghiệm thu, chúng đang đúng.

### Nhóm 2 — 16 flag còn lại: **FE2 tự kiểm, từng cái một**

Cách làm, mỗi flag mất vài phút:

```
1. Bật flag → khởi động lại dev server
2. Mở route tương ứng
3. Đọc tab Network:
   endpoint trả 2xx          → nhóm B: chạy được, nghiệm thu ngay
   endpoint 404 / 501        → nhóm A: BE chưa deploy, ghi yêu cầu BE, tắt lại
   endpoint 403 có reason    → BE có nhưng chặn quyền, ghi lại để kiểm bằng role khác
```

### Căn cứ

1. **Không ai cần quyết định gì cả.** Đây là một **sự thật kiểm tra được**, không phải lựa chọn chính sách. Biến nó thành câu hỏi chặn là tự tạo điểm tắc.
2. **BE có code cho mọi module đó.** Đã kiểm `src/AIPMS.Application/Features/`: `ReviewGates`, `Chat`, `Appeals`, `PeerEvaluations`, `WeeklyAssessments`, `Cold`, `Industry`, `Sprints`, `Meetings`, `ExecutionCapabilities`, `MilestoneTemplates` — **đều tồn tại**. Nên khả năng B (chạy được ngay) cao hơn A.
3. **Chi phí kiểm rất thấp**, kết quả dùng được ngay cho cả BE-18.

### Việc phải làm

- **FE2 G9**: thực hiện vòng kiểm 16 flag như trên. Kết quả ghi thành bảng: flag · route · endpoint · HTTP status · nhóm A/B · ghi chú.
- Flag nhóm A → gửi danh sách endpoint thiếu cho BE dev, nhập vào **BE-18**.
- Flag nhóm B → nghiệm thu 13 UC AI ngay.
- Commit `.env.acceptance` với 16 flag bật + 3 flag nhóm 1 tắt kèm comment lý do.

### Mức độ chắc chắn: **CAO về cách làm**

Nhóm 1 có lý do ghi trong `.env.local`, không tranh cãi. Nhóm 2 là việc kiểm, kết quả tự nói.

---

## Hai câu nhỏ — chốt luôn

### Bấm deep link thông báo có tự đánh dấu đã đọc?

### ✅ **CÓ** — mở deep link thì đánh dấu đã đọc.

Căn cứ: người dùng đã mở đích để xem nội dung, tức đã đọc. Để họ phải bấm thêm "Đánh dấu đã đọc" là thao tác dư. **Giữ** nút đánh dấu riêng cho trường hợp xử lý nhanh cả danh sách mà không mở từng cái.

→ **FE2 G4**: `onClick` của deep link gọi `onRead()` trước khi điều hướng. Nếu `onRead()` lỗi → **vẫn điều hướng** (không chặn người dùng vì một lỗi phụ), ghi log.

### `ProjectGovernanceDto.canPublish` — xóa được chưa?

### ✅ **KIỂM RỒI MỚI XÓA, trong phạm vi BE-16.**

Căn cứ: audit trước ghi nhận FE không tiêu thụ field này. Nhưng *"ghi nhận"* không phải *"đã kiểm"*.

→ **BE-16**: grep toàn repo FE tìm `canPublish`. Không có kết quả nào ngoài khai báo type → xóa cả hai phía trong cùng PR. Có nơi dùng → **giữ**, ghi rõ nơi nào dùng. Không xóa field DTO dựa trên ghi chú của người khác.

---

## Tóm tắt: quyết định nào gỡ chặn task nào

| Q | Quyết định | Gỡ chặn | Cần code? |
|---|---|---|---|
| Q1 | Member được tạo draft | **BE-06** | BE: 1 dòng · FE: hiện người tạo |
| Q2 | Giữ luồng hiện tại, sửa Report 3 | **BE-07**, và **BE-01 chạy ngay không chờ** | **không** — chỉ sửa tài liệu |
| Q3 | Admin không xem Ma trận Gates | **FE2 G1** | FE: bỏ 1 role khỏi registry |
| Q4 | `blockers`+`risks` thay `issuesAndRisks` | **FE1 F4** | FE: thiết kế form 7 ô |
| Q5 | `formula.components` | **BE-15**, **FE2 G3** | BE: thêm field · FE: render |
| Q6 | Tự kiểm, không chờ | **FE2 G9** | không — việc kiểm |
| phụ | Deep link tự đánh dấu đã đọc | FE2 G4 | FE: 1 lời gọi |
| phụ | `canPublish` kiểm rồi mới xóa | BE-16 | BE: grep + xóa nếu sạch |

**Không còn task nào bị chặn bởi câu hỏi nghiệp vụ.** Cả 3 dev bắt đầu được ngay.
