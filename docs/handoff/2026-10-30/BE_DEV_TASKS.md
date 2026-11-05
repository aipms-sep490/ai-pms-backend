# NHIỆM VỤ BACKEND — AI-PMS · giao ngày 30/10/2026

> **Đọc hết mục 0 trước khi sửa bất cứ dòng nào.** File này được viết để một AI coding agent thực thi được mà không cần hỏi lại. Mỗi task có: bằng chứng (file:dòng + code thật), nghiệp vụ, cách xử lý đề xuất, tiêu chí nghiệm thu, cách kiểm chứng, file dự kiến.
>
> **Mục tiêu đợt này: hệ thống chạy thật được một kỳ.** Không làm bản demo dùng một lần. Hệ quả: không có workaround nào được chấp nhận ở phần rubric và chấm điểm — sai là sai **điểm thật của sinh viên**.
>
> **Ba ticket `BE-06` `BE-07` `BE-08` dán nhãn 🔴 CHỜ VĂN BẢN.** Code được, mở PR được, **không merge** khi bộ môn chưa ký. Xem mục 0.9.

---

## 0. NGỮ CẢNH BẮT BUỘC ĐỌC

### 0.1 Dự án là gì

AI-PMS là hệ thống **quản trị đồ án học thuật** (academic project governance) cho một trường đại học, không phải task manager. Hai phương thức đồ án dùng **chung một lifecycle**:

- `SINGLE_MAJOR` — đồ án một ngành, một GVHD chính (PRIMARY supervisor).
- `INTERDISCIPLINARY` — đồ án liên ngành, một GVHD chính **cộng thêm** một Mentor cho **mỗi ngành** tham gia (`DISCIPLINE_MENTOR`).

Vai trò trong DB: `ADMIN` · `DEPARTMENT_STAFF` · `LECTURER` · `STUDENT`. Không có role nào khác — xem `N13`.

Thứ tự thẩm quyền — **không được đảo**:

```
Quy định trường đã phê duyệt
  > Policy của ProjectPeriod đang hiệu lực (+ snapshot đã đóng băng)
  > Phần v5.0 team đã chọn
  > SRS (Report 3) / SDD (Report 4)
  > Đề xuất lịch sử
  > Mockup
```

**BE là authority cuối cùng** về quyền, phạm vi, trạng thái, điểm, lưu trữ. FE fail-closed — chỉ hiển thị cái BE cho phép. Mọi lỗi về quyền/trạng thái/điểm **phải sửa ở BE**, không được để FE tự nới.

### 0.2 Repo và baseline

| | |
|---|---|
| Repo | `https://github.com/aipms-sep490/ai-pms-backend` |
| Nhánh gốc | `develop` |
| Baseline SHA | `14eecc7a04a1b91bb4c491c5ca85b09edbb99f94` (CI xanh) |
| Staging | `https://api-staging.khaidz.com` |
| PR đang mở | **không có** |

Kiến trúc: Clean Architecture, 4 project. Số file tại baseline: `AIPMS.Api` 117 · `AIPMS.Application` 546 · `AIPMS.Domain` 18 · `AIPMS.Infrastructure` 325.

### 0.3 Quy tắc làm việc

1. **Một task = một nhánh mới từ `develop` đã fetch = một PR.** Không code trực tiếp trên `develop`.
2. Trước khi sửa: `git fetch`, kiểm `git status`, kiểm `git log origin/develop..develop --oneline` — repo này từng có commit local chưa push.
3. Task sau không dùng tiếp nhánh của task trước. Có phụ thuộc thì ghi `depends on #<PR>` trong mô tả PR.
4. Khi bắt đầu task: báo tên nhánh + baseline SHA. Khi bàn giao: ghi rõ đã chạy validation gì, trạng thái commit/push/PR.
5. **Database First.** Không dùng EF migrations. Không sửa file generated (`src/AIPMS.Infrastructure/Persistence/Generated/*`) bằng tay. Thay đổi schema đi qua `db/changes/<yyyyMMdd>_<tên>.sql` + cập nhật `db/schema.sql` + thêm dòng vào `db/e2e/migrations.json`. Xem mục 0.8.
6. **Không tự chốt policy đang ở trạng thái `PROPOSED`/`OPEN`.** Đợt này có **8 quyết định chưa ký** — xem `CAU_HOI_BO_MON_2026-10-30.md`. Gặp câu hỏi chính sách mới thì dừng, viết câu hỏi, chờ trả lời.
7. Mọi thay đổi hành vi phải có test. CI chạy `-warnaserror`, cảnh báo = lỗi.
8. **Mọi exception phải có `errorCode`.** Repo đã bắt đầu làm ở commit `132b9bc`; đợt này làm nốt cho vùng mình chạm. FE dựng thông báo từ `errorCode`, không parse câu tiếng Anh.

### 0.4 Lệnh kiểm chứng

```bash
dotnet restore AIPMS.sln
dotnet build AIPMS.sln --configuration Release --no-restore -warnaserror
dotnet test AIPMS.sln --configuration Release --no-build
```

Chạy một nhóm test: `dotnet test --filter "FullyQualifiedName~RubricRules"`.

**Nghiệm thu phải là output thật.** `dotnet test --filter` phải trả `Passed: N, Failed: 0` với `N > 0`. Bảng tự khai không tính — xem `X9`.

### 0.5 🔁 MƯỜI BỐN BẤT BIẾN NGHIỆP VỤ — khối bắt buộc, giống hệt bản của FE

> ⚠️ Khối này là **bản sao bắt buộc giống nhau từng chữ** với mục 0.6 của `FE_DEV_TASKS.md`. Sửa bên này phải sửa bên kia.

| # | Quy tắc | Nguồn / bằng chứng |
|---|---|---|
| **N1** | **Hai bộ máy độc lập.** Review 1–2–3 chạy bộ máy **Review Gate** (ra *đạt / phải sửa* + nhận xét). Review 4 chạy bộ máy **Evaluation** (ra **điểm**). Chúng không nói chuyện với nhau | `ReviewGatesController` vs `PolicyEvaluationController`; không có code nào nối hai bên |
| **N2** | **Trọng số tiến độ ≠ trọng số điểm.** `ProjectGateInstance.Weight` → phần trăm tiến độ học thuật. `SchemeComponent.StudentWeightPercent` → phần trăm điểm. **Không bao giờ cộng hay nhân vào nhau** | `ReviewGateService.cs:589` vs `VersionedResultCalculator.cs:64` |
| **N3** | **`Stage` và `Scope` là hai trục độc lập.** Stage ∈ `ON_GOING\|COLD\|DEFENSE`. Scope ∈ `COMMON\|MAJOR_SPECIFIC\|INDIVIDUAL`. Một component = một ô (Stage × Scope × Major) | `SchemeComponentInput(Scope, MajorId, Stage, …)` |
| **N4** | **Không nhân chồng hai hệ trọng số.** 30/70 theo chặng và 50/30/20 theo phạm vi phải được **giải vào từng ô**, tổng 100 **đúng một lần** | v5 dòng 288 *"Never double-apply"*; ma trận ở `RUBRIC_DESIGN_2026-10-30.md` §6.1 |
| **N5** | **Chỉ chấm tiêu chí lá, ở effective weight.** Tiêu chí cha **không có** `MaxScore` và **không** `IsRequired`. Không thể chấm trùng cha + con | `RubricRules.cs:25`; `EvaluationDraftRepository.cs` truyền `EffectiveWeightPercent` |
| **N6** | **`null` ≠ `0`.** Ô trống = chưa chấm. Nhập `0` = cho 0 điểm. Thiếu bất kỳ phiếu nào ⇒ kết quả `null` (CHỜ), **không chia lại trọng số** cho người đã chấm | `EvaluationScoring.cs:31-33`; `VersionedResultCalculator.cs:54` |
| **N7** | **Demo ≠ chính thức, và BE chặn cứng.** `IsDemo=true` ⇒ `ApprovalStatus` phải là `"DEMO"`. `IsDemo=false` ⇒ phải có `"APPROVED"` **và** `ApprovalReference` không rỗng | `VersionedResultCalculator.cs:17` |
| **N8** | **Rubric đã publish và đã được tham chiếu là bất biến.** Sửa = tạo version mới. Điểm lịch sử không bị tính lại | `RubricRules.cs:10`; `RubricWorkflow.NewVersion` |
| **N9** | **GVHD chính bị cấm chấm chính thức dự án mình hướng dẫn.** `EvaluationType="SUPERVISOR"` đã khai tử. **Mentor ngành KHÔNG bị cấm** | `EvaluationDraftWorkflow.cs:114-117`; grep `mentor` trong `Features/Evaluations` ⇒ 0 |
| **N10** | **Phiếu tuần là bằng chứng, không phải điểm.** `WeeklyAssessments` chỉ GVHD chính ghi, `AffectsOfficialResult = false` | `WeeklyAssessmentService.cs:54`; `WeeklyAssessmentDto` |
| **N11** | **`ContributionIndex` / số task / commit / story point KHÔNG được quy thành điểm.** Chỉ là bằng chứng để người chấm đọc | v5 dòng 199 |
| **N12** | **Số mốc Review là cấu hình, không phải hằng số.** `ReviewGateTemplate.Gates[]` nhận N mốc. "Review 1–4" là một template cụ thể, **không** mặc định bằng G1–G6 hay COLD/DEFENSE | `ReviewGateContracts.cs:11-20`; SRS **không có** khái niệm Review 1–4 |
| **N13** | **`Head of Department` là trách nhiệm học thuật, không phải role trong DB.** `db/seed.sql` chỉ có `ADMIN · DEPARTMENT_STAFF · LECTURER · STUDENT`. **Không thêm identity role mới** | `db/seed.sql`; `AuthorizationPolicies` có 6 policy, không có HoD |
| **N14** | **Annotation/comment ≠ Official Correction.** Personal note ≠ shared comment ≠ reviewer feedback ≠ official correction ≠ signed minutes — khác quyền, khác authority | v3.0 REQ-32; BE hiện **chỉ có** `task_comments` |

**Bốn thứ tuyệt đối không làm:** phát triển tính năng AI trong sản phẩm · điền `ApprovalReference` giả · sửa rubric đã publish · trả success khi chưa thực sự ghi.

### 0.6 Nơi đặt code — theo mẫu có sẵn trong repo

| Đặt gì | Ở đâu | Mẫu để theo |
|---|---|---|
| Controller mỏng, chỉ định tuyến + `[Authorize]` | `src/AIPMS.Api/Controllers/` | `RubricsController.cs` |
| DTO / request / response record | `src/AIPMS.Application/Features/<Domain>/DTOs/` | `RubricDtos.cs` · `EvaluationSchemeDtos.cs` |
| **Guard nghiệp vụ + authority** | `src/AIPMS.Application/Features/<Domain>/Services/*Workflow.cs` | `RubricWorkflow.cs` · `EvaluationDraftWorkflow.cs` |
| **Bất biến miền (validate thuần, không I/O)** | `…/Services/*Rules.cs` | `RubricRules.cs` ← mẫu tốt nhất trong repo |
| **Tính toán thuần, không I/O** | `…/Services/` static class | `EvaluationScoring.cs` · `RubricHierarchy.cs` · `VersionedResultCalculator.cs` |
| Query / Command qua MediatR | `…/Queries/` · `…/Commands/` | `GetEligibleEvaluatorsQuery.cs` |
| Repository + EF config | `src/AIPMS.Infrastructure/Persistence/Repositories/` · `…/Configuration/` | `RubricRepository.cs` · `AipmsDbContext.Rubrics.cs` |
| Service có I/O phức tạp | `src/AIPMS.Infrastructure/Services/Projects/` | `ReviewGateService.cs` · `WeeklyAssessmentService.cs` |

