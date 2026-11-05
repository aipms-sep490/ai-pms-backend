# MA TRẬN PHỦ 116 ID — AI-PMS · 30/10/2026

**Nguồn đề bài:** `AI-PMS_FULL_Requirements_Workflows_UIUX_Claude_Plan_v3.0_2026-10-30.md` — 34 REQ · 36 FL · 32 WF · 14 T
**Nguồn hiện trạng:** BE `14eecc7a` · FE `e8e9801c`
**Đính kèm cho:** BE dev · FE dev

---

## Cách đọc

**Phân loại:**

| | Nghĩa |
|---|---|
| `REUSE` | Đã có, dùng được. **Không viết lại.** |
| `EXTEND` | Đã có nền, cần mở rộng |
| `FIX` | Có cả hai phía nhưng **không nối với nhau** — lỗi tích hợp, không phải tính năng thiếu |
| `NEW` | Chưa có gì |
| `DEFER` | Hoãn sang đợt sau, có lý do |
| `BLOCKED` | Chờ bộ môn / PO ký — **không code, kể cả làm tạm** |

**Cột "Ai làm"** trỏ sang ticket trong `BE_DEV_TASKS.md` / `FE_DEV_TASKS.md`.

> **Con số đáng chú ý nhất của ma trận này:** phần lớn các ID **không phải việc mới**. 32 wireframe ⇒ **24 dùng route/modal đã có**. 14 mẫu biểu ⇒ **4 import thật**, không phải 14 endpoint mới. Đọc kỹ trước khi dựng gì mới.

---

## REQ-01 … REQ-34 — yêu cầu

