# Kế hoạch: Tài liệu bàn giao BE — 3 quy tắc nghiệp vụ đăng ký còn thiếu (hợp nhất, 13/10/2026)

## Context

Toàn bộ FE v5 phiên này đã merge vào `develop` (PR #58) và đang được polish UI/UX (PR #59–#63). Phần lớn FE chỉ **đối chiếu lại contract** với BE đã ship (sprint/chat/dashboard/meeting/AI, COLD/weekly/checkpoint/defense/evidence…) — không cần BE làm thêm. Nhưng **3 quy tắc nghiệp vụ đăng ký cốt lõi mà người dùng đã chốt** vẫn **chưa được BE triển khai**, và đây là khoảng trống lớn nhất chặn hệ thống vận hành đúng nghiệp vụ đầu-cuối:

1. **D18 (Phương án B):** Supervisor + Mentor phải **ACCEPT trước khi được Submit** đăng ký — hiện BE làm **ngược** (mời supervisor chỉ mở SAU khi Department duyệt).
2. **Chống trùng đề tài** với 3 kỳ gần nhất.
3. **Pre-check nội dung** Student Proposal trước khi submit.

**Mục tiêu tài liệu này:** giao cho dev BE một bản kế hoạch hợp nhất, đã **verify trực tiếp source `develop` hiện tại (3337e60) + live DB `AI_PMS`** (người dùng cấp quyền đọc), ghi rõ: nghiệp vụ, luồng, code cần sửa/thêm, DB cần cập nhật gì, contract API + reason code, test bắt buộc, thứ tự làm. Thay thế/nối tiếp bản handoff cũ (`BE_HANDOFF_D18_TOPIC_PROPOSAL_2026-10-12.md`, viết tại `e625ee2`) — nội dung vẫn đúng vì **domain đăng ký/supervisor/topic/proposal không đổi** giữa `e625ee2 → 3337e60` (chỉ có PR #128 result-appeals xen vào).

**Khi thực thi:** tạo nhánh mới từ `develop` FE, ghi tài liệu vào `docs/handoff/BE_HANDOFF_REGISTRATION_RULES_2026-10-13.md` (và tuỳ chọn copy sang `ai-pms-backend/docs/`), commit (không tự merge).

---

## Hiện trạng đã xác minh (grounding — ghi cho BE khỏi phải tự dò)

**BE `develop @ 3337e60`:**
- `SupervisorRequestWorkflow.cs` (~dòng 144–147): gửi request PRIMARY chỉ hợp lệ khi `project.Status == "APPROVED"` (hoặc accept khi `SUPERVISOR_PENDING`); MENTOR yêu cầu `Status == "ACTIVE"`. ⇒ Toàn bộ luồng mời supervisor/mentor **chỉ mở sau duyệt** — trái D18.
- `WorkflowContextReader.Projects.cs` (dòng 57): `submissionIssues = team.EligibilityIssues.Concat(proposalIssues)`. Dòng 76: `submit_project` chỉ cần `Status==DRAFT` + issues. ⇒ **Submit KHÔNG bị chặn** bởi việc có supervisor/mentor hay chưa, cũng chưa có duplicate/proposal-content check.
- `TopicRepository.cs` chỉ filter `Title Contains` cho tìm kiếm; **không có** cơ chế similarity/duplicate. `TopicRules.cs` (PublicationIssues) không kiểm trùng.
- `CreateProjectDraftCommand.cs` / `ProjectValidators.cs`: chỉ validate độ dài/bắt buộc cơ bản, **không** kiểm độ đầy đủ nội dung proposal.
- Hook points đều còn tồn tại ở develop: `TopicsController`, `TopicWorkflow`, `SubmitProjectCommand`, `ResubmitProjectCommand`, `ProjectsController`.

**Live DB `AI_PMS` (đã đọc read-only, dataset nhỏ: 4 projects / 3 supervisor_requests / 2 topics / 2 assignments — migration an toàn, không rủi ro backfill lớn):**
- `supervisor_requests`: có sẵn `assignment_type (PRIMARY/DISCIPLINE_MENTOR)`, `major_id`, `requested_by`, `requested_at`, `responded_at`. **THIẾU `expires_at`.** CHECK `ck_supervisor_requests_status` chỉ cho `PENDING/ACCEPTED/REJECTED/CANCELLED` — **chưa có `EXPIRED`, `RELEASED`.**
- `projects`: có `title`, `topic_id (nullable)`, `proposal_source (NOT NULL)`. CHECK `ck_projects_status` đã đủ full lifecycle (DRAFT…ARCHIVED) ⇒ **D18 không cần ProjectState mới.**
- `project_topics`: có `title`, `project_period_id`, `lead_department_id`, `status`, `keywords_json`, `domain`, `project_mode` ⇒ đủ field để làm duplicate/similarity, **không cần bảng mới.**
- `project_registration_snapshots.snapshot_json`: nguồn eligibility/scope lúc submit.
- **Không tồn tại** bảng/cột similarity nào.

---

## Task 1 — D18: Supervisor/Mentor ACCEPT trước khi Submit (ưu tiên cao nhất)

### Nghiệp vụ & luồng mục tiêu
`Topic/Proposal → Academic scope → Team eligibility → Project DRAFT → **mời & được ACCEPT Supervisor (+ Mentor từng ngành nếu liên ngành)** → Submit → Department review → Approve → ACTIVE`. Mời supervisor/mentor diễn ra **khi project còn DRAFT**, không phải sau duyệt.

### Code cần sửa (BE)
1. `SupervisorRequestWorkflow.RequireEligibilityAsync`: cho phép gửi request PRIMARY và DISCIPLINE_MENTOR khi `project.Status == "DRAFT"` (không chỉ APPROVED/ACTIVE). **Giữ capacity check atomic tại thời điểm Accept** (dòng ~166–169), không đổi.
2. Mở contract cho **student-initiated mentor request**: student leader được `POST /projects/{id}/supervisor-requests` với `assignmentType=DISCIPLINE_MENTOR` + `majorId` khi DRAFT (giống luồng PRIMARY hiện có).
3. `WorkflowContextReader.Projects.cs` (dòng ~57): thêm vào `submissionIssues`:
   - `SUPERVISOR_NOT_CONFIRMED` khi chưa có Primary Supervisor status ACCEPTED.
   - `MENTOR_NOT_CONFIRMED:<majorId>` cho mỗi ngành tham gia (nếu INTERDISCIPLINARY và policy yêu cầu mentor) chưa có Mentor ACCEPTED.
   - Tự hiện ở `submit_project`/`resubmit_project` action. **FE không cần đổi gì** — `CapabilityReasonCode` là union string mở, FE auto-hiển thị reason mới.
4. Cho lecturer có `supervisor_requests` PENDING/ACCEPTED đọc **scoped draft** của team đó (title/description/objectives/roster) để quyết định nhận hướng dẫn — không cấp đọc project khác.
5. **Parallel request + first-accept-wins** cho cùng 1 slot (PRIMARY hoặc MENTOR-1-ngành): request accept đầu tiên thắng (transaction lock giống D17), các PENDING còn lại cùng slot tự `CANCELLED` + notify.
6. **Timeout:** request PENDING quá hạn tự `EXPIRED`.
7. **Re-validate mentor coverage khi đổi scope:** nếu thêm ngành sau khi đã đủ mentor, tái tính `MENTOR_NOT_CONFIRMED:<majorId>` cho ngành mới trước khi cho submit/resubmit.
8. **Giải phóng khi reject chung cuộc:** `reject_project` (không phải revision) → mọi `supervisor_requests` ACCEPTED của team → `RELEASED` + trả capacity lecturer + notify.

### DB cần cập nhật
```sql
ALTER TABLE supervisor_requests ADD expires_at datetime2 NULL;  -- mặc định requested_at + 7 ngày (set ở app layer)
-- mở rộng CHECK status thêm 2 giá trị:
ALTER TABLE supervisor_requests DROP CONSTRAINT ck_supervisor_requests_status;
ALTER TABLE supervisor_requests ADD CONSTRAINT ck_supervisor_requests_status
  CHECK ([status] IN (N'PENDING',N'ACCEPTED',N'REJECTED',N'CANCELLED',N'EXPIRED',N'RELEASED'));
```
Không cần bảng mới (slot mentor-per-major đã có sẵn qua `assignment_type`+`major_id`).

### Contract / reason code (FE đã sẵn sàng tiêu thụ)
| Trường hợp | Kết quả |
|---|---|
| Gửi request khi project không DRAFT (và không phải accept cũ) | 409 |
| Capacity đầy tại thời điểm Accept | 409 `SUPERVISOR_CAPACITY_FULL` |
| Submit thiếu Supervisor | `submit_project.allowed=false` + `SUPERVISOR_NOT_CONFIRMED` (422 nếu cố POST thẳng) |
| Submit liên ngành thiếu Mentor | `MENTOR_NOT_CONFIRMED:<majorId>` đúng ngành thiếu |
| Lecturer đọc draft team chưa mời mình | 403 |

### Test bắt buộc (7)
1. Gửi PRIMARY khi DRAFT → 200/201; accept → project vẫn DRAFT, Supervisor active.
2. Gửi MENTOR 2 candidate song song cho cùng 1 ngành → đầu tiên accept thắng, còn lại CANCELLED.
3. Submit chưa có Supervisor ACCEPTED → chặn với `SUPERVISOR_NOT_CONFIRMED`.
4. Submit liên ngành thiếu mentor 1/2 ngành → `MENTOR_NOT_CONFIRMED:<majorId>` đúng ngành.
5. Thêm 1 ngành sau khi đủ 2 mentor cũ → submit chặn lại vì mentor ngành mới.
6. `reject_project` chung cuộc → ACCEPTED → RELEASED, capacity tăng lại.
7. PENDING quá 7 ngày (giả lập clock) → EXPIRED.

### Tham số nhóm đã chốt (lưu trong policy versioned, **không hard-code**)
Capacity theo workload policy lecturer; deadline commit 14 ngày trước khi đóng window đăng ký; timeout PENDING 7 ngày; Department chỉ giới thiệu, không ép gán; lecturer tự Accept.

---

## Task 2 — Chống trùng đề tài với 3 kỳ gần nhất (độc lập, làm song song được)

### Nghiệp vụ
Khi Department publish Topic HOẶC student tạo/submit Proposal: kiểm trùng với đề tài `APPROVED/ACTIVE/COMPLETED` trong **3 `project_period` gần nhất cùng `lead_department_id`**:
- **Trùng tên chính xác** (normalize: bỏ dấu, lowercase, trim) → **chặn 422**.
- **Gần trùng** (overlap keyword/domain) → **chỉ cảnh báo** + danh sách tương tự + điểm số, người dùng tự quyết (không tự chặn).

### Code cần thêm
1. Endpoint mới `GET /api/v1/topics/similarity-check?title=&keywords=&leadDepartmentId=&periodWindow=3` → `{ exactMatch: TopicSummary|null, similar: [{topic, score}] }`. Query `project_topics` (và `projects` cho proposal tự do) theo `lead_department_id` + status + 3 period gần nhất; exact trên `normalize(title)`, near trên Jaccard keyword/domain (ngưỡng đề xuất ≥ 0.5, **Department xác nhận**).
2. Gọi trong `TopicWorkflow` publish: `exactMatch != null` → 422 `DUPLICATE_TOPIC_TITLE`; chỉ `similar` → publish OK + ghi audit cảnh báo.
3. Gọi tại `CreateProjectDraftCommand`/`submit_project` khi `proposal_source==STUDENT_PROPOSAL`: exact chặn 422, near chỉ cảnh báo.

### DB
Không cần bảng mới. Nếu chậm trên dữ liệu lớn: cân nhắc computed/persisted `normalized_title` + index trên `project_topics` (không bắt buộc MVP — dữ liệu hiện 2 dòng).

### Test (5): trùng exact cùng dept trong 3 kỳ → 422; near → OK có `similar[]`; khác lead dept → không chặn; kỳ ≥4 trở về trước → không chặn; proposal trùng exact → chặn 422.

---

## Task 3 — Pre-check nội dung Student Proposal (gộp chung PR với Task 1)

### Nghiệp vụ
`STUDENT_PROPOSAL` đang free-text, chỉ bị soi muộn lúc Department review (lãng phí nếu bị reject vì thiếu nội dung cơ bản). Áp cùng chuẩn tối thiểu như Topic: đủ problem statement, objectives, expected output, domain, technologies, keywords.

### Code cần thêm
1. Tái dùng `TopicRules.PublicationIssues` (hoặc tách `ContentCompletenessRules.Check(...)`) áp cho `Project` khi `proposal_source==STUDENT_PROPOSAL`.
2. Thêm vào `submissionIssues` (cùng chỗ Task 1 mục 3): mỗi field thiếu → `PROPOSAL_CONTENT_INCOMPLETE:<fieldName>`.
3. (Tuỳ chọn) `POST /projects/{id}/proposal/validate` trả field thiếu mà không submit — không bắt buộc MVP (dùng chung `GET /projects/{id}/actions` là đủ).

### Test (3): thiếu `problemStatement` → chặn với `PROPOSAL_CONTENT_INCOMPLETE:problemStatement`; đủ 6 field → không có issue này; `PUBLISHED_TOPIC` → không áp (đã kiểm ở bước publish).

---

## Task 4 — (Tuỳ chọn, chưa cần làm ngay) Review feedback có cấu trúc giàu
`checkpoint.decisions` hiện (MET/AT_RISK/NOT_MET/CANCELLED + reason + evidence) đơn giản hơn "khuôn góp ý" gốc (per-report: Điểm tốt/Cần cải thiện/Vấn đề nghiêm trọng + quick-tag + traffic-light + auto action item). FE đã có bản dùng đúng contract hiện tại. Nếu sau muốn bản giàu: cần bảng `review_feedback_forms`/`review_feedback_items`. Ghi lại để không quên, **không cần làm cho MVP**.

---

## Nhắc (không phải code — cần xử lý trước khi vận hành thật)
- **Ký duyệt học vụ:** D18 (Phương án B), rule chống trùng đề tài, chuẩn nội dung proposal hiện mới là **TEAM_SELECTED**, chưa có **ACADEMIC_APPROVED**. Cần academic owner ký trước khi enforce như policy chính thức (hoặc để sau feature-flag/configurable, không hard-code).
- **D5 — ngưỡng pass theo ngành (Defense):** chưa có ngưỡng chính thức để tự kết luận PASS/FAIL; cần ký trước khi dùng Defense ra quyết định tự động.
- **Bảo mật (phát hiện khi audit DB phiên này):** mật khẩu `sa` đang để **plaintext** trong `ai-pms-backend/src/AIPMS.Api/appsettings.json`. Cần xoay vòng mật khẩu + chuyển user-secrets/biến môi trường + xoá khỏi lịch sử commit nếu khả thi.

---

## Thứ tự khuyến nghị & dependency
- **Task 1 + Task 3 chung 1 PR** — vì Task 1 mục 3 và Task 3 mục 2 **cùng sửa `submissionIssues` trong `WorkflowContextReader.Projects.cs`**, tách ra sẽ conflict.
- **Task 2 độc lập** — làm song song.
- Trình tự mỗi task: Approved academic contract → DB change → Domain rule → Application service → Repository → API contract → Integration test → FE bật cờ/nối → E2E acceptance.
- **FE sẵn sàng:** supervisor-request API + readiness UI của FE đã contract-first; chỉ chờ BE mở DRAFT + 2 reason code là nối được ngay, không cần FE đổi contract.

## Verification (sau khi BE làm xong)
1. BE: unit + integration test cho 7+5+3 case ở trên; chạy `dotnet test`.
2. DB: readback `supervisor_requests` có `expires_at` + CHECK có `EXPIRED/RELEASED`; không có bảng dư thừa mới.
3. E2E: chạy kịch bản SINGLE_MAJOR + INTERDISCIPLINARY đầy đủ — mời→accept supervisor/mentor khi DRAFT→submit→review; + kịch bản trùng đề tài; + proposal thiếu field.
4. FE: bật cờ liên quan, kiểm readiness checklist hiện đúng PASS/PENDING/BLOCKED + reason code từ `GET /projects/{id}/actions`.