**Hai quy tắc kiến trúc của đợt này:**

1. **Không đổi ngữ nghĩa enum đang dùng.** Thêm bảng/object mới thay vì thêm value vào enum cũ. Lý do có thật: `Gate.Status` hiện gộp 3 khái niệm vì từng có người thêm value, nay muốn gỡ phải rà **19 call site**. Ngoại lệ duy nhất của đợt này là `BE-08` thêm `PENDING_APPROVAL` — và nó được phép vì giá trị mới **không đổi nghĩa** ba giá trị cũ.
2. **Tính toán thuần tách khỏi I/O.** Mọi logic điểm phải nằm trong static class testable được, như `EvaluationScoring`.

### 0.7 🔁 MƯỜI QUY TẮC CHỐNG NHẦM LẪN BE ↔ FE — khối bắt buộc, giống hệt bản của FE

> ⚠️ Khối này là **bản sao bắt buộc giống nhau từng chữ** với mục 0.8 của `FE_DEV_TASKS.md`. Sửa bên này phải sửa bên kia.
>
> Mỗi quy tắc giải một lỗi **đã xảy ra thật** trong dự án này. Không phải lý thuyết.

| # | Quy tắc | Lỗi thật nó ngăn |
|---|---|---|
| **X1** | **Một Feature ID = một BE task + một FE task + một QA task + một Contract Card.** Không bao giờ giao hai mô tả khác nhau cho cùng một API | — |
| **X2** | **Slice phải dọc.** "BE làm API, FE làm UI" là **hai nửa slice**, không phải một slice | `progress-reports/prefill` merged từ lâu mà FE **0 caller** |
| **X3** | **Endpoint mới không được merge nếu không có example JSON** trong `docs/contracts/` và không có tên test trong Contract Card | FE `rubrics-api.ts` **bỏ cả `parentId` và `effectiveWeightPercent`** mà BE đã gửi; không test nào phát hiện |
| **X4** | **FE phải vendor example JSON, không copy tay.** `sync-contracts.mjs` + `contract-lock.json` + job `contract-drift` đỏ khi lệch | BE có 37 file example máy đọc được; FE có 7 tài liệu văn xuôi — prose không fail CI |
| **X5** | **Mọi mã trạng thái phải có hàng trong Contract Card**, kèm `errorCode` và FE làm gì | FE phải làm riêng `execution-error-codes.ts` **sau khi** BE gán `errorCode` ở `132b9bc` — hai lần làm cho một việc |
| **X6** | **Mọi field có thể `null` phải có hàng trong Contract Card**, ghi rõ null nghĩa là gì và FE hiển thị gì | `totalScore = null` nghĩa "chưa đủ phiếu", không phải 0 |
| **X7** | **Giới hạn và policy không được hardcode ở FE.** BE phải expose, FE phải đọc | FE hardcode 20 MiB ở review gates, BE có **4 mức khác nhau**: 6/9/11/22 MiB |
| **X8** | **Không đổi hợp đồng sau `FROZEN`** mà không mở card mới và cập nhật example | — |
| **X9** | **Nghiệm thu phải kèm lệnh chạy lại được.** Bảng tự khai không tính | `docs/FINAL_BACKEND_ACCEPTANCE_REPORT.md` khai `ReviewGateServiceTests.cs (77 tests) TESTS PASS` — file đó **chưa từng tồn tại ở bất kỳ ref nào**, module đó có **0 test** |
| **X10** | **Endpoint không có caller phải được giải thích.** Chạy `check-orphan-endpoints.sh` mỗi sprint | `prefill` và `review-gates/scopes` đều có BE đủ quyền mà không có đường gọi thật |

### 0.8 Convention database — phải tuân theo

**SQL Server database-first.** Schema nguồn là **SQL script**, không phải EF migrations.

| Thứ | Ở đâu |
|---|---|
| Schema đầy đủ | `db/schema.sql` |
| Thay đổi tăng dần | `db/changes/<yyyyMMdd>_<mô_tả>.sql` |
| Manifest thứ tự chạy | `db/e2e/migrations.json` |
| Seed | `db/seed.sql` · `db/sample_data.sql` |
| **Mẫu tốt nhất để theo** | `db/changes/20260922_add_rubric_hierarchy.sql` · `20260911_add_rubric_versions.sql` |

**Khuôn file `db/changes/*.sql` — theo đúng mẫu `20260922_add_rubric_hierarchy.sql`:**

```sql
-- <một dòng nói rõ: additive, rerunnable, và cái gì KHÔNG bị đổi>
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF COL_LENGTH(N'dbo.<bảng>', N'<cột>') IS NULL
        ALTER TABLE dbo.<bảng> ADD <cột> <type> NULL;
    -- ... các bước khác, mỗi bước có IF EXISTS / IF NOT EXISTS
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
```

**Năm quy tắc migration:**

1. **Idempotent và chạy lại được** — `IF NOT EXISTS` / `IF COL_LENGTH(...) IS NULL`. Chạy hai lần không được lỗi.
2. **Cột mới phải `NULL`-able, không backfill tự động.** Hàng lịch sử giữ nguyên nghĩa cũ. Nhìn comment trong `20260911_add_rubric_versions.sql`: *"No reliable publication history exists for legacy rows. Protect all of them; never infer an editable draft from is_active = 0."* — đó là tinh thần.
3. **Không xóa/đổi cột đang dùng.** Thêm mới, chuyển dần.
4. **Lịch sử bất biến:** không `UPDATE` bảng kết quả/điểm đã finalize.
5. **Chạy được trên isolated fixture** (`IsolatedSqlDatabase.cs`), và **phải có script rollback** trong mô tả PR (không cần commit, nhưng phải viết ra).

**Bảng không được chạm:** `student_results` · `project_results` · `evaluation_finalizations` · `rubric_versions` đã `PUBLISHED`/referenced · `gate_decisions` · mọi snapshot JSON. Chỉ append, không update.

> ⚠️ **Lưu ý đã phát hiện:** `db/changes/20261024_add_design_department.sql` tồn tại trong repo nhưng **không có trong `db/e2e/migrations.json`**. Không sửa trong đợt này, nhưng đừng lấy nó làm mẫu, và khi thêm file mới thì **nhớ thêm dòng vào manifest**.

### 0.9 KHÔNG ĐƯỢC LÀM trong đợt này

#### Nhóm A — Làn 0, chờ bộ môn, không code dù "làm tạm"

| Việc | Chờ quyết định | Vì sao không code trước |
|---|---|---|
| Tách `Gate.Status` thành `CommitteeOutcome` (PASS/PENDING/FAIL, quorum, nhất trí xuyên ngành) | `Q7` · `Q9–Q12` · `D-NEW-12` | **19 call site** phải sửa. Sai hướng là sửa hai lần. `BE-02` của đợt này là lưới an toàn cho việc đó, làm trước |
| Domain `ReviewQuestion` + `ReviewCorrection` | `Q16–Q18` · `Q36` | Domain mới hoàn toàn. Khuôn đã có ở `ResultAppealsController` (18 endpoint) — nhưng **không gộp** `ResultCorrectionRequest` (khiếu nại điểm đã publish) với `ReviewCorrection` (sửa sau Review Gate): hai domain, hai authority, hai timeline |
| Thông báo bộ môn (`announcement`) | `Q28` | grep `announcement` trong `src` + `db` ⇒ **0**. Hạ tầng gửi thì dùng lại 100% (hub + 3 worker + queue) |
| Ghi hình bảo vệ trực tuyến | `Q-T4` | **Đây là tuân thủ luật, không phải tính năng.** Thông tư 08/2021/TT-BGDĐT Điều 9 khoản 1 đòi hội đồng ≥3, đồng thuận của cả hội đồng và sinh viên, **ghi hình ghi âm đầy đủ và lưu trữ**. Hiện BE không có gì. Cần pháp chế trước |

#### Nhóm B — Ba ticket 🔴 CHỜ VĂN BẢN: làm được, không merge được

`BE-06` · `BE-07` · `BE-08` được đặc tả đầy đủ trong file này. Được phép: tạo nhánh, viết script SQL, viết code, viết test, mở PR, nhờ review. **Không được phép: merge.**

> **Chốt an toàn ở backend, đừng tìm cách đi vòng:** `VersionedResultCalculator.cs:17` chặn cứng
> ```csharp
> || !policy.IsDemo && (policy.ApprovalStatus != "APPROVED" || string.IsNullOrWhiteSpace(policy.ApprovalReference))
> ```
> **Không ai được điền `ApprovalReference` giả để qua cửa này.** Khi bộ môn ra văn bản, việc cần làm chỉ là nhập số văn bản — **thao tác dữ liệu, không sửa code.** Ba ticket này được thiết kế để lúc đó không phải làm lại gì.

### 0.10 Phụ thuộc FE — biết trước để giao hợp đồng đúng lúc

| Ticket BE | Mở đường cho | FE chờ gì cụ thể |
|---|---|---|
| `BE-05` policy upload | `F7` | FE đang hardcode 20 MiB ở `review-gates-api.ts:95`. Cần endpoint trả giới hạn thật theo parent |
| `BE-06` + `BE-07` + `BE-08` rubric | `F8` Rubric Builder | FE không dựng được form 4 mức khi DTO chưa có trường |
| `BE-09` descriptor lên draft DTO | `F9` màn chấm | FE không hiện được mức chất lượng khi draft DTO chưa gửi |

**Sáu ticket FE còn lại (`F1`…`F6`) không chờ BE gì cả** — BE không cần làm gì cho chúng. Nếu FE báo "chờ BE" ở các ticket đó thì đọc lại `FE_DEV_TASKS.md` mục 0.10.

---

## 1. BẢN ĐỒ PHỤ THUỘC

```
PHASE 0 — LÀM NGAY, KHÔNG CHỜ AI
  BE-01  siết authority template Review Gate      🔴 SECURITY
  BE-02  20 test hành vi Review Gate hiện tại     ← chặn mọi việc Gate về sau
  BE-03  test scheme 11 component lọc theo ngành  🔴 CRITICAL — chống sai điểm thật
        │  (ba cái độc lập nhau, làm song song được)
        ▼
  ✅ CHECKPOINT 0
        │
PHASE 1 — HỢP ĐỒNG NHỎ
  BE-04  cold template: major_id nullable + scope
  BE-05  expose policy upload theo parent          → mở đường F7
        ▼
  ✅ CHECKPOINT 1
        │
PHASE 2 — ĐƯỜNG GĂNG RUBRIC  🔴 CHỜ VĂN BẢN (code được, không merge)
  BE-06  rubric_criterion_levels + 2 cột          ← chặn bởi Q-A10
  BE-07  7 cột scope/stage/major trên rubrics      ← chặn bởi Q-N02
  BE-08  PENDING_APPROVAL + rubric_approvals       ← chặn bởi Q-N02, cần BE-07
        ▼
  ✅ CHECKPOINT 2
        │
PHASE 3 — ĐƯA LÊN HỢP ĐỒNG CHẤM
  BE-09  descriptor + trọng số + stage lên draft DTO   ← cần BE-06, mở đường F9
```