| ID | Tên | Phân loại | Ai làm | Bằng chứng / ghi chú |
|---|---|---|---|---|
| REQ-01 | Lifecycle đồ án | REUSE+EXTEND | — | `ProjectState` + `my-capabilities` + `next-actions` đã có; thiếu "hôm nay làm gì" cho Reviewer/Committee |
| REQ-02 | Review 1..N | REUSE | `BE-01` | Engine N-mốc đủ. **SRS không có khái niệm Review 1–4** |
| REQ-03 | Readiness | EXTEND | `F5` | `BlockingReasons[]` + `VerificationStatus` đã có; cần tách 5 lớp |
| REQ-04 | Review Session | EXTEND | **BLOCKED** | Meeting + LiveKit có; **không có entity `ReviewSession`** nối Project+Gate+Meeting |
| REQ-05 | Câu hỏi / Correction / Notes | NEW | **BLOCKED** | grep `GateQuestion\|CorrectionState\|MandatoryCorrection` ⇒ **0** |
| REQ-06 | PASS / PENDING / FAIL | **BLOCKED** | `Q7` `Q9–Q12` | Chỉ có `PASSED\|REVISION_REQUIRED`; **19 call site**. `BE-02` là lưới an toàn cho việc này |
| REQ-07 | Deadline / Extension | EXTEND | **BLOCKED** | `WindowStart/End` + `DueAt` có; không có `OVERDUE`/`EXTENDED` |
| REQ-08 | FAIL / Re-review | EXTEND | **BLOCKED** | `ReopenAssignmentsForRevision` có; không giới hạn số lượt |
| REQ-09 | Liên kết Sprint–Review | EXTEND | — | **`TaskDisciplines(TaskId, MajorId, Role)` đã tồn tại** ⇒ làm `TaskGateRequirements` theo đúng pattern đó |
| REQ-10 | Các mô hình tiến độ | EXTEND | `F5` | `AcademicProgress`+`Source` + `TaskProgress` + `DisciplineProgress` có; **null-vs-0 đã đúng** |
| REQ-11 | Evaluation / Results | REUSE+EXTEND | `BE-09` `F4` `F9` | Toàn bộ engine đã có. Thiếu **trải nghiệm người chấm**, không thiếu logic |
| REQ-12 | Supervisor / Mentor | REUSE | — | `supervisors/me/triage-queue` đã có |
| REQ-13 | Mẫu biểu / Import | EXTEND | `TPL` | `ClosedXML 0.105.0` + mẫu `preview → commit` đã có |
| REQ-14 | Giảm nhập tay | **FIX** | `F6` | **0 caller** `progress-reports/prefill`. BE đã xong từ lâu |
| REQ-15 | Thông báo bộ môn | NEW | **BLOCKED** `Q28` | grep `announcement` trong `src`+`db` ⇒ **0**. Hạ tầng gửi dùng lại 100% |
| REQ-16 | Viewer hợp nhất | EXTEND | DEFER | `DocumentPreviewModal` cho pdf/ảnh/text inline; Office tải về |
| REQ-17 | Quản trị hội đồng | NEW | **BLOCKED** | **Xung đột lợi ích đã có** (`ReviewGateService.cs:636-637`); thiếu entity hội đồng + quorum |
| REQ-18 | Từ điển trường dữ liệu | EXTEND | `TPL` | Mẫu viết đã có ở `docs/admin-*-import*.md` |
| REQ-19 | Hướng dẫn có ngữ cảnh | EXTEND | `F10` | 6 target, **0 target nghiệp vụ**; Skip===Completed; `sidebar-primary` chết |
| REQ-20 | Thông báo / audit | EXTEND | — | deep-link resolver + `AuditLogsController` + `IAuditTrail` đã có |
| REQ-21 | Báo cáo bộ môn / export | EXTEND | DEFER | `departments/review-gates/matrix` + dashboards đã có |
| REQ-22 | Tương tác / a11y / an toàn | EXTEND | DoD | 409 giữ nháp + `UnsavedChangesNotice` + `role="alert"` đã có |
| REQ-23 | Trợ giúp không-AI / thuật ngữ | DEFER | — | `AcademicKnowledge*` sau cờ |
| **REQ-24** | **Danh mục rubric** | **EXTEND** | 🔴 `BE-06` `F2` `F3` `F8` | 🔴 grep `descriptor` trong `Features/Evaluations` ⇒ **0** |
| **REQ-25** | **Chấm theo Stage × Scope** | **REUSE** | `F1` | ✅ `COMPONENT_WEIGHTS` đã đủ, **BE không cần sửa**. Chỉ FE thiếu ô chọn Stage |
| **REQ-26** | **Quản trị cấp bộ môn** | **EXTEND** | 🔴 `BE-07` `BE-08` | `RubricWorkflow` chỉ so `DepartmentId`; `DRAFT→PUBLISHED` trực tiếp, một người |
| REQ-27 | UI/UX toàn hệ | EXTEND | DEFER | 83 page · 137 route · primitive có ở `components/ui`. **Không tạo bộ thứ hai** |
| REQ-28 | Theme / design system | EXTEND+DEFER | DEFER | `index.css` + `design-system/ai-pms/MASTER.md` có. **Dark Mode: 0 token** ⇒ P3 |
| REQ-29 | Motion | REUSE | — | `src/motion/motion.css` + `useEntranceMotion.ts` + `EffectsShowcase.tsx` **đều tồn tại** |
| REQ-30 | Xem trước tài liệu / ảnh | EXTEND | DEFER | Office: **không có converter nào ở BE** ⇒ giữ tải về |
| REQ-31 | Không gian minh chứng | EXTEND | — | `ProjectEvidenceLedger.tsx` + `ReviewSubmissionModal` tồn tại |
| REQ-32 | Annotation / comment | **NEW** | DEFER | Chỉ có `task_comments`. **Không có annotation nào.** Domain mới |
| REQ-33 | Upload / đính kèm | **FIX** | `BE-05` `F7` | 🔴 **11 thuộc tính, 4 mức** 6/9/11/22 MiB; FE hardcode 20. Thiếu 3 parent type |
| REQ-34 | Audit UI + E2E | NEW | `F11` | **0 `BROWSER_VERIFIED`** trong toàn dự án |

---

## FL-01 … FL-36 — luồng nghiệp vụ