**Thứ tự bắt buộc:** `BE-02` trước mọi việc sửa Review Gate về sau · `BE-07` trước `BE-08` · `BE-06` trước `BE-09`. Ngoài ra tự do.

---

# PHASE 0 — LÀM NGAY, KHÔNG CHỜ AI

## BE-01 · 🔴 SECURITY · Staff bộ môn nào cũng publish được lộ trình Review của ngành/kỳ không thuộc mình

### Bằng chứng

`src/AIPMS.Infrastructure/Services/Projects/ReviewGateService.cs:56` — `IsStaff` chỉ kiểm role, không kiểm phạm vi:

```csharp
private async Task<bool> IsStaff(CancellationToken ct) => await db.Users.AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE" &&
    u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN" || r.Role.Code == "DEPARTMENT_STAFF"), ct);
```

`:61` — `RequireStaff` chỉ gọi `IsStaff`:

```csharp
private async Task RequireStaff(CancellationToken ct)
{ if (!await IsStaff(ct)) throw new ForbiddenException("Academic staff access is required."); }
```

`:83` `SaveTemplate` → `:86` chỉ `await RequireStaff(token);`
`:136` `PublishTemplate` → `:138` chỉ `await RequireStaff(token);`

Request nhận `Mode` và `Major`:

```csharp
SaveReviewGateTemplateRequest(long? ProjectPeriodId, string Code, string Name, string Mode,
    string? Major, int Version, string? ConcurrencyToken, IReadOnlyList<ReviewGateInput> Gates)
```

⇒ **Một staff bộ môn Marketing publish được template `Major = "SE"` cho kỳ của bộ môn CNTT.** Không có chỗ nào so `Major`/`ProjectPeriodId` với phạm vi của actor.

`RequireStaff` được gọi ở **11 chỗ**: `:69` `:77` `:86` `:138` `:360` `:409` `:473` `:488` `:503`.

### Nghiệp vụ đúng

Lộ trình Review là **văn bản học vụ của một bộ môn cho một kỳ của một ngành**. Người không thuộc phạm vi đó không được tạo, sửa hay công bố nó. Đây không phải "RBAC chưa nhất quán" — đây là **lỗ hổng phân quyền**: một người có thể đổi điều kiện bảo vệ của sinh viên ngành khác.

`ADMIN` thì sao: Admin trần **không** được publish văn bản học vụ. Tiền lệ đã có trong repo — `BE-AW-005` đã siết publish kết quả để Admin trần không làm được. Theo đúng tiền lệ đó.

### Cách xử lý đề xuất

Repo **đã có sẵn** hàm cần dùng: `ReviewGateService.cs:647-660` `RequireScopeMutationAuthority` — nó đã giải đúng bài toán "actor này có thẩm quyền trên phạm vi này không". Tái dụng, đừng viết hàm thứ hai.

1. Thêm `RequireTemplateAuthority(SaveReviewGateTemplateRequest input, CancellationToken ct)` trong cùng file, cạnh `RequireStaff`:
   - `ADMIN` trần ⇒ `ForbiddenException` với `errorCode = "TEMPLATE_AUTHORITY_ADMIN_NOT_ACADEMIC"`.
   - `DEPARTMENT_STAFF`: resolve department của actor. Nếu `input.Major` khác null ⇒ major đó phải thuộc department của actor. Nếu `input.ProjectPeriodId` khác null ⇒ period đó phải thuộc department của actor.
   - `Mode = "INTERDISCIPLINARY"` ⇒ actor phải thuộc **bộ môn chủ trì** của kỳ đó.
   - Không thỏa ⇒ `ForbiddenException`, `errorCode = "TEMPLATE_SCOPE_FORBIDDEN"`, message nói rõ ngành/kỳ nào bị từ chối.
2. Gọi nó ở `:86` (`SaveTemplate`) và `:138` (`PublishTemplate`) — **thay** `RequireStaff`, không phải thêm vào sau.
3. **Chín call site `RequireStaff` còn lại: không chạm trong ticket này.** Chúng là đọc/vận hành, không phải tạo văn bản. Nếu thấy chỗ nào cũng cần siết thì **viết ra, đừng tự sửa** — mở ticket riêng.

### Tiêu chí nghiệm thu

- Staff bộ môn Marketing gọi `SaveTemplate` với `Major = "SE"` ⇒ **403** kèm `errorCode = "TEMPLATE_SCOPE_FORBIDDEN"`.
- Staff bộ môn Marketing gọi `PublishTemplate` cho template của ngành SE ⇒ **403**.
- Staff bộ môn Marketing gọi với `Major = "MKT"` và period của mình ⇒ **200**, không hồi quy.
- `ADMIN` trần gọi `PublishTemplate` ⇒ **403** kèm `errorCode = "TEMPLATE_AUTHORITY_ADMIN_NOT_ACADEMIC"`.
- Template liên ngành: staff bộ môn **tham gia** (không chủ trì) ⇒ **403**; staff bộ môn **chủ trì** ⇒ **200**.
- Chín call site `RequireStaff` còn lại không đổi hành vi.

### Kiểm chứng

```bash
dotnet test AIPMS.sln --filter "FullyQualifiedName~ReviewGateTemplateAuthority" --configuration Release
git grep -n "RequireStaff\|RequireTemplateAuthority" -- src/AIPMS.Infrastructure/Services/Projects/ReviewGateService.cs
# kỳ vọng: :86 và :138 gọi RequireTemplateAuthority; 9 chỗ còn lại vẫn RequireStaff
```

### File dự kiến

- `src/AIPMS.Infrastructure/Services/Projects/ReviewGateService.cs` (sửa `:86`, `:138`; thêm hàm cạnh `:61`)
- `tests/.../ReviewGateTemplateAuthorityTests.cs` (mới)

---

## BE-02 · HIGH · Review Gate có 0 test hành vi — phải dựng lưới trước khi ai sửa nó

### Bằng chứng

```bash
git grep -iln "ReviewGate" 14eecc7a -- tests
# → 1 file duy nhất, và là DepartOpenApiTests.cs (test hình dạng OpenAPI, không phải hành vi)
```

Toàn bộ subsystem Review Gate — `ReviewGateService.cs` hơn 700 dòng, 19 call site so `Status == "PASSED"`, logic tiến độ, logic sẵn sàng bảo vệ, 11 guard phân quyền — **không có một test hành vi nào**.

Và `docs/FINAL_BACKEND_ACCEPTANCE_REPORT.md` khai cho UC-135:

> `ReviewGateServiceTests.cs (77 tests) TESTS PASS`

```bash
git ls-files "*ReviewGateServiceTests.cs"   # → rỗng
git log --all --diff-filter=A -- "*ReviewGateServiceTests.cs"   # → rỗng
```

File đó **chưa từng tồn tại ở bất kỳ commit nào của bất kỳ ref nào.** Đây là `X9`.

### Nghiệp vụ

Việc tách `Gate.Status` thành `CommitteeOutcome` (Làn 0, chờ bộ môn) sẽ phải sửa 19 call site. Sửa 19 chỗ trong một vùng không có test là cách chắc chắn nhất để làm vỡ một hành vi mà không ai biết. **Dựng lưới trước, sửa sau.**

Ticket này **không đổi hành vi nào**. Nó ghi lại hành vi hiện tại thành test. Nếu viết test mà thấy hành vi hiện tại **sai nghiệp vụ** ⇒ **vẫn test đúng hành vi hiện tại**, đánh dấu `// TODO(GATE-02): hành vi này có thể sai, xem Q9` và **báo ra**, không sửa trong ticket này.

### Cách xử lý đề xuất

20 test, đặt ở `tests/.../ReviewGateServiceBehaviorTests.cs`. Dùng `WebApplicationFactory` + `IsolatedSqlDatabase` theo mẫu có sẵn trong repo.

| # | Test | Kỳ vọng (hành vi HIỆN TẠI) |
|---|---|---|
| 1 | `SaveTemplate` với `Gates` rỗng | 422 |
| 2 | `SaveTemplate` với `Sequence` trùng | 422 (`:90`) |
| 3 | `SaveTemplate` với `Code` gate trùng (khác hoa thường) | 422 |
| 4 | `SaveTemplate` với `Mode` lạ | 422 |
| 5 | `PublishTemplate` khi có gate không có requirement | 422 (`:144`) |
| 6 | `PublishTemplate` không kèm `ConcurrencyToken` | 422 |
| 7 | `PublishTemplate` với token cũ | 409 |
| 8 | `PublishTemplate` hai lần | lần 2 trả về bình thường, không lỗi (`:146` early-return) |
| 9 | Template 3 gate publish được | 200 |
| 10 | Template 5 gate publish được | 200 — chứng minh `N12`: số mốc là cấu hình |
| 11 | 🔴 `Mode = "INTERDISCIPLINARY"`, `Major = "SE+MKT+GD"` | `:572` khớp chuỗi `"INTERDISCIPLINARY"` thất bại ⇒ `:574` `return []` ⇒ **0 gate, KHÔNG báo lỗi**. Test ghi lại đúng cái bẫy này |
| 12 | Timeline khi project có 0 requirement | `AcademicProgress = null` + `Source = "NOT_CONFIGURED"`, **không phải 0%** (`N6`) |
| 13 | Timeline khi mới qua gate 1/4 | `AcademicProgress` theo `Weight`, không theo số gate |
| 14 | `Decide` khi chỉ 1/2 reviewer đã quyết | gate chưa `PASSED` (`:348` đòi tất cả) |
| 15 | `Decide` khi cả 2 reviewer `PASSED` | gate `PASSED`, `OpenNextGate` mở **đúng một** gate kế (`:691`) |
| 16 | `Decide` với reviewer đã `REVOKED` | 403 (`:627-630`) |
| 17 | `Decide` bởi Primary supervisor của chính project đó | 403 (`:636-637`) — COI đã có sẵn |
| 18 | `Decide` bởi Mentor ngành của chính project đó | 403 (`:636-637`) |
| 19 | `VerifyEvidence` cho requirement mà assignment không phủ | 403 (`Covers`, `:324`) |
| 20 | `DefenseReadiness` khi thiếu quyết định phạm vi liên ngành | chưa sẵn sàng (`:517-541`, `decided >= majorCount + 1`) |

### Tiêu chí nghiệm thu

- `dotnet test --filter "FullyQualifiedName~ReviewGateServiceBehaviorTests"` ⇒ `Passed: 20, Failed: 0`.
- **Không có file nào trong `src/` bị sửa.** Diff của PR này chỉ có `tests/`.
- Test #11 và #12 phải **pass**, tức là ghi lại đúng hành vi hiện tại kể cả khi hành vi đó đáng ngờ.
- Mô tả PR liệt kê mọi chỗ gắn `// TODO(GATE-02)`.

### Kiểm chứng

```bash
dotnet test AIPMS.sln --filter "FullyQualifiedName~ReviewGateServiceBehaviorTests" --configuration Release
git diff --stat origin/develop...HEAD -- src/    # kỳ vọng: rỗng
```

### File dự kiến

- `tests/.../ReviewGateServiceBehaviorTests.cs` (mới, duy nhất)

---

## BE-03 · 🔴 CRITICAL · Chưa có test nào chứng minh scheme 11 component tính đúng điểm theo ngành

### Bằng chứng

`src/AIPMS.Application/Features/Evaluations/Services/VersionedResultCalculator.cs:54`:

```csharp
if (components.Count == 0 || components.Any(c => c.Score is null || c.FinalizedEvaluators != c.RequiredEvaluators)) return null;
```

`:64`:

```csharp
if (components.Sum(c => c.WeightPercent) != 100m) return null;
return components.Sum(c => c.Score!.Value * c.WeightPercent / 100m);
```

Hàm cộng **mọi** component được truyền vào. Thiết kế chấm điểm của đợt này (xem `RUBRIC_DESIGN_2026-10-30.md` §6.2) có **11 component** cho dự án liên ngành 3 ngành. Một sinh viên ngành SE chỉ được tính **5** trong số đó:

```
#1 CHUNG×COLD (15) + #2 CHUNG×DEFENSE (35) + #3 SE×COLD (15) + #4 SE×DEFENSE (15) + #9 INDIVIDUAL-SE (20) = 100 ✅
```

Truyền cả 11 ⇒ tổng = **215** ⇒ `:64` trả `null`. Không có test nào kiểm điều này.

Và `:31` chặn công thức demo:

```csharp
if (policy.Formula == "DEMO_50_30_20" && (!policy.IsDemo || components.Count != 1 + majors.Count * 2 ...
```

3 ngành ⇒ `1 + 3*2 = 7` component. Cấu trúc này có 11 ⇒ **`DEMO_50_30_20` không dùng được**, buộc phải `COMPONENT_WEIGHTS`.

### Nghiệp vụ

Đợt này chấm **điểm thật của sinh viên**. Lọc sai bộ component ⇒ hoặc kết quả treo ở `null` mãi, hoặc tệ hơn: lọc thiếu mà tổng vẫn tình cờ bằng 100 ⇒ **điểm sai mà không ai biết**. Đây là rủi ro số một của đợt này.

### Cách xử lý đề xuất

Test thuần, không cần DB. `VersionedResultCalculator` là static class ⇒ gọi trực tiếp.

| # | Test | Kỳ vọng |
|---|---|---|
| 1 | 5 component đã lọc đúng cho SV ngành SE, đủ phiếu | tổng **đúng 100**, trả số thật |
| 2 | Truyền cả 11 component | `null` (tổng 215) |
| 3 | Lọc thiếu `#9 INDIVIDUAL` (còn 80) | `null` |
| 4 | 5 component đúng nhưng một component `Score = null` | `null` — `N6` |
| 5 | 5 component đúng, một component `FinalizedEvaluators = 2 / RequiredEvaluators = 3` | `null`, **không chia lại trọng số** cho 2 người đã chấm — `N6` |
| 6 | Một component `Score = 0` (không phải null) | tính được, 0 là điểm thật — `N6` |
| 7 | `Formula = "DEMO_50_30_20"` với 11 component, 3 ngành | `Validate` throw — `:31` |
| 8 | `Formula = "COMPONENT_WEIGHTS"`, `IsDemo = true`, `ApprovalStatus = "DEMO"` | `Validate` pass |
| 9 | `IsDemo = false`, `ApprovalStatus = "APPROVED"`, `ApprovalReference` **rỗng** | `Validate` throw — `N7`, `:17` |
| 10 | `IsDemo = false`, `ApprovalStatus = "APPROVED"`, `ApprovalReference = "QĐ 123/QĐ-ĐHX"` | `Validate` pass |
| 11 | `IsDemo = true` nhưng `ApprovalStatus = "APPROVED"` | `Validate` throw — `N7` |
| 12 | Dự án liên ngành **2 ngành** (8 component), SV ngành MKT lọc còn 5 | tổng **đúng 100** — chứng minh ma trận không đổi khi bớt ngành |

Ma trận trọng số lấy từ `RUBRIC_DESIGN_2026-10-30.md` §6.1: CHUNG 15/35 · theo-ngành 15/15 · cá nhân 0/20.

### Tiêu chí nghiệm thu

- `dotnet test --filter "FullyQualifiedName~ElevenComponentScoring"` ⇒ `Passed: 12, Failed: 0`.
- Không sửa file nào trong `src/`.
- Test #5 phải chứng minh rõ: **không** có chỗ nào chia lại trọng số.

### Kiểm chứng

```bash
dotnet test AIPMS.sln --filter "FullyQualifiedName~ElevenComponentScoring" --configuration Release
git diff --stat origin/develop...HEAD -- src/    # kỳ vọng: rỗng
```

### File dự kiến

- `tests/.../ElevenComponentScoringTests.cs` (mới, duy nhất)

---

### ✅ CHECKPOINT 0 — sau BE-01, BE-02, BE-03

Báo lại ba thứ:

1. `BE-01`: ba PR merged chưa, có hồi quy nào ở 9 call site `RequireStaff` còn lại không.
2. `BE-02`: output `Passed: 20, Failed: 0`, và **danh sách chỗ gắn `TODO(GATE-02)`** — đây là input cho quyết định `Q9–Q12` của bộ môn.
3. `BE-03`: output `Passed: 12, Failed: 0`. Nếu bất kỳ test nào trong #1–#12 **fail**, dừng lại ngay và báo: nghĩa là bộ máy tính điểm không làm điều mà thiết kế giả định, và cả kế hoạch chấm phải xem lại.

---

# PHASE 1 — HỢP ĐỒNG NHỎ

## BE-04 · MEDIUM · Dự án liên ngành không tạo được bộ báo cáo chấm nguội phần CHUNG

### Bằng chứng

`src/AIPMS.Application/Features/Cold/ColdContracts.cs:8`:

```csharp
public sealed record CreateColdTemplateRequest(long MajorId, long ProjectPeriodId, string Name, IReadOnlyList<ColdReportInput> Reports);
```

`MajorId` là `long` **không nullable**.

Thiết kế chấm điểm có ba phạm vi: `COMMON` · `MAJOR_SPECIFIC` · `INDIVIDUAL`. Bộ báo cáo chấm nguội phần **CHUNG** của dự án liên ngành **không có `MajorId` tự nhiên nào** — nó thuộc cả ba ngành. Hiện buộc phải nhồi một `MajorId` bất kỳ, làm dữ liệu sai nghĩa.

### Nghiệp vụ

Bộ báo cáo chấm nguội phải khai được phạm vi của mình, giống như component của scheme đã khai (`Scope` + `MajorId`). `COMMON` ⇒ không có ngành. `MAJOR_SPECIFIC` ⇒ có ngành.

### Cách xử lý đề xuất

1. `db/changes/20261030_cold_template_common_scope.sql`:
   - `cold_report_templates.major_id` → `NULL`-able (dùng `IF EXISTS (SELECT 1 FROM sys.columns ... AND is_nullable = 0)` rồi `ALTER COLUMN`, theo mẫu `20260922_add_rubric_hierarchy.sql`).
   - `ADD scope NVARCHAR(32) NULL`.
   - `CHECK ck_cold_template_scope`: `scope IS NULL OR scope IN ('COMMON','MAJOR_SPECIFIC','INDIVIDUAL')` — cho `NULL` để hàng cũ hợp lệ.
   - `CHECK ck_cold_template_scope_major`: `scope <> 'COMMON' OR major_id IS NULL` — COMMON thì không được có ngành.
2. Cập nhật `db/schema.sql` + thêm dòng vào `db/e2e/migrations.json`.
3. DTO: `long MajorId` → `long? MajorId`, thêm `string? Scope`.
4. Validate trong workflow (không ở DB): `Scope == "COMMON"` ⇒ `MajorId` phải null, ngược lại 422 `errorCode = "COLD_TEMPLATE_COMMON_HAS_MAJOR"`. `Scope == "MAJOR_SPECIFIC"` ⇒ `MajorId` bắt buộc, 422 `errorCode = "COLD_TEMPLATE_MAJOR_REQUIRED"`.
5. **Hàng cũ `scope = NULL` phải đọc và dùng được như trước.** Không backfill.

### Tiêu chí nghiệm thu

- Tạo template `Scope = "COMMON"`, `MajorId = null` ⇒ 201.
- Tạo template `Scope = "COMMON"`, `MajorId = 5` ⇒ 422 `COLD_TEMPLATE_COMMON_HAS_MAJOR`.
- Tạo template `Scope = "MAJOR_SPECIFIC"`, `MajorId = null` ⇒ 422 `COLD_TEMPLATE_MAJOR_REQUIRED`.
- Template cũ (`scope = NULL`, có `major_id`) ⇒ GET trả về đúng, không lỗi.
- Chạy migration hai lần ⇒ không lỗi.

### Kiểm chứng

```bash
dotnet test AIPMS.sln --filter "FullyQualifiedName~ColdTemplate" --configuration Release
# chạy migration 2 lần trên isolated fixture
grep -c "20261030_cold_template_common_scope.sql" db/e2e/migrations.json   # kỳ vọng: 1
```

### File dự kiến

- `db/changes/20261030_cold_template_common_scope.sql` (mới) · `db/schema.sql` · `db/e2e/migrations.json`
- `src/AIPMS.Application/Features/Cold/ColdContracts.cs:8`
- Workflow + repository của Cold
- `tests/.../ColdTemplateScopeTests.cs` (mới)

---

## BE-05 · HIGH · Mười một giới hạn upload trên bốn mức khác nhau, FE phải đoán

### Bằng chứng

```bash
git grep -oh "RequestSizeLimit([0-9 *_]*)" 14eecc7a -- src/AIPMS.Api/Controllers | sort | uniq -c
#   3 RequestSizeLimit(11 * 1024 * 1024)
#   4 RequestSizeLimit(22 * 1024 * 1024)
#   2 RequestSizeLimit(6 * 1024 * 1024)
#   2 RequestSizeLimit(9 * 1024 * 1024)
```

**11 thuộc tính, 4 mức khác nhau: 6 · 9 · 11 · 22 MiB.** Và FE hardcode một con số thứ năm không trùng cái nào:

`ai-pms-frontend/src/features/review-gates/api/review-gates-api.ts:95`:

```ts
if (req.file.size > 20 * 1024 * 1024) throw new Error('Tệp vượt quá giới hạn 20 MiB.')
```

⇒ FE chặn ở 20 MiB cho một endpoint mà BE cho 22 MiB (mất 2 MiB hợp lệ), và cho qua 20 MiB ở nơi BE chỉ nhận 6 MiB (người dùng chờ upload xong rồi mới bị từ chối). Đây là `X7`.