| ID | Tên | Phân loại | Ai làm | Ghi chú |
|---|---|---|---|---|
| FL-01 | Lần đầu vào hệ thống | EXTEND | `F10` | — |
| FL-02 | Cấu hình kỳ đồ án | EXTEND | `BE-01` | 🔴 lỗ hổng phân quyền template |
| FL-03 | Đăng ký / kích hoạt dự án | REUSE | — | Registration + supervisor acceptance đã có |
| FL-04 | Cấu hình lộ trình Review | EXTEND | `BE-01` | 🔴 `Major` phải đúng chuỗi `"INTERDISCIPLINARY"`, sai ⇒ **0 gate, không báo lỗi** |
| FL-05 | Leader lập Sprint | REUSE+EXTEND | — | Sprint CRUD/burndown có; thiếu liên kết task↔requirement |
| FL-06 | Member làm Task | REUSE | — | TaskBoard + `TaskDisciplines` có; `TaskDto` **không trả về** disciplines |
| FL-07 | Nộp báo cáo tiến độ | **FIX** | `F6` | — |
| FL-08 | Chuẩn bị và nộp Review | EXTEND | `F5` | 🔴 `disciplineProgress` suy biến khi gate trộn scope |
| FL-09 | Review trực tuyến | EXTEND | **BLOCKED** `Q-T4` | **TT 08/2021 đòi ghi hình + lưu trữ**, hệ thống chưa có |
| FL-10 | Review trực tiếp | EXTEND | **BLOCKED** | — |
| FL-11 | Hội đồng ghi vấn đề | NEW | **BLOCKED** | — |
| FL-12 | Quyết định PASS | EXTEND | **BLOCKED** | `ReviewGateService.cs:348` đòi nhất trí **xuyên scope** — cần `Q9–Q12` |
| FL-13 | Quyết định PENDING | NEW | **BLOCKED** | — |
| FL-14 | PENDING quá hạn | NEW | **BLOCKED** | **Không auto trừ điểm, không auto FAIL** |
| FL-15 | FAIL Review đầu | EXTEND | **BLOCKED** | — |
| FL-16 | FAIL Review sau | EXTEND | **BLOCKED** | — |
| FL-17 | Khóa biên bản | NEW | **BLOCKED** `Q35` | `MeetingNotesVersion` **đã có version + lock** ⇒ nền tốt, dùng lại |
| **FL-18** | **Cấu hình Rubric** | **EXTEND** | 🔴 `BE-06/07/08` `F2` `F3` `F8` | ⭐ đường găng |
| **FL-19** | **Chấm đơn ngành** | **EXTEND** | `BE-09` `F4` `F9` | ⭐ **cùng một màn** với FL-20, FL-21 |
| **FL-20** | **Chấm liên ngành** | **EXTEND** | `BE-09` `F4` `F9` | ⭐ khác FL-19 chỉ ở `Scope` của assignment |
| **FL-21** | **Vấn đáp cá nhân** | **EXTEND** | `BE-09` `F4` `F9` | ⭐ `Scope = INDIVIDUAL` + `StudentId` |
| **FL-22** | **Tổng hợp & công bố điểm** | **EXTEND** | `BE-03` | ⭐ **Dữ liệu đã đủ** — `ResultBreakdownDto` có `UnroundedTotal`, `IsDemo`, `Readiness`, `Components[].Stage`. Chỉ là việc hiển thị |
| FL-23 | Khiếu nại điểm | REUSE | — | `ResultAppealsController` **18 endpoint** đã có. **Không gộp với correction sau Review** |
| FL-24 | Import rubric Excel | EXTEND | `TPL` | Sau `BE-06`/`BE-07` |
| FL-25 | Export phiếu chấm | NEW nhỏ | — | 🔴 **EXPORT-ONLY**, không import điểm — xem T-04 |
| FL-26 | Đăng thông báo | NEW | **BLOCKED** `Q28` | — |
| FL-27 | Comment tài liệu | NEW | DEFER | — |
| FL-28 | Supervisor theo dõi | REUSE | — | `triage-queue` đã có |
| FL-29 | Thông báo deep-link | REUSE | — | FE PR #110 đã sửa theo recipient role |
| FL-30 | Mở lại hướng dẫn | EXTEND | `F10` | — |
| FL-31 | Đổi theme | DEFER | — | 0 token dark |
| FL-32 | Xem ảnh / tài liệu | EXTEND | DEFER | — |
| FL-33 | Reviewer xác minh evidence | EXTEND | `BE-02` #19 | **Xung đột lợi ích đã có** (`:636-637`). Bất biến: **nhận v1 không ngụ ý nhận v2** |
| FL-34 | Annotation | NEW | DEFER | — |
| FL-35 | Nộp evidence sửa đổi | EXTEND | **BLOCKED** | — |
| FL-36 | Upload / đính kèm | **FIX** | `BE-05` `F7` | ⭐ |