Ngoài ra enum parent type hiện có **6 giá trị**, thiếu `REPORT` · `CORRECTION` · `ANNOUNCEMENT` mà v3.0 REQ-33 cần.

### Nghiệp vụ

Giới hạn và loại file cho phép là **policy của backend**. FE phải **đọc**, không được đoán và không được hardcode. Người dùng phải biết giới hạn **trước khi** chọn file, không phải sau khi upload thất bại.

### Cách xử lý đề xuất

1. Endpoint mới: `GET /v1/upload-policies` → danh sách; hoặc `GET /v1/upload-policies/{parentType}` → một bản.

   ```jsonc
   {
     "parentType": "REVIEW_GATE_EVIDENCE",
     "maxBytes": 23068672,              // 22 MiB — lấy từ RequestSizeLimit thật của endpoint đó
     "maxBytesLabel": "22 MiB",         // BE sinh nhãn, FE không tự format
     "allowedMimeTypes": ["application/pdf", "image/png", "..."],
     "allowedExtensions": [".pdf", ".png"],
     "maxFilesPerRequest": 1,
     "requiresVersionAnchor": true
   }
   ```
2. **Lấy số từ một nguồn duy nhất.** Khai một static class `UploadPolicies` trong `Application`, các controller đọc hằng từ đó cho `[RequestSizeLimit]`, endpoint mới cũng đọc từ đó. Không để hai chỗ ghi hai số.
3. **Không đổi 4 mức hiện tại trong ticket này.** Chỉ expose. Việc thống nhất về một mức là quyết định sản phẩm — viết ra, đừng tự chốt.
4. Ba parent type thiếu (`REPORT`, `CORRECTION`, `ANNOUNCEMENT`): **chưa thêm.** `CORRECTION` và `ANNOUNCEMENT` thuộc Làn 0 (chờ `Q16–Q18`, `Q28`), nên thêm bây giờ là tạo enum value cho một domain chưa tồn tại. Ghi vào mô tả PR là việc còn nợ.
5. 413 phải có `errorCode = "UPLOAD_TOO_LARGE"` kèm `maxBytes` trong body, để FE nói được con số đúng.

### Tiêu chí nghiệm thu

- `GET /v1/upload-policies` trả đủ **11** entry, số `maxBytes` khớp đúng `[RequestSizeLimit]` của từng endpoint.
- Sửa hằng trong `UploadPolicies` ⇒ **cả** `[RequestSizeLimit]` và response của endpoint mới đổi theo. Có test chứng minh hai chỗ không lệch.
- Upload quá size ⇒ 413 kèm `errorCode = "UPLOAD_TOO_LARGE"` và `maxBytes`.
- Example JSON đã nằm trong `docs/contracts/` — `X3`.

### Kiểm chứng

```bash
dotnet test AIPMS.sln --filter "FullyQualifiedName~UploadPolicy" --configuration Release
git grep -c "RequestSizeLimit" -- src/AIPMS.Api/Controllers      # kỳ vọng: 11
git grep -n "UploadPolicies\." -- src/AIPMS.Api/Controllers      # kỳ vọng: 11 chỗ, không còn số literal
ls docs/contracts/*/upload-policies*.json
```

### File dự kiến

- `src/AIPMS.Application/Features/Files/UploadPolicies.cs` (mới)
- `src/AIPMS.Api/Controllers/UploadPoliciesController.cs` (mới)
- 11 controller có `[RequestSizeLimit]` (thay literal bằng hằng)
- `docs/contracts/.../upload-policies.json` (mới)
- `tests/.../UploadPolicyConsistencyTests.cs` (mới)

---

### ✅ CHECKPOINT 1 — sau BE-04, BE-05

Báo: `BE-05` đã nộp example JSON cho FE chưa (`X3`), và danh sách việc còn nợ (3 parent type, thống nhất 4 mức giới hạn).

---

# PHASE 2 — ĐƯỜNG GĂNG RUBRIC

> # 🔴 CẢ BA TICKET PHASE 2: CHỜ VĂN BẢN
>
> **Được phép:** tạo nhánh, viết SQL, viết code, viết test, mở PR, nhờ review.
> **Không được phép: merge.** Chờ `CAU_HOI_BO_MON_2026-10-30.md` mục `Q-A10` (`BE-06`) và `Q-N02` (`BE-07`, `BE-08`) có chữ ký.
> **Không ai được điền `ApprovalReference` giả.** Xem mục 0.9 nhóm B.

## BE-06 · 🔴 BLOCKER · Rubric không có chỗ nào lưu mô tả mức chất lượng

### Bằng chứng

```bash
git grep -in "descriptor" 14eecc7a -- src/AIPMS.Application/Features/Evaluations
# → rỗng
```

`src/AIPMS.Application/Features/Evaluations/DTOs/RubricDtos.cs:6-7` — toàn bộ những gì một tiêu chí có:

```csharp
public sealed record RubricCriterionDto(long Id, long CriterionId, string Name, string? Description,
    decimal WeightPercent, decimal? MaxScore, int SortOrder, bool IsRequired)
```

Và `db/schema.sql:1094` — `rubric_criteria` **không có cột name/description**, nó FK sang `evaluation_criteria`:

```sql
CREATE TABLE dbo.rubric_criteria (
    id BIGINT IDENTITY(1,1) NOT NULL,
    rubric_id BIGINT NOT NULL,
    criterion_id BIGINT NOT NULL,     -- → dbo.evaluation_criteria(id)
    parent_id BIGINT NULL,
    weight_percent DECIMAL(5,2) NOT NULL,
    max_score DECIMAL(8,2) NULL,
    sort_order INT NOT NULL,
    is_required BIT NOT NULL,
    ...
```

`evaluation_criteria` có `code` · `name` · `description NVARCHAR(1000)`.

⇒ Chỗ duy nhất có thể nhồi mô tả 4 mức là `evaluation_criteria.description`, một ô văn bản 1000 ký tự dùng chung cho mọi rubric. **Đợt này không chấp nhận workaround đó** (xem đầu file).

> ✅ **Tin tốt, đọc kỹ để không làm thừa:** `criterionCode` **không cần cột mới** — nó đã tồn tại là `evaluation_criteria.code`. Việc cần làm chỉ là **expose** nó lên DTO, không phải thêm nó vào schema.

### Nghiệp vụ

Một rubric học thuật dùng được phải nói rõ **"mức nào là 9 điểm, mức nào là 5 điểm"**, nếu không hai giảng viên chấm cùng một bài sẽ lệch nhau và sinh viên khiếu nại thì không có căn cứ bảo vệ. Chuẩn AAC&U VALUE và rubric ABET đều là ma trận tiêu chí × mức, không phải danh sách tiêu chí có trọng số.

**Vì sao mô tả mức phải gắn vào `rubric_criteria`, không phải `evaluation_criteria`:** cùng một khái niệm tiêu chí (ví dụ "tích hợp liên ngành") được chấm ở **hai chặng khác nhau với hai bộ mô tả khác nhau** — chặng COLD chấm *hợp đồng tích hợp có được viết và truy vết được không*, chặng DEFENSE chấm *nhóm có giải thích được quyết định tích hợp khi bị chất vấn không*. Và `max_score` cũng là per-`rubric_criteria`, nên dải điểm gợi ý phải đi cùng. Đó là lý do bảng mới FK sang `rubric_criteria`, không sang `evaluation_criteria`.

### Cách xử lý đề xuất

**Số mức (4 hay 5) là `Q-A10`, chưa ký.** Thiết kế dưới đây **không phụ thuộc con số đó** — số mức là dữ liệu, không phải schema. Đó là lý do dùng bảng riêng thay vì 4 cặp cột.

`db/changes/20261030_add_rubric_criterion_levels.sql`:

```sql
-- Additive and rerunnable. Legacy rubrics without levels stay readable and scorable.
-- Level count is data, not schema: a 4-level and a 5-level rubric both fit.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.rubric_criterion_levels', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.rubric_criterion_levels (
            id                  BIGINT IDENTITY(1,1) NOT NULL,
            rubric_criterion_id BIGINT NOT NULL,
            level_order         INT NOT NULL,              -- 1 = thấp nhất
            label               NVARCHAR(100) NOT NULL,
            descriptor          NVARCHAR(1000) NOT NULL,
            score_from          DECIMAL(8,2) NULL,         -- dải GỢI Ý, không phải ngưỡng đạt
            score_to            DECIMAL(8,2) NULL,
            created_at          DATETIME2(0) NOT NULL
                CONSTRAINT df_rubric_criterion_levels_created_at DEFAULT (SYSUTCDATETIME()),
            updated_at          DATETIME2(0) NOT NULL
                CONSTRAINT df_rubric_criterion_levels_updated_at DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT pk_rubric_criterion_levels PRIMARY KEY (id),
            CONSTRAINT uq_rubric_criterion_levels_order UNIQUE (rubric_criterion_id, level_order),
            CONSTRAINT ck_rubric_criterion_levels_order CHECK (level_order > 0),
            CONSTRAINT ck_rubric_criterion_levels_band
                CHECK (score_from IS NULL OR score_to IS NULL OR score_from <= score_to),
            CONSTRAINT fk_rubric_criterion_levels_criterion FOREIGN KEY (rubric_criterion_id)
                REFERENCES dbo.rubric_criteria(id) ON DELETE CASCADE ON UPDATE NO ACTION
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.rubric_criterion_levels')
                   AND name = N'ix_rubric_criterion_levels_criterion')
        EXEC(N'CREATE INDEX ix_rubric_criterion_levels_criterion
               ON dbo.rubric_criterion_levels(rubric_criterion_id, level_order)');

    -- Hướng dẫn chấm và chuẩn đầu ra: thuộc tiêu chí TRONG MỘT rubric, không phải khái niệm dùng chung
    IF COL_LENGTH(N'dbo.rubric_criteria', N'learning_outcome') IS NULL
        ALTER TABLE dbo.rubric_criteria ADD learning_outcome NVARCHAR(500) NULL;

    IF COL_LENGTH(N'dbo.rubric_criteria', N'evidence_guidance') IS NULL
        ALTER TABLE dbo.rubric_criteria ADD evidence_guidance NVARCHAR(1000) NULL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
```

**Rollback** (viết vào mô tả PR, không commit): `DROP TABLE dbo.rubric_criterion_levels;` + `ALTER TABLE dbo.rubric_criteria DROP COLUMN learning_outcome, evidence_guidance;`

**DTO delta** — `RubricDtos.cs`:

```csharp
public sealed record RubricCriterionLevelDto(int LevelOrder, string Label, string Descriptor,
    decimal? ScoreFrom, decimal? ScoreTo);

// thêm vào RubricCriterionDto:
public string? CriterionCode { get; init; }        // ← evaluation_criteria.code, ĐÃ CÓ, chỉ expose
public string? LearningOutcome { get; init; }
public string? EvidenceGuidance { get; init; }
public IReadOnlyList<RubricCriterionLevelDto> Levels { get; init; } = [];
```

Input tương ứng trên `RubricCriterionInput`.

**Bất biến — enforce ở `RubricRules`, KHÔNG ở DB:**

Thêm vào `RubricRules.EnsurePublishable` (hoặc hàm mới `EnsureSubmittable` nếu `BE-08` đã có):

- Mọi tiêu chí **lá** phải có **đủ** số mức theo cấu hình, `level_order` liên tục từ 1, `descriptor` không rỗng.
- 🔴 **Chỉ chặn ở bước gửi duyệt / publish. KHÔNG chặn ở `Save Draft`** — nếu chặn lúc lưu nháp thì bộ môn không soạn dần được, và 38 tiêu chí × 4 mức là việc của nhiều buổi.
- 🔴 **Rubric cũ không có mức vẫn phải đọc và chấm được.** `Levels = []` là hợp lệ cho mọi rubric đã `PUBLISHED`/`RETIRED` trước migration. Chỉ rubric đi qua đường gửi-duyệt mới bị đòi đủ mức.
- Tiêu chí **cha** không có mức (nó không được chấm — `N5`). Có mức trên tiêu chí cha ⇒ 422 `errorCode = "RUBRIC_PARENT_HAS_LEVELS"`.
- `errorCode` cho thiếu mức: `"RUBRIC_LEAF_LEVELS_INCOMPLETE"`, message nói rõ **tiêu chí nào** thiếu **mức nào**.

**Số mức lấy từ đâu khi `Q-A10` chưa ký:** đọc từ cấu hình (`appsettings` key `Rubric:RequiredLevelCount`), mặc định `4`. Khi bộ môn chốt 5 thì đổi cấu hình, **không sửa code, không migrate.** Đây là cách ticket này tiến được mà không chờ chữ ký.

### Tiêu chí nghiệm thu

- Migration chạy hai lần ⇒ không lỗi.
- Rubric `DRAFT` lưu được khi tiêu chí lá **chưa có mức nào** ⇒ 200.
- Rubric có tiêu chí lá thiếu 1 mức ⇒ gửi duyệt/publish **422** kèm `RUBRIC_LEAF_LEVELS_INCOMPLETE` và tên tiêu chí.
- Rubric đủ mức mọi tiêu chí lá ⇒ gửi duyệt/publish 200.
- Đặt mức trên tiêu chí **cha** ⇒ 422 `RUBRIC_PARENT_HAS_LEVELS`.
- 🔴 Rubric legacy (`PUBLISHED` trước migration, `Levels = []`) ⇒ GET 200, chấm được bình thường, **không** bị đòi mức.
- Đổi `Rubric:RequiredLevelCount` 4 → 5 ⇒ validate theo 5, không cần build lại DB.
- `RubricHierarchyMigrationTests` và mọi test rubric cũ **vẫn xanh**.
- `WeeklyAssessmentService` (dùng `EvaluationScoreRecord` + snapshot `RubricJson`) **không hồi quy** — chạy smoke test Weekly.

### Kiểm chứng

```bash
dotnet test AIPMS.sln --filter "FullyQualifiedName~RubricLevel|FullyQualifiedName~RubricHierarchy|FullyQualifiedName~WeeklyAssessment" --configuration Release
git grep -n "CriterionCode\|Levels" -- src/AIPMS.Application/Features/Evaluations/DTOs/RubricDtos.cs
grep -c "20261030_add_rubric_criterion_levels.sql" db/e2e/migrations.json   # kỳ vọng: 1
# chạy migration 2 lần trên isolated fixture
```

### File dự kiến

- `db/changes/20261030_add_rubric_criterion_levels.sql` (mới) · `db/schema.sql` · `db/e2e/migrations.json`
- `src/AIPMS.Application/Features/Evaluations/DTOs/RubricDtos.cs`
- `src/AIPMS.Application/Features/Evaluations/Models/RubricModels.cs`
- `src/AIPMS.Application/Features/Evaluations/Services/RubricRules.cs`
- `src/AIPMS.Infrastructure/Persistence/Repositories/RubricRepository.cs` · `…/Configuration/AipmsDbContext.Rubrics.cs`
- `docs/contracts/.../rubric-with-levels.json` (mới — `X3`)
- `tests/.../RubricLevelTests.cs` (mới)

---

## BE-07 · 🔴 BLOCKER · Chín rubric của một kỳ chỉ phân biệt được bằng `code` và tên

### Bằng chứng

`db/schema.sql:1067` — toàn bộ scope mà một rubric khai được:

```sql
CREATE TABLE dbo.rubrics (
    id                   BIGINT IDENTITY(1,1) NOT NULL,
    department_id        BIGINT NULL,
    academic_semester_id BIGINT NULL,
    code                 NVARCHAR(50) NOT NULL,
    name                 NVARCHAR(255) NOT NULL,
    description          NVARCHAR(1000) NULL,
    is_active            BIT NOT NULL,
    ...
    CONSTRAINT ck_rubrics_scope CHECK (department_id IS NOT NULL OR academic_semester_id IS NOT NULL),
```

```bash
git grep -in "evaluation_stage\|evaluation_scope\|project_mode" 14eecc7a -- db/schema.sql
# → rỗng
```

Thiết kế của đợt này (xem `RUBRIC_DESIGN_2026-10-30.md` §6.3) cần **9 rubric** cho một kỳ liên ngành:

```
CMN-COLD · CMN-DEF · SE-COLD · SE-DEF · MKT-COLD · MKT-DEF · GD-COLD · GD-DEF · IND-DEF
```

Cả chín đều cùng `department_id` + `academic_semester_id`. Chúng chỉ khác nhau ở `code` — một chuỗi tự do do người nhập đặt. Không có cách nào để hệ thống biết rubric nào dùng cho ô nào ⇒ **gán sai rubric cho component là chuyện sẽ xảy ra**, và không gì chặn.

Thiếu luôn `approval_reference` — mà `N7` lại đòi nó để bỏ nhãn DEMO.

### Nghiệp vụ

Rubric phải tự khai được: dùng cho **hình thức** đồ án nào, **chặng** nào, **phạm vi** nào, **ngành** nào, có hiệu lực từ bao giờ đến bao giờ, và được phê duyệt bởi **văn bản** nào. Không có những thông tin đó thì nó là một tờ giấy rời, không phải văn bản học vụ.

### Cách xử lý đề xuất

**`Q-N02` (ai sở hữu, ai duyệt) chưa ký.** Thiết kế dưới đây thêm *dữ liệu* để mô tả phạm vi; phần *ai được làm gì với phạm vi đó* nằm ở `BE-08`. Tách vậy để ticket này tiến được.

`db/changes/20261030_add_rubric_scope_fields.sql` — bảy cột, **tất cả `NULL`-able, không backfill**:

| Cột | Type | Nghĩa |
|---|---|---|
| `major_id` | `BIGINT NULL` FK → `dbo.majors(id)` | ngành, `NULL` khi scope là COMMON hoặc rubric cũ |
| `project_mode` | `NVARCHAR(32) NULL` | `SINGLE_MAJOR` \| `INTERDISCIPLINARY` |
| `evaluation_stage` | `NVARCHAR(32) NULL` | `ON_GOING` \| `COLD` \| `DEFENSE` |
| `evaluation_scope` | `NVARCHAR(32) NULL` | `COMMON` \| `MAJOR_SPECIFIC` \| `INDIVIDUAL` |
| `effective_from` | `DATE NULL` | — |
| `effective_to` | `DATE NULL` | — |
| `approval_reference` | `NVARCHAR(1000) NULL` | số văn bản phê duyệt. Dài 1000 cho khớp `ApprovalReference?.Length > 1000` ở `VersionedResultCalculator.cs:19` |

**CHECK constraint — tất cả đều cho `NULL` qua, để hàng cũ hợp lệ:**

```sql
ck_rubrics_project_mode:     project_mode IS NULL OR project_mode IN ('SINGLE_MAJOR','INTERDISCIPLINARY')
ck_rubrics_evaluation_stage: evaluation_stage IS NULL OR evaluation_stage IN ('ON_GOING','COLD','DEFENSE')
ck_rubrics_evaluation_scope: evaluation_scope IS NULL OR evaluation_scope IN ('COMMON','MAJOR_SPECIFIC','INDIVIDUAL')
ck_rubrics_scope_major:      evaluation_scope <> 'COMMON' OR major_id IS NULL
ck_rubrics_effective_range:  effective_from IS NULL OR effective_to IS NULL OR effective_from <= effective_to
```

Index: `CREATE INDEX ix_rubrics_scope ON dbo.rubrics(academic_semester_id, project_mode, evaluation_stage, evaluation_scope, major_id)` — để tra "rubric nào cho ô này".

**Bất biến enforce ở `RubricWorkflow` / `RubricRules`, không ở DB:**

- `evaluation_scope = 'COMMON'` ⇒ `major_id` phải `NULL`. 422 `errorCode = "RUBRIC_COMMON_HAS_MAJOR"`.
- `evaluation_scope = 'MAJOR_SPECIFIC'` ⇒ `major_id` bắt buộc. 422 `errorCode = "RUBRIC_MAJOR_REQUIRED"`.
- 🔴 **`approval_reference` không được set thủ công qua `UpdateRubricRequest`.** Nó chỉ được ghi qua đường phê duyệt của `BE-08`. Cho ghi tự do ở đây là mở đúng cái cửa mà `N7` muốn đóng. Nếu `BE-08` chưa merge thì để field này **readonly, luôn `NULL`**.
- Rubric đã `PUBLISHED` + `IsReferenced` ⇒ không sửa được bảy cột này (đã có `RubricRules.EnsureEditable:10`, kiểm lại là nó phủ).

**DTO:** thêm bảy field tương ứng vào `RubricDto` và `CreateRubricRequest`; `UpdateRubricRequest` **không** nhận `ApprovalReference`.

### Tiêu chí nghiệm thu

- Migration chạy hai lần ⇒ không lỗi.
- Tạo được 9 rubric cùng department + semester, phân biệt bằng (`project_mode`, `evaluation_stage`, `evaluation_scope`, `major_id`) ⇒ 201 cả 9.
- `evaluation_scope = "COMMON"` + `major_id = 3` ⇒ 422 `RUBRIC_COMMON_HAS_MAJOR`.
- `evaluation_scope = "MAJOR_SPECIFIC"` + `major_id = null` ⇒ 422 `RUBRIC_MAJOR_REQUIRED`.
- `effective_from > effective_to` ⇒ 422.
- `UpdateRubricRequest` có `approvalReference` ⇒ bị bỏ qua hoặc 422; `approval_reference` trong DB **không đổi**.
- 🔴 Rubric legacy (7 cột `NULL`) ⇒ GET 200, chấm được, publish/retire được như trước.
- Rubric `PUBLISHED` + referenced ⇒ sửa bảy cột ⇒ 409.

### Kiểm chứng

```bash
dotnet test AIPMS.sln --filter "FullyQualifiedName~RubricScope" --configuration Release
git grep -n "evaluation_stage\|evaluation_scope\|project_mode\|approval_reference" -- db/schema.sql
grep -c "20261030_add_rubric_scope_fields.sql" db/e2e/migrations.json   # kỳ vọng: 1
```

### File dự kiến