---

## WF-01 … WF-32 — wireframe

| ID | Tên | Route / component hiện có | Phân loại |
|---|---|---|---|
| WF-01 | My Work / Timeline | `/project/overview` · `/project/timeline` · `/supervisor/dashboard` · `/department/workspace` | EXTEND |
| WF-02 | Review Template Builder | `/academic/review-gates` | EXTEND |
| WF-03 | Gate Detail + Readiness | `/project/timeline` | EXTEND ⭐ |
| WF-04 | Review trực tuyến | `/project/meetings/:id/video` | EXTEND · **BLOCKED** |
| WF-05 | Review trực tiếp | `/project/meetings/:id` | EXTEND · **BLOCKED** |
| WF-06 | Quyết định hội đồng | `/review/gate-assignments` | **BLOCKED** |
| WF-07 | Correction Center | tab trong `/project/timeline` + `/review/projects/:id/corrections` | NEW · **BLOCKED** |
| WF-08 | Sprint–Review Planner | `/project/tasks` · `/project/gantt` | EXTEND |
| WF-09 | Task Board | `/project/tasks` | REUSE |
| WF-10 | Progress Dashboard | `/project/overview` · `/department/portfolio` | EXTEND |
| WF-11 | Evaluator Scoring | `/evaluator/assignments/:id` | EXTEND ⭐ |
| WF-12 | Template Library / Import | tab trong `/academic/governance` | EXTEND |
| WF-13 | Supervisor Action Center | `/supervisor/workspace` · `/mentor/workspace` | REUSE + gộp |
| WF-14 | Announcement Editor | ❌ **route mới** | NEW · **BLOCKED** |
| WF-15 | Announcement Reader | ❌ **route mới** | NEW · **BLOCKED** |
| WF-16 | Viewer hợp nhất | modal dùng ở 4 nơi | EXTEND |
| WF-17 | Minutes Preview | ❌ **route mới** | NEW · **BLOCKED** |
| WF-18 | Field Dictionary | **gộp vào WF-12** | MERGE |
| WF-19 | Tour / Walkthrough | shell | EXTEND |
| WF-20 | Help Center | ❌ **route mới** | DEFER |
| WF-21 | Portfolio / Export | `/department/portfolio` · `/department/projects/progress-matrix` | EXTEND |
| WF-22 | Policy Version History | tab trong `/academic/project-periods/:id/policy` | EXTEND |
| **WF-23** | **Rubric Builder** | `/academic/rubrics` | **EXTEND ⭐** |
| **WF-24** | **Scoring / Defense Workbench** | `/evaluator/assignments/:id` ≡ WF-11 | **EXTEND ⭐** |
| **WF-25** | **Scoring Breakdown** | `/project/result` · `/department/projects/:id/result` | **EXTEND ⭐** |
| WF-26 | Rubric Excel Import | **gộp vào WF-12** | MERGE |
| WF-27 | Appearance Settings | ❌ **route mới** | DEFER — 0 token dark |
| WF-28 | Doc / Media Review Workspace | mở rộng `DocumentPreviewModal` | EXTEND · DEFER |
| WF-29 | Evidence Detail / Version Compare | `/project/evidence` → `ProjectEvidenceLedger` | EXTEND |
| WF-30 | PDF / Image Annotation | trong WF-28 | NEW · DEFER |
| WF-31 | Unified Upload Manager | mọi chỗ upload | **FIX ⭐** |
| WF-32 | Motion / Interaction Spec | `src/ui-review/EffectsShowcase.tsx` **đã có** | REUSE+EXTEND |

> **Tổng: 32 WF ⇒ 24 dùng route/modal đã có · 3 gộp · 5 route mới — và cả 5 đều phụ thuộc feature chưa tồn tại ở BE.**
>
> 🔴 **Đừng dựng vỏ UI cho 5 route mới đó.** Không có API thì chúng là trang chết, và khi API ra hình dáng thật sẽ phải làm lại.