- `db/changes/20261030_add_rubric_scope_fields.sql` (mới) · `db/schema.sql` · `db/e2e/migrations.json`
- `src/AIPMS.Application/Features/Evaluations/DTOs/RubricDtos.cs` · `Models/RubricModels.cs`
- `src/AIPMS.Application/Features/Evaluations/Services/RubricWorkflow.cs` · `RubricRules.cs`
- `src/AIPMS.Infrastructure/Persistence/Repositories/RubricRepository.cs` · `…/AipmsDbContext.Rubrics.cs`
- `docs/contracts/.../rubric-scoped.json` (mới)
- `tests/.../RubricScopeTests.cs` (mới)

---

## BE-08 · 🔴 BLOCKER · Một người tự publish được rubric dùng để chấm điểm thật

### Bằng chứng

`db/schema.sql:1700` — trạng thái rubric nằm ở `rubric_versions`, và CHECK chỉ cho ba giá trị:

```sql
CREATE TABLE dbo.rubric_versions (
    rubric_id BIGINT NOT NULL,
    root_rubric_id BIGINT NOT NULL,
    version_number INT NOT NULL,
    status VARCHAR(20) NOT NULL,
    concurrency_token UNIQUEIDENTIFIER NOT NULL,
    ...
    CONSTRAINT ck_rubric_versions_status CHECK (status IN ('DRAFT','PUBLISHED','RETIRED')),
```

```bash
git grep -n "PENDING_APPROVAL" 14eecc7a -- src    # → rỗng
```

⇒ Đường đi hiện tại là `DRAFT → PUBLISHED` **trực tiếp**, một lời gọi, một người. Không có bước gửi duyệt, không có người thứ hai.

`RubricWorkflow.cs:23` — authority hiện tại chỉ so `DepartmentId` của actor với của rubric. Không phân biệt người soạn và người duyệt.

> ✅ **Tin tốt, đừng làm thừa:** `RubricDtoMapper` tại `RubricDtos.cs:42` tính
> ```csharp
> r.Status == RubricStatuses.Draft && !r.IsReferenced      // → CanEdit
> ```
> Nên khi thêm `PENDING_APPROVAL`, rubric ở trạng thái đó **tự động `CanEdit = false`**. Không cần sửa `CanEdit`, và FE tự readonly theo. Kiểm lại cho chắc, đừng viết thêm nhánh.

### Nghiệp vụ

Rubric là văn bản quyết định **điểm thật của sinh viên**. Để một người vừa soạn vừa công bố nó là không có kiểm soát nào — và nếu sinh viên khiếu nại thì không có vết ai đã phê duyệt. Nguyên tắc bốn mắt (four-eyes): người gửi duyệt và người duyệt **phải là hai người khác nhau**.

Ở đợt demo thì tạm được. Đợt này **chạy thật, nên không được.**

`N13` nhắc lại: **không thêm identity role mới.** "Trưởng bộ môn" là trách nhiệm học thuật, không phải role trong DB. Người duyệt là một `DEPARTMENT_STAFF` khác người gửi, trong cùng phạm vi.

### Cách xử lý đề xuất

`db/changes/20261030_add_rubric_approvals.sql`:

**Bước 1 — mở rộng CHECK constraint.** Đây là chỗ duy nhất trong đợt này phải drop/recreate một constraint, làm cẩn thận:

```sql
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE parent_object_id = OBJECT_ID(N'dbo.rubric_versions')
           AND name = N'ck_rubric_versions_status')
    ALTER TABLE dbo.rubric_versions DROP CONSTRAINT ck_rubric_versions_status;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.rubric_versions')
               AND name = N'ck_rubric_versions_status')
    EXEC(N'ALTER TABLE dbo.rubric_versions ADD CONSTRAINT ck_rubric_versions_status
           CHECK (status IN (''DRAFT'',''PENDING_APPROVAL'',''PUBLISHED'',''RETIRED''))');
```

🔴 **Chỉ THÊM một giá trị. Ba giá trị cũ giữ nguyên nghĩa.** Đây là ngoại lệ được phép của quy tắc 1 mục 0.6 — và nó được phép chính vì nó không đổi nghĩa gì cả.

**Bước 2 — bảng vết phê duyệt:**

```sql
CREATE TABLE dbo.rubric_approvals (
    id            BIGINT IDENTITY(1,1) NOT NULL,
    rubric_id     BIGINT NOT NULL,
    submitted_by  BIGINT NOT NULL,
    submitted_at  DATETIME2(0) NOT NULL CONSTRAINT df_rubric_approvals_submitted_at DEFAULT (SYSUTCDATETIME()),
    decided_by    BIGINT NULL,
    decided_at    DATETIME2(0) NULL,
    decision      NVARCHAR(16) NULL,
    reason        NVARCHAR(1000) NULL,
    CONSTRAINT pk_rubric_approvals PRIMARY KEY (id),
    CONSTRAINT ck_rubric_approvals_decision CHECK (decision IS NULL OR decision IN ('APPROVED','REJECTED')),
    CONSTRAINT ck_rubric_approvals_decided CHECK ((decided_by IS NULL) = (decided_at IS NULL)),
    CONSTRAINT ck_rubric_approvals_four_eyes CHECK (decided_by IS NULL OR decided_by <> submitted_by),
    CONSTRAINT fk_rubric_approvals_rubric FOREIGN KEY (rubric_id) REFERENCES dbo.rubrics(id),
    CONSTRAINT fk_rubric_approvals_submitter FOREIGN KEY (submitted_by) REFERENCES dbo.users(id),
    CONSTRAINT fk_rubric_approvals_decider FOREIGN KEY (decided_by) REFERENCES dbo.users(id)
);
```

CHECK `ck_rubric_approvals_four_eyes` là **lưới cuối**, không phải chỗ enforce chính — xem dưới. Append-only: không `UPDATE` hàng đã có `decided_at`; gửi lại thì tạo hàng mới.

**Bước 3 — endpoint và guard:**

| Endpoint | Chuyển trạng thái | Guard |
|---|---|---|
| `POST /v1/rubrics/{id}/submit-for-approval` | `DRAFT → PENDING_APPROVAL` | actor thuộc phạm vi; `EnsureEditable` pass; **mọi tiêu chí lá đủ mức** (`BE-06`) |
| `POST /v1/rubrics/{id}/approve` | `PENDING_APPROVAL → PUBLISHED` | 🔴 actor ≠ `submitted_by`; actor thuộc phạm vi |
| `POST /v1/rubrics/{id}/reject` | `PENDING_APPROVAL → DRAFT` | như approve, bắt buộc có `reason` |

**Four-eyes enforce ở `RubricWorkflow`, KHÔNG ở DB constraint.** Lý do: cần thông báo lỗi có nghĩa cho người dùng, không phải một lỗi SQL constraint. `errorCode = "RUBRIC_SELF_APPROVAL_FORBIDDEN"`, HTTP **403**.

**Bước 4 — authority theo phạm vi.** Dùng bảy cột của `BE-07`:

- `evaluation_scope = 'COMMON'` + `project_mode = 'INTERDISCIPLINARY'` ⇒ chỉ staff **bộ môn chủ trì** được duyệt. Staff bộ môn tham gia ⇒ 403 `errorCode = "RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT"`.
- `evaluation_scope = 'MAJOR_SPECIFIC'` ⇒ chỉ staff **bộ môn của ngành đó** được duyệt.
- Tái dụng `ReviewGateService.cs:647-660` `RequireScopeMutationAuthority` nếu dùng được; nếu không thì theo cùng khuôn.
- 🔴 **`ADMIN` trần không được duyệt.** Theo tiền lệ `BE-AW-005`. `errorCode = "RUBRIC_APPROVAL_ADMIN_NOT_ACADEMIC"`.

**Bước 5 — `approval_reference`.** Khi approve, nhận `approvalReference` trong body và ghi vào `rubrics.approval_reference`. **Không bắt buộc** ở bước này (bộ môn có thể duyệt nội bộ trước khi có số văn bản). Nhưng nếu rỗng ⇒ scheme dùng rubric đó **vẫn phải ở `IsDemo = true`**, và đó là đúng theo `N7`. Không có đường nào đi vòng.

**Bước 6 — `POST /publish` cũ.** Không xóa (hàng legacy và test cũ dùng). Nhưng thêm guard: rubric đã có cột `evaluation_scope` khác `NULL` ⇒ `publish` trực tiếp bị từ chối 409 `errorCode = "RUBRIC_REQUIRES_APPROVAL_FLOW"`. Rubric legacy (`evaluation_scope IS NULL`) vẫn publish trực tiếp được như cũ.

### Tiêu chí nghiệm thu

- Migration chạy hai lần ⇒ không lỗi. Ba giá trị status cũ vẫn insert được.
- `submit-for-approval` khi tiêu chí lá thiếu mức ⇒ 422 `RUBRIC_LEAF_LEVELS_INCOMPLETE`.
- Rubric ở `PENDING_APPROVAL` ⇒ `PUT /v1/rubrics/{id}` trả **409**, và `GET` trả `canEdit = false` (tự động, không code thêm).
- 🔴 Người gửi tự gọi `approve` ⇒ **403** `RUBRIC_SELF_APPROVAL_FORBIDDEN`.
- Người khác cùng phạm vi gọi `approve` ⇒ 200, status `PUBLISHED`, có hàng `rubric_approvals` với `decided_by` khác `submitted_by`.
- Staff bộ môn **tham gia** duyệt rubric COMMON liên ngành ⇒ 403 `RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT`.
- `ADMIN` trần gọi `approve` ⇒ 403 `RUBRIC_APPROVAL_ADMIN_NOT_ACADEMIC`.
- `reject` không kèm `reason` ⇒ 422. Có `reason` ⇒ 200, về `DRAFT`, sửa được lại.
- Approve mà không có `approvalReference` ⇒ 200, `rubrics.approval_reference` vẫn `NULL`, và scheme dùng rubric đó **không** publish được ở `IsDemo = false`.
- 🔴 Rubric legacy (`evaluation_scope IS NULL`) ⇒ `POST /publish` trực tiếp **vẫn 200** như trước. Không phá dữ liệu cũ.
- Rubric có `evaluation_scope` ⇒ `POST /publish` trực tiếp ⇒ 409 `RUBRIC_REQUIRES_APPROVAL_FLOW`.
- Audit: có `RUBRIC_SUBMITTED_FOR_APPROVAL` · `RUBRIC_APPROVED` · `RUBRIC_REJECTED` qua `IAuditTrail`.

### Kiểm chứng

```bash
dotnet test AIPMS.sln --filter "FullyQualifiedName~RubricApproval" --configuration Release
git grep -n "PENDING_APPROVAL" -- src db
git grep -n "ck_rubric_versions_status" -- db/schema.sql db/changes/20261030_add_rubric_approvals.sql
grep -c "20261030_add_rubric_approvals.sql" db/e2e/migrations.json   # kỳ vọng: 1
```

### File dự kiến