---

## T-01 … T-14 — mẫu biểu

| ID | Mẫu | Phán quyết | Lý do |
|---|---|---|---|
| T-01 | Review Workflow Config | **IMPORT bản nháp** | `SaveReviewGateTemplateRequest` nhận cả cây trong 1 payload |
| T-02 | Review Criteria Checklist | **IMPORT bản nháp** | `ReviewGateRequirementInput` 9 trường phẳng |
| T-03 | Rubric Definition | **IMPORT bản nháp** — chờ `BE-06`/`BE-07` | **Không publish từ file** |
| T-04 | Evaluator Scoring Sheet | 🔴 **EXPORT-ONLY** | **7 vector rủi ro:** mạo danh evaluator · sai đối tượng chấm · lệch phiên bản rubric · sửa trọng số qua file · formula injection · bỏ qua bước chốt · mất kiểm soát xung đột 409. **Không làm import điểm, kể cả khi được yêu cầu** |
| T-05 | Question / Feedback Log | **FORM** + export | Cần liên kết entity; Excel không giữ được an toàn |
| T-06 | Correction Register | **FORM** + export | Quyết định học thuật, cần authority + audit từng dòng |
| T-07 | Sprint–Review Plan | **IMPORT bản nháp** | Sprint CRUD đã có |
| T-08 | Task Assignment | IMPORT tuỳ chọn | Ưu tiên thấp |
| T-09 | Progress Report | **FORM + PREFILL** | `F6` |
| T-10 | Supervisor Assessment | **FORM** | `WeeklyAssessments` đã có |
| T-11 | Announcement | **FORM** rich text | **BLOCKED** |
| T-12 | Review Minutes | **FORM + EXPORT PDF** | Cần sign-off trong hệ thống |
| T-13 | Dept Status Export | **EXPORT-ONLY** | `departments/review-gates/matrix` đã có dữ liệu |
| T-14 | Student Status Export | **EXPORT-ONLY** | Snapshot + ẩn thông tin cá nhân |

> **Tổng: 4 IMPORT thật** (T-01 · T-02 · T-03 · T-07) **+ 1 tuỳ chọn + 5 FORM + 4 EXPORT-ONLY.**
>
> **Không phải 14 endpoint import mới.** Đọc kỹ T-04 trước khi làm bất cứ gì liên quan nhập điểm từ file.

---

## Tổng kết theo phân loại

Đếm theo **nhãn chính** (nhãn đầu tiên của mỗi dòng), nên mỗi cột cộng đúng bằng tổng số ID:

| Nhãn chính | REQ | FL | WF |
|---|---:|---:|---:|
| `REUSE` (có, dùng được) | 6 | 6 | 3 |
| `EXTEND` (có nền) | 19 | 19 | 18 |
| `FIX` (lỗi tích hợp) | 2 | 2 | 1 |
| `NEW` | 5 | 8 | 5 |
| `MERGE` (gộp) | — | — | 2 |
| `DEFER` | 1 | 1 | 2 |
| `BLOCKED` | 1 | — | 1 |
| **Tổng** | **34** | **36** | **32** |

Nhiều dòng còn mang **nhãn phụ**, nên con số dưới đây chồng lên bảng trên — đọc riêng:

| Nhãn phụ | REQ | FL | WF |
|---|---:|---:|---:|
| 🔴 `BLOCKED` (tính cả nhãn chính) | **7** | **11** | **7** |
| `DEFER` (tính cả nhãn chính) | 7 | 4 | 5 |

T-01…T-14 không dùng hệ nhãn này — xem phán quyết riêng từng mẫu ở bảng trên.

**Điều quan trọng nhất đọc được từ bảng này:** phần lớn công việc đợt này là **mở rộng và nối lại những thứ đã có**, không phải xây mới. Ba ID duy nhất cần schema mới là `REQ-24` · `REQ-26` (⇒ `BE-06` `BE-07` `BE-08`), và `REQ-32` (hoãn).

Trước khi dựng bất cứ gì mới, tìm trong cột "bằng chứng / ghi chú" xem nó đã tồn tại chưa.