- `db/changes/20261030_add_rubric_approvals.sql` (mới) · `db/schema.sql` · `db/e2e/migrations.json`
- `src/AIPMS.Application/Features/Evaluations/Models/RubricModels.cs` (`RubricStatuses` thêm `PendingApproval`)
- `src/AIPMS.Application/Features/Evaluations/Services/RubricWorkflow.cs` (3 hàm mới + guard)
- `src/AIPMS.Api/Controllers/RubricsController.cs` (3 endpoint mới)
- `src/AIPMS.Infrastructure/Persistence/Repositories/RubricRepository.cs`
- `docs/contracts/.../rubric-approval-flow.json` (mới)
- `tests/.../RubricApprovalTests.cs` (mới)

---

### ✅ CHECKPOINT 2 — sau BE-06, BE-07, BE-08

Báo lại, và **nhớ: ba PR này chưa merge được.**

1. Ba PR đã mở, CI xanh, đã được review.
2. **Xác nhận bằng lệnh** là ba thứ sau vẫn chạy: rubric legacy đọc/chấm/publish được · `RubricHierarchyMigrationTests` xanh · `WeeklyAssessment` không hồi quy.
3. Xác nhận **không có chỗ nào** điền `approval_reference` bằng giá trị bịa.
4. Nói rõ đang chờ `Q-A10` và `Q-N02` — và `Rubric:RequiredLevelCount` hiện đặt bao nhiêu.

---

# PHASE 3 — ĐƯA LÊN HỢP ĐỒNG CHẤM

## BE-09 · HIGH · Người chấm không nhận được mô tả mức, chặng, và tỷ trọng thật

### Bằng chứng

Màn chấm của FE (`EvaluatorAssignmentDetailPage.tsx`) hiện nhận được, cho mỗi tiêu chí: tên · mô tả một dòng · **một** con số trọng số · ô điểm. Không có mô tả mức (chưa tồn tại trước `BE-06`), không có `Stage` ở header, và chỉ một trọng số nên không phân biệt được *trọng số trong bộ tiêu chí* với *tỷ trọng trong điểm sinh viên*.

```bash
git grep -c "isDemo\|approvalStatus" e8e9801c -- src/features/evaluations/pages/EvaluatorAssignmentDetailPage.tsx
# → 0
```

⇒ Giảng viên đang chấm **không thấy** scheme đã được phê duyệt hay chưa. Nhãn đó có ở `EvaluationSchemePolicySummary`, `ProjectResultBreakdown`, `StudentProjectResultPage` — nhưng **không có ở chỗ người ta thực sự nhập điểm**.

### Nghiệp vụ

Người chấm phải biết bốn thứ tại chỗ nhập điểm: tiêu chí này **nặng bao nhiêu trong điểm cuối của sinh viên** · mức nào ứng với điểm nào · đang chấm **chặng** nào (hồ sơ đã khóa, hay bảo vệ) · phiếu này có phải điểm chính thức hay chưa.

Không có chúng thì hai giảng viên chấm lệch nhau, và `RUBRIC_DESIGN_2026-10-30.md` §8 (phiếu chấm) không triển khai được.

### Cách xử lý đề xuất

Không thêm endpoint. Mở rộng DTO đã có.

**Trên từng item của draft / assignment detail:**

| Field mới | Nguồn | FE dùng để |
|---|---|---|
| `criterionCode` | `evaluation_criteria.code` | mã tiêu chí trên phiếu |
| `levels[]` | `rubric_criterion_levels` (`BE-06`) | hiện 4 mức cạnh ô điểm |
| `learningOutcome` | `rubric_criteria` (`BE-06`) | tooltip chuẩn đầu ra |
| `evidenceGuidance` | `rubric_criteria` (`BE-06`) | "chấm trên minh chứng nào" |
| `effectiveWeightPercent` | đã có ở `RubricCriterionDto:10` | trọng số trong bộ tiêu chí |

**Trên header của assignment detail:**

| Field mới | Nguồn | FE dùng để |
|---|---|---|
| `stage` | `SchemeComponent.Stage` | hiện "Chấm hồ sơ đã khóa" / "Bảo vệ" |
| `componentStudentWeightPercent` | `SchemeComponent.StudentWeightPercent` | **tỷ trọng của cả phiếu này trong điểm sinh viên** |
| `schemeApprovalStatus` | `SchemePolicy.ApprovalStatus` | nhãn DEMO / chính thức |
| `schemeIsDemo` | `SchemePolicy.IsDemo` | nhãn DEMO / chính thức |

**Hai quy tắc khi làm:**

1. 🔴 **BE phải gửi cả hai con số trọng số, không gộp, không nhân sẵn.** `effectiveWeightPercent` (trong bộ tiêu chí) và `componentStudentWeightPercent` (của cả phiếu) là hai thứ khác nhau — `N2`, `N4`. **Không** tính sẵn "tỷ trọng cuối của tiêu chí này trong điểm sinh viên" rồi gửi một số: nhân hai hệ số là chính cái `N4` cấm. FE hiển thị hai số cạnh nhau.
2. 🔴 **Tất cả field mới phải nullable và FE phải chịu được `null`.** Rubric legacy không có `levels` ⇒ `levels = []`. Phiếu legacy không có `stage` ⇒ `stage = null`. FE đã làm đúng việc này ở `evaluations.api.ts:105` (*"Preserve historical nulls; never silently backfill a stage"*) — đừng phá.

### Tiêu chí nghiệm thu

- `GET /evaluation-assignments/{id}` trả đủ 4 field header mới + 5 field item mới.
- Rubric có mức ⇒ `levels[]` đủ, đúng `levelOrder` tăng dần.
- 🔴 Rubric legacy không có mức ⇒ `levels = []`, **không lỗi**, vẫn chấm và chốt được.
- Phiếu legacy không có `stage` ⇒ `stage = null`, không bị backfill thành `"DEFENSE"`.
- `effectiveWeightPercent` và `componentStudentWeightPercent` là **hai field riêng**, không có field nào là tích của hai.
- Scheme `IsDemo = true` ⇒ `schemeIsDemo = true`, `schemeApprovalStatus = "DEMO"`.
- Example JSON đã nằm trong `docs/contracts/` kèm **cả hai** biến thể: rubric có mức và rubric legacy — `X3`, `X6`.
- Hành vi chấm, lưu nháp, chốt, 409, 422 **không đổi**.

### Kiểm chứng

```bash
dotnet test AIPMS.sln --filter "FullyQualifiedName~EvaluationDraft|FullyQualifiedName~EvaluatorAssignment" --configuration Release
ls docs/contracts/*/evaluator-assignment-*.json   # kỳ vọng: 2 file (có mức / legacy)
git grep -n "componentStudentWeightPercent\|effectiveWeightPercent" -- src/AIPMS.Application/Features/Evaluations
```

### File dự kiến

- `src/AIPMS.Application/Features/Evaluations/DTOs/` (DTO draft + assignment)
- `src/AIPMS.Infrastructure/Persistence/Repositories/EvaluationDraftRepository.cs`
- `docs/contracts/.../evaluator-assignment-with-levels.json` · `…-legacy.json` (mới)
- `tests/.../EvaluationDraftContractTests.cs`

---

# 2. CONTRACT CARD — BE PHẢI NỘP GÌ CHO FE

Theo `X1`, `X3`, `X5`, `X6`. Mỗi ticket đổi hợp đồng (`BE-05`, `BE-06`, `BE-07`, `BE-08`, `BE-09`) phải nộp **trước khi FE bắt đầu**:

1. **Example JSON** trong `docs/contracts/` theo đúng khuôn 37 file đã có: `{request: {method, path, headers, body}, response: {status, body}}`. **Không phải tài liệu văn xuôi** — prose không fail CI được (`X4`).
2. **Bảng mã trạng thái**: mỗi HTTP status mà endpoint trả, kèm `errorCode` và một câu "FE nên làm gì".
3. **Bảng field nullable**: mỗi field có thể `null`, kèm "null nghĩa là gì" và "FE hiển thị gì". `null` không bao giờ được render thành `0`.
4. **Tên test** chứng minh từng dòng ở mục 2 và 3.
5. **Biến thể legacy**: với `BE-06`/`BE-09`, nộp **hai** example — một có dữ liệu mới, một là hàng legacy.

Thiếu bất kỳ mục nào ⇒ chưa được merge. Đây là cách ngăn đúng cái lỗi `rubrics-api.ts` đã mắc: BE gửi `parentId` và `effectiveWeightPercent`, FE bỏ cả hai, không test nào thấy.

---

# 3. VERIFICATION TỔNG — chạy trước khi báo xong đợt

```bash
cd ai-pms-backend && git fetch -q

# 1. Baseline và trạng thái
git log -1 --format='BE %h %ci' origin/develop
gh pr list --repo aipms-sep490/ai-pms-backend --state open

# 2. BE-01 — authority template đã siết
git grep -n "RequireTemplateAuthority" -- src/AIPMS.Infrastructure/Services/Projects/ReviewGateService.cs
# kỳ vọng: gọi ở SaveTemplate và PublishTemplate

# 3. BE-02 / BE-03 — hai lưới an toàn
dotnet test AIPMS.sln --filter "FullyQualifiedName~ReviewGateServiceBehaviorTests" --configuration Release
dotnet test AIPMS.sln --filter "FullyQualifiedName~ElevenComponentScoring" --configuration Release
# kỳ vọng: Passed: 20 / Passed: 12, Failed: 0 cả hai

# 4. BE-06/07/08 — schema đã có
git grep -c "rubric_criterion_levels\|evaluation_scope\|PENDING_APPROVAL" -- db/schema.sql

# 5. Mọi migration mới đều có trong manifest
for f in $(git diff --name-only origin/develop...HEAD -- db/changes/); do
  n=$(basename "$f"); grep -q "$n" db/e2e/migrations.json && echo "OK  $n" || echo "THIẾU TRONG MANIFEST: $n"
done

# 6. Không còn exception thiếu errorCode trong vùng đã chạm
git grep -n "throw new ConflictException(\"" -- src/AIPMS.Application/Features/Evaluations | grep -v "," 
# kỳ vọng: rỗng (mọi ConflictException đều có tham số errorCode thứ hai)

# 7. Mọi endpoint mới đều có example JSON
git diff --name-only origin/develop...HEAD -- src/AIPMS.Api/Controllers/
ls docs/contracts/*/

# 8. Build và test toàn bộ
dotnet build AIPMS.sln --configuration Release -warnaserror
dotnet test AIPMS.sln --configuration Release --no-build
```

**Không làm được, cần người khác:**

- **`BROWSER_VERIFIED`**: toàn bộ file này ở mức `VERIFIED_SOURCE` + `CI_VERIFIED`. Chưa ai chạy app từ baseline này. Mọi mô tả hành vi runtime là **suy ra từ source**. Gặp chỗ app làm khác source thì **app đúng** — báo lại, đừng tự sửa tài liệu.
- **Chữ ký bộ môn** cho `Q-A10` và `Q-N02`: không có thì `BE-06`/`BE-07`/`BE-08` mở PR được nhưng không merge được.
- **Nội dung 9 rubric** (`Q-D13`, `Q-N09`, `Q-N11`) và **ngưỡng đạt** (`Q-A9`): là dữ liệu bộ môn nhập, không phải code.
