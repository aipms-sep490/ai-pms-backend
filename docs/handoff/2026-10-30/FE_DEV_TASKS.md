# NHIỆM VỤ FRONTEND — AI-PMS · giao ngày 30/10/2026

> **Đọc hết mục 0 trước khi sửa dòng nào.** File này được viết để một AI coding agent thực thi được mà không cần hỏi lại. Mỗi task có: bằng chứng (file:dòng + code thật), nghiệp vụ, cách sửa, bảng trạng thái UI, tiêu chí nghiệm thu, cách kiểm chứng, file dự kiến.
>
> **Mục tiêu đợt này: hệ thống chạy thật được một kỳ.** Không làm bản demo dùng một lần.
>
> **Tin tốt: sáu ticket đầu (`F1`…`F6`) không chờ BE gì cả.** Bắt đầu được ngay hôm nay. Xem mục 0.10.

---

## 0. NGỮ CẢNH BẮT BUỘC ĐỌC

### 0.1 Dự án là gì

AI-PMS là hệ thống **quản trị đồ án học thuật** cho một trường đại học, **không phải** task manager. Hai phương thức đồ án dùng **chung một lifecycle**:

- `SINGLE_MAJOR` — một ngành, một GVHD chính (PRIMARY).
- `INTERDISCIPLINARY` — liên ngành, một GVHD chính **cộng** một Mentor cho **mỗi ngành** (`DISCIPLINE_MENTOR`).

Vai trò: `STUDENT` (Leader / Member) · `LECTURER` (Supervisor / Mentor / Evaluator) · `DEPARTMENT_STAFF` · `ADMIN`.

### 0.2 Luật tối cao của FE trong dự án này

**BE là authority cuối cùng** về quyền, phạm vi, trạng thái, điểm, lưu trữ. FE **fail-closed**: chỉ hiển thị cái BE cho phép.

| | |
|---|---|
| ❌ | Không bao giờ "nới quyền ở FE cho chạy được". BE chặn ⇒ hoặc guard đúng, hoặc là lỗi BE → **báo lên**, không vá ở FE |
| ❌ | **Không tự tính con số nghiệp vụ.** Không tự suy công thức, không tự cộng trọng số, không tự quyết trạng thái hợp lệ |
| ❌ | Không biến policy `PROPOSED`/`OPEN` thành chính thức bằng code FE |
| ❌ | `null` **không bao giờ** được render thành `0` |
| ❌ | **Cờ feature chỉ bật/tắt tính năng, KHÔNG dùng làm cờ quyền ghi** — đây là nguyên nhân của `F5` |
| ✅ | Đọc `capabilities` / `errorCode` do BE trả và hiển thị đúng |
| ✅ | Giữ nháp của người dùng khi 409 |

PR #113 của repo đã sửa đúng hướng — *"display server result formulas without assumed 50/50 weights"*. Giữ nguyên tắc đó.

### 0.3 Repo và baseline

| | |
|---|---|
| Repo | `https://github.com/aipms-sep490/ai-pms-frontend` |
| Nhánh gốc | `develop` |
| Baseline SHA | `e8e9801c401bc810ce19299748c332af4f36aec0` (CI xanh) |
| PR đang mở | **không có** |

Quy mô hiện tại: 83 page · 137 route entry · bộ primitive dùng chung ở `src/components/ui/`.

> ⚠️ **Dọn trước khi bắt đầu:** máy đang có **6 worktree FE**, **3 trong số đó ở detached HEAD**. Kiểm `git worktree list` và dọn, nếu không sẽ sửa file ở nhánh mình không nghĩ là đang ở.

### 0.4 Quy tắc làm việc — `AGENTS.md` của repo

Đọc `AGENTS.md` ở gốc repo. Trích phần bắt buộc:

1. **Mỗi task = một nhánh mới từ `develop` đã cập nhật = một PR.** Không code/commit trực tiếp trên `develop`.
2. Trước khi sửa file: `git status`, xác định nhánh và commit gốc, `git fetch`. **Quan trọng:** chạy `git log origin/develop..develop --oneline` — repo này từng có commit local chưa push; branch off mà không kiểm thì PR bị lẫn commit lạ.
3. Chỉ cập nhật `develop` bằng fast-forward khi an toàn. **Không** reset/stash/ghi đè thay đổi của người khác để ép checkout sạch.
4. Task trước chưa merge **không** phải lý do dùng tiếp nhánh cũ. Có phụ thuộc thì ghi `depends on #<PR>` trong mô tả PR.
5. Khi bắt đầu task: báo tên nhánh + baseline. Khi bàn giao: ghi validation đã chạy + trạng thái commit/push/PR.

### 0.5 Lệnh kiểm chứng — và một cái bẫy môi trường

```bash
npm ci
npm run typecheck     # tsc -b --pretty false
npm run lint          # oxlint
npm run test          # vitest run
npm run build         # tsc -b && vite build
```

> # ⚠️ `npm run dev` KHÔNG CÓ BACKEND
>
> `vite.config.ts:10`:
> ```ts
> const proxyTarget = process.env.VITE_API_PROXY_TARGET?.trim()
>   || configEnv.VITE_API_PROXY_TARGET?.trim()
>   || 'http://localhost:5080'
> ```
> Không set biến ⇒ proxy **lặng lẽ** trỏ về `http://localhost:5080`, và **không có gì lắng nghe ở đó**. Mọi request trượt, nhưng không có thông báo nào nói tại sao.
>
> **Phải set trước khi `npm run dev`:**
> ```bash
> VITE_API_PROXY_TARGET=https://api-staging.khaidz.com npm run dev
> ```
> Đừng mất buổi chiều debug một lỗi không tồn tại.

### 0.6 🔁 MƯỜI BỐN BẤT BIẾN NGHIỆP VỤ — khối bắt buộc, giống hệt bản của BE

> ⚠️ Khối này là **bản sao bắt buộc giống nhau từng chữ** với mục 0.5 của `BE_DEV_TASKS.md`. Sửa bên này phải sửa bên kia.
>
> FE không enforce chúng — BE enforce. Nhưng FE **phải hiển thị đúng theo chúng**, nếu không người dùng sẽ bị dẫn sai.

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

**Bốn thứ tuyệt đối không làm:** phát triển tính năng AI trong sản phẩm · hiển thị số văn bản phê duyệt mà BE không gửi · cho sửa rubric đã publish · hiển thị thành công khi backend chưa xác nhận.

### 0.7 Nơi đặt code — theo mẫu có sẵn

| Đặt gì | Ở đâu | Mẫu để theo |
|---|---|---|
| Trang theo domain | `src/features/<domain>/pages/` | `EvaluatorAssignmentDetailPage.tsx` |
| API client theo domain | `src/services/api/<domain>.api.ts` hoặc `src/features/<domain>/api/` | `evaluations.api.ts` · `rubrics-api.ts` |
| Type khớp DTO của BE | `src/features/<domain>/*-types.ts` | `evaluation-types.ts` |
| Primitive dùng chung | `src/components/ui/` | `WorkspacePage` · `Button` · `InlineAlert` · `DocumentPreviewModal` |
| Route + guard | `src/app/router/index.tsx` · `workspace-route-registry.ts` · `*Route.tsx` | `EvaluatorAssignmentRoute.tsx` |
| Token giao diện | `src/index.css` · `src/motion/motion.css` | `design-system/ai-pms/MASTER.md` |
| Hướng dẫn | `src/features/onboarding/` | `TourProvider` · `tour-scripts.ts` |

**Bảy quy tắc FE bắt buộc:**

1. **Không tự tính con số nghiệp vụ.** BE gửi gì hiện cái đó.
2. **Không tự suy quyền.** Ẩn/hiện theo `/projects/{id}/my-capabilities`. `disabled` trên UI **không phải** bảo mật.
3. **409 không được mất input.** `EvaluatorAssignmentDetailPage.tsx:128,160` đã làm đúng — nhân rộng, đừng làm khác.
4. **`null` không render thành `0`.**
5. **Cờ feature chỉ bật/tắt tính năng, KHÔNG dùng làm cờ quyền ghi.** Xem `F5`.
6. **Không viết fixture tay** cho endpoint đã có example JSON — dùng `docs/contracts/` của BE. Xem `X4`.
7. **Không tạo bộ primitive thứ hai.** Audit `src/components/ui/` trước khi thêm component mới.

### 0.8 🔁 MƯỜI QUY TẮC CHỐNG NHẦM LẪN BE ↔ FE — khối bắt buộc, giống hệt bản của BE

> ⚠️ Khối này là **bản sao bắt buộc giống nhau từng chữ** với mục 0.7 của `BE_DEV_TASKS.md`. Sửa bên này phải sửa bên kia.
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

### 0.9 KHÔNG ĐƯỢC LÀM trong đợt này

| Việc | Vì sao hoãn |
|---|---|
| **REQ-28 Dark Mode** | ```git grep -c "prefers-color-scheme\|data-theme\|\[data-mode" e8e9801c -- src/index.css``` ⇒ **0**. **Chưa có một token nào.** Làm dark mode nghĩa là định nghĩa lại toàn bộ bảng màu — một đợt riêng. P3, opt-in, chờ PO duyệt |
| **REQ-30 Office preview inline** | BE **không có converter nào**. `DocumentPreviewModal.tsx:14-36` hiện cho pdf/ảnh/text inline, Office thì tải về — đó là hành vi đúng cho đến khi có quyết định converter + bảo mật + bản quyền |
| **REQ-32 annotation sâu (ghim toạ độ, thread)** | BE **chỉ có** `task_comments`. Không có bảng annotation nào. Domain mới hoàn toàn, chờ `Q29` |
| **Năm route mới của WF-14 · WF-15 · WF-17 · WF-20 · WF-27** | Cả năm phụ thuộc feature chưa tồn tại ở BE (announcement, minutes, help center, theme). Dựng vỏ UI trước là tạo trang chết |
| **Hậu-Review: PASS/PENDING/FAIL, correction center** | BE Làn 0, chờ bộ môn (`Q7` · `Q9–Q12` · `Q16–Q18`). FE dựng UI cho một API chưa có hình dáng là làm hai lần |

Thấy việc nào trong bảng này "làm tạm cũng được" thì **đừng**. Viết câu hỏi, gửi lên.

### 0.10 Phụ thuộc BE — biết trước để không bị kẹt

| Ticket | Chờ BE? | Chờ cái gì |
|---|---|---|
| `F1` · `F2` · `F3` · `F4` · `F5` · `F6` | **KHÔNG** | BE **đã gửi đủ dữ liệu** cho cả sáu. Đây là sáu lỗi tích hợp phía FE, không phải tính năng thiếu |
| `F7` | ✅ | `BE-05` — endpoint `GET /v1/upload-policies` |
| `F8` | ✅ 🔴 | `BE-06` + `BE-07` + `BE-08` — và ba cái đó **chờ bộ môn ký**, nên `F8` cũng **không merge được** trước khi có chữ ký |
| `F9` | ✅ | `BE-09` — descriptor + stage + hai trọng số trên draft DTO |
| `F10` | **KHÔNG** | thuần FE |

**Sáu ticket đầu không cần BE làm gì.** Nếu bị nói "chờ BE" ở `F1`–`F6` thì đọc lại mục bằng chứng của từng ticket: dữ liệu đã có trên dây, FE đang bỏ đi hoặc tự khóa.

---

## 1. THỨ TỰ THỰC HIỆN

```
PHASE 0 — SÁU VIỆC KHÔNG CHỜ AI  (làm song song được, độc lập nhau)
  F1  cho chọn Stage khi cấu hình component scheme    🔴 BLOCKER
  F2  nhận parentId + effectiveWeightPercent, hai trọng số
  F3  validate trọng số theo từng nhóm sibling
  F4  nhãn trạng thái phê duyệt trên MÀN CHẤM
  F5  mở quyết định phạm vi liên ngành ở prod         🔴 BLOCKER nghiệp vụ
  F6  gọi prefill báo cáo tiến độ
        ▼
  ✅ CHECKPOINT 0
        │
PHASE 1 — SAU KHI BE XONG
  F7  đọc policy upload từ BE, bỏ hardcode 20 MiB     ← BE-05
  F8  Rubric Builder: 4 mức, reorder, gửi duyệt       ← BE-06/07/08  🔴 không merge
  F9  màn chấm: mức + Stage + hai trọng số            ← BE-09
        ▼
  ✅ CHECKPOINT 1
        │
PHASE 2 — HOÀN THIỆN
  F10 tour: sidebar-primary chết, Skip ≠ Completed
  F11 hạ tầng contract-drift                          (X4)
```

**Ưu tiên nếu phải chọn:** `F1` trước — không có nó thì bộ môn **không dựng được** scheme 11 component, và cả kỳ không chấm được. Rồi `F5` — nó đang chặn dự án liên ngành đạt trạng thái sẵn sàng bảo vệ.

---

# PHASE 0 — SÁU VIỆC KHÔNG CHỜ AI

# TASK F1 · 🔴 BLOCKER · Không có chỗ nào chọn Stage, nên không dựng được scheme 11 component

### Bằng chứng — dữ liệu đã có trên dây, UI không có ô nhập

Type đã khai `Stage`. `src/features/evaluations/evaluation-types.ts:16`:

```ts
export type EvaluationStage = 'ON_GOING' | 'COLD' | 'DEFENSE' | (string & {})
```

Dòng 26 — component đã mang `stage`. API client truyền **hai chiều**, `src/services/api/evaluations.api.ts:50` và `:106`. Và `:105` có comment rất đúng:

```ts
// Preserve historical nulls; never silently backfill a stage while saving other fields.
```

Nhưng:

```bash
git grep -n "EvaluationStage" e8e9801c -- "src/**/*.tsx"
# → RỖNG
```

**Không một file `.tsx` nào** tham chiếu `EvaluationStage`. Toàn bộ chỗ dùng `item.stage` trong trang cấu hình scheme là **đúng một dòng**, và nó là validation chứ không phải input — `src/features/evaluations/EvaluationSchemeManagementPage.tsx:184`:

```ts
if (item.projectWeightPercent < 0 || ... || (item.projectWeightPercent + item.studentWeightPercent === 0 && item.stage !== 'COLD')) issues.push(`Thành phần ${row} có trọng số không hợp lệ.`)
```

⇒ Trang biết `stage` tồn tại (đủ để nới luật trọng số cho component COLD), nhưng **người dùng không có cách nào đặt nó**. Mọi component tạo từ UI đều có `stage` là `null` hoặc giữ nguyên giá trị cũ.

### Nghiệp vụ

Thiết kế chấm điểm của đợt này (`RUBRIC_DESIGN_2026-10-30.md` §6.2) cần **11 component** cho dự án liên ngành, phân theo hai chặng:

| Chặng | Nghĩa | Số component |
|---|---|---|
| `COLD` | chấm hồ sơ **đã khóa**, trước buổi bảo vệ | 4 |
| `DEFENSE` | chấm tại buổi bảo vệ | 7 |

Theo `N3`, `Stage` và `Scope` là **hai trục độc lập**: một component = một ô (Stage × Scope × Major). Không chọn được Stage ⇒ không khai được ô ⇒ **không dựng được cấu trúc điểm của cả kỳ**. Đây là lý do ticket này là chặn tuyệt đối.

`ON_GOING` **có trong enum nhưng không dùng trong đợt này** — xem `N10`: theo dõi trong quá trình là việc của `WeeklyAssessments`, không phải một chặng chấm chính thức. Vẫn cho chọn (BE nhận), nhưng kèm chú thích cảnh báo.

### Cách sửa đề xuất

1. Trong form cấu hình component của `EvaluationSchemeManagementPage.tsx`, thêm `<select>` **Giai đoạn** cạnh ô Phạm vi:
   - `COLD` → nhãn **"Chấm hồ sơ đã khóa"**
   - `DEFENSE` → nhãn **"Bảo vệ"**
   - `ON_GOING` → nhãn **"Đánh giá quá trình"** + chú thích nhỏ: *"Không dùng cho điểm chính thức ở kỳ này."*
   - Dùng đúng nhãn tiếng Việt đã có trong repo — `EvaluatorAssignmentDetailPage.tsx:164` đã map `{ DEFENSE: 'Bảo vệ', COLD: 'Chấm báo cáo', ON_GOING: 'Đánh giá quá trình' }`. **Tốt hơn: tách map đó ra một hằng dùng chung** thay vì khai lần thứ hai.
2. 🔴 **Component cũ có `stage === null` phải giữ `null`.** Select hiện placeholder *"Chưa xác định"*, và nếu người dùng **không chạm vào** thì payload gửi lên vẫn `null`. Đây là điều mà comment ở `evaluations.api.ts:105` đang bảo vệ — đừng phá.
3. Hiển thị bảng tổng kết component **nhóm theo Stage**, kèm tổng `studentWeightPercent` của từng chặng, để bộ môn tự kiểm được COLD 30 / DEFENSE 70.
4. 🔴 **Không tự tính, không tự validate tổng 100.** Dòng `:187` hiện đã kiểm tổng `projectWeight` — giữ nguyên, đừng thêm luật mới. Tổng hợp lệ là việc BE quyết (`VersionedResultCalculator.cs:64`); FE chỉ hiện con số để người ta thấy.

### Bảng trạng thái UI

| Trạng thái | Hiển thị |
|---|---|
| Tạo component mới | select chưa chọn, placeholder "Chưa xác định" |
| Component cũ `stage = null` | "Chưa xác định", **không** tự đổi thành `DEFENSE` |
| Component `stage = "COLD"` | "Chấm hồ sơ đã khóa" |
| Scheme đã publish (readonly) | select `disabled`, hiện nhãn, không hiện placeholder |
| `stage` là giá trị lạ BE gửi về | hiện nguyên chuỗi, **không** crash — type đã có `(string & {})` để đỡ việc này |
| 422 từ BE | hiện lỗi tại đúng dòng component, không toast chung |
| 409 | giữ nguyên form, hiện banner đối chiếu |

### Tiêu chí nghiệm thu

- Dựng được scheme **11 component** từ UI, mỗi component chọn được Stage + Scope + Major.
- Component cũ `stage = null`: mở trang, sửa **tên** component rồi lưu ⇒ payload `stage` vẫn `null`.
- Bảng tổng kết hiện tổng `studentWeightPercent` theo từng Stage.
- Scheme đã publish ⇒ select readonly.
- `npm run typecheck` và `npm run lint` xanh.
- Có test: render form, chọn `COLD`, submit ⇒ payload chứa `stage: 'COLD'`; không chạm select ⇒ payload giữ `null`.

### Kiểm chứng

```bash
git grep -n "EvaluationStage" -- "src/**/*.tsx"      # kỳ vọng: CÓ (trước khi sửa là rỗng)
npm run typecheck && npm run lint && npm run test
# chạy thật:
VITE_API_PROXY_TARGET=https://api-staging.khaidz.com npm run dev
```

### File dự kiến

- `src/features/evaluations/EvaluationSchemeManagementPage.tsx`
- `src/features/evaluations/evaluation-types.ts` (hằng nhãn dùng chung)
- `src/features/evaluations/__tests__/EvaluationSchemeStage.test.tsx` (mới)

**Liên quan:** WF-25 · FL-02 · REQ-25

---

# TASK F2 · HIGH · FE bỏ hai trường BE đã gửi, nên người chấm không thấy tỷ trọng thật

### Bằng chứng

`src/features/evaluations/rubrics-api.ts:4-7` — toàn bộ type:

```ts
export interface RubricCriterion {
  id: number; name: string; description: string | null; weightPercent: number; maxScore: number | null
  sortOrder: number; isRequired: boolean; children: RubricCriterion[]
}
```

Còn BE đang gửi — `ai-pms-backend/src/AIPMS.Application/Features/Evaluations/DTOs/RubricDtos.cs:6-12`:

```csharp
public sealed record RubricCriterionDto(long Id, long CriterionId, string Name, string? Description,
    decimal WeightPercent, decimal? MaxScore, int SortOrder, bool IsRequired)
{
    public long? ParentId { get; init; }
    public decimal EffectiveWeightPercent { get; init; }
    public IReadOnlyList<RubricCriterionDto> Children { get; init; } = [];
}
```

⇒ **FE bỏ cả `parentId` và `effectiveWeightPercent`.** Dữ liệu đã nằm trên dây, FE không đọc. Và không một test nào phát hiện — đây đúng là lỗi mà `X3` được viết ra để ngăn.

### Nghiệp vụ

Một tiêu chí có **hai** con số trọng số khác nhau, và người chấm cần cả hai:

| Con số | Nghĩa | Ví dụ |
|---|---|---|
| `weightPercent` | trọng số **trong nhóm cùng cấp** | tiêu chí này 35% của bộ tiêu chí |
| `effectiveWeightPercent` | trọng số **thật trong cả bộ tiêu chí**, sau khi nhân qua các cấp cha | nếu cha nó chiếm 40% thì effective = 14% |

Hiện FE chỉ hiện con số thứ nhất, nên trong rubric có cây hai cấp, giảng viên **đọc sai mức quan trọng của tiêu chí**. Với rubric `CMN-DEF-2026A` chẳng hạn, tiêu chí nặng nhất chiếm 12,25% điểm cuối — con số đó không hiện ra được ở trạng thái hiện tại.

`parentId` thì cần để dựng cây đúng và để biết tiêu chí nào là lá (chỉ lá mới chấm — `N5`).

### Cách sửa đề xuất

1. Thêm `parentId: number | null` và `effectiveWeightPercent: number` vào `RubricCriterion`.
2. Hiện **hai con số cạnh nhau** ở mọi chỗ render tiêu chí, với nhãn phân biệt rõ:
   - `trong bộ tiêu chí: 35%`
   - `tỷ trọng hiệu lực: 14%`
   - Nhãn một chữ "trọng số" là không đủ — đó chính là lỗi hiện tại ở màn chấm (`EvaluatorAssignmentDetailPage.tsx:174`).
3. 🔴 **Không tự tính `effectiveWeightPercent`.** BE đã tính qua `RubricHierarchy.WithEffectiveWeights`. FE nhân lại là vi phạm quy tắc 1 mục 0.7 và sẽ lệch khi BE đổi cách làm tròn.
4. Dùng `parentId` để render thụt lề cây và để đánh dấu tiêu chí lá.
5. 🔴 **Chịu được dữ liệu cũ:** rubric phẳng một cấp ⇒ `parentId = null` ở mọi tiêu chí và `effectiveWeightPercent === weightPercent`. Không được hiện hai số giống nhau hai lần gây rối — khi bằng nhau thì chỉ hiện một.

### Bảng trạng thái UI

| Trạng thái | Hiển thị |
|---|---|
| Rubric phẳng (effective == weight) | một con số |
| Rubric có cây (effective ≠ weight) | hai con số, nhãn khác nhau |
| `effectiveWeightPercent` BE không gửi (API cũ) | hiện `weightPercent`, **không** hiện `0` hay `NaN` — `N6` |
| Tiêu chí cha | không hiện `maxScore`, không hiện "bắt buộc" — `N5` |

### Tiêu chí nghiệm thu

- Rubric hai cấp: tiêu chí con hiện đúng **cả hai** con số, và `effectiveWeightPercent` khớp đúng giá trị BE trả (so bằng response thật, không bằng tính tay).
- Rubric phẳng: hiện một con số, không hiện trùng.
- Cây render đúng theo `parentId`, thụt lề đúng cấp.
- Không có chỗ nào trong FE nhân `weightPercent` với nhau.
- Có test với fixture **lấy từ `docs/contracts/` của BE**, không viết tay — `X4`.

### Kiểm chứng

```bash
git grep -n "effectiveWeightPercent\|parentId" -- src/features/evaluations/rubrics-api.ts   # kỳ vọng: CÓ
git grep -rn "weightPercent \*" -- src    # kỳ vọng: RỖNG (không tự nhân trọng số)
npm run typecheck && npm run test
```

### File dự kiến

- `src/features/evaluations/rubrics-api.ts:4-7`
- `src/features/evaluations/RubricManagementPage.tsx` (`CriterionEditor` `:164`, `CriterionReadView` `:177`)
- `src/features/evaluations/__tests__/rubric-effective-weight.test.tsx` (mới)

**Liên quan:** WF-23 · FL-18 · REQ-24

---

# TASK F3 · HIGH · Cảnh báo trọng số chỉ tính cấp 1, nên người dùng nhận 409 không biết sai đâu

### Bằng chứng

FE chỉ cộng cấp cao nhất. `src/features/evaluations/RubricManagementPage.tsx:147-148`:

```ts
const topLevelWeightSum = criteria.reduce((sum, item) => sum + (Number(item.weightPercent) || 0), 0)
const isWeight100 = Math.abs(topLevelWeightSum - 100) < 0.01
```

Và chính giao diện đã **hứa** điều mạnh hơn thế — `:155` (trích):

> *"Trọng số của các tiêu chí cùng cấp phải bằng 100% trước khi công bố."*

Banner hiện: *"Tổng trọng số tiêu chí chính: X% / 100%"* — chỉ tiêu chí **chính**.

Còn BE validate **từng nhóm cùng cấp** — `RubricRules.cs:26`:

```csharp
|| criteria.GroupBy(c => c.ParentId).Any(group => group.Sum(c => c.WeightPercent) != 100m
    || group.Select(c => c.SortOrder).Distinct().Count() != group.Count())
```

⇒ Người dùng có cấp 1 tổng đúng 100 (banner **xanh**), nhưng một nhóm con tổng 90 ⇒ bấm lưu ⇒ BE từ chối, và FE **không chỉ được nhóm nào sai**.

### Nghiệp vụ

Rubric của đợt này có cây hai cấp ở nhiều bộ. Soạn 38 tiêu chí mà mỗi lần sai một nhóm con lại nhận lỗi không rõ chỗ thì bộ môn sẽ bỏ, và cả thiết kế chấm điểm không triển khai được. Đây là ticket về tính dùng được, nhưng nó chặn việc vận hành thật.

### Cách sửa đề xuất

1. Thay `topLevelWeightSum` bằng hàm duyệt cây, trả về **danh sách nhóm sai**:
   ```ts
   type WeightIssue = { parentPath: number[]; parentName: string | null; sum: number }
   ```
   `parentName === null` ⇒ nhóm gốc.
2. Banner liệt kê **từng nhóm sai, kèm tên tiêu chí cha và số thiếu/vượt**. Nhóm đúng thì không nhắc.
3. Highlight **đúng** `CriterionEditor` của nhóm sai (`:164`), không highlight cả form.
4. Kiểm luôn `sortOrder` trùng trong cùng nhóm — BE cũng từ chối việc đó ở cùng dòng `:26`, và hiện FE không kiểm.
5. 🔴 **Validate ở client là để hiện lỗi sớm, KHÔNG phải để thay BE.** Vẫn gửi request; BE vẫn là người quyết. Nếu BE trả 422 với nội dung khác client dự đoán thì **hiện nội dung của BE**.
6. Dùng cùng epsilon với code hiện tại: `< 0.01`. Đừng đổi thành so sánh bằng — `weightPercent` là `DECIMAL(5,2)`.

### Bảng trạng thái UI

| Trạng thái | Hiển thị |
|---|---|
| Mọi nhóm = 100 | banner xanh "Tất cả nhóm tiêu chí đã đủ 100%" |
| Nhóm gốc sai | "Nhóm tiêu chí chính: 90% — còn thiếu 10%" |
| Một nhóm con sai | "Nhóm con của *Tích hợp liên ngành*: 110% — đang vượt 10%" |
| Nhiều nhóm sai | liệt kê **tất cả**, không chỉ cái đầu |
| `sortOrder` trùng | "Nhóm con của *X* có hai tiêu chí cùng thứ tự" |
| Rubric readonly | không hiện banner validate |
| BE trả 422 khác dự đoán | hiện **nội dung của BE**, không hiện dự đoán của client |

### Tiêu chí nghiệm thu

- Cấp 1 đúng 100, một nhóm con 90 ⇒ banner **đỏ**, chỉ đúng tên nhóm con, nêu thiếu 10%.
- Ba nhóm sai ⇒ liệt kê cả ba.
- Mọi nhóm đúng ⇒ banner xanh, lưu được.
- `sortOrder` trùng trong một nhóm ⇒ báo trước khi gọi API.
- Có test cho cây 3 cấp.

### Kiểm chứng

```bash
git grep -n "topLevelWeightSum" -- src/features/evaluations/RubricManagementPage.tsx   # kỳ vọng: RỖNG sau khi sửa
npm run test -- rubric-weight
```

### File dự kiến

- `src/features/evaluations/RubricManagementPage.tsx:147-148` · `:155` (banner) · `:164` (`CriterionEditor`)
- `src/features/evaluations/__tests__/rubric-weight-groups.test.tsx` (mới)

**Liên quan:** WF-23 · FL-18 · REQ-24

---

# TASK F4 · HIGH · Màn chấm không có nhãn nào nói đây là điểm chính thức hay chưa

### Bằng chứng

```bash
git grep -c "isDemo\|approvalStatus" e8e9801c -- src/features/evaluations/pages/EvaluatorAssignmentDetailPage.tsx
# → 0
```

Nhãn đó **có** ở ba nơi khác: `EvaluationSchemePolicySummary`, `ProjectResultBreakdown`, `StudentProjectResultPage`.

Nhưng header của màn chấm — `EvaluatorAssignmentDetailPage.tsx:156` — chỉ có ba dòng:

```
Đồ án: … | Bộ tiêu chí: … | Phạm vi: …
```

Không có trạng thái phê duyệt. Không có cả `Stage` (việc đó là `F9`).

⇒ **Giảng viên đang nhập điểm không biết phiếu mình chấm có phải điểm chính thức hay không.**

### Nghiệp vụ

`N7`: `IsDemo = true` nghĩa bộ tiêu chí **chưa được phê duyệt bằng văn bản**, điểm chấm ra chưa phải điểm chính thức. BE chặn cứng không cho công bố (`VersionedResultCalculator.cs:17`), nên không có rủi ro dữ liệu — nhưng có rủi ro **con người**: giảng viên tưởng mình đang cho điểm thật, hoặc ngược lại, chấm hời vì tưởng là thử.

Chỗ cần nhãn nhất là chỗ người ta **thực sự nhập điểm**, không phải trang kết quả mà ít ai mở.

### Cách sửa đề xuất

1. Đọc `isDemo` / `approvalStatus` từ assignment detail. **Nếu BE chưa gửi** (sẽ có ở `BE-09`) thì đọc từ scheme policy đã có sẵn trong `evaluations.api.ts` — đừng chờ `BE-09`, ticket này không phụ thuộc nó.
2. Thêm một banner **cố định trên form chấm**, không phải toast:
   - `isDemo === true` ⇒ 🟡 **"Bộ tiêu chí chưa được phê duyệt bằng văn bản. Điểm chấm ở đây chưa phải điểm chính thức."**
   - `approvalStatus === "APPROVED"` ⇒ 🟢 **"Bộ tiêu chí đã được phê duyệt. Đây là điểm chính thức."**
   - không rõ ⇒ ⚪ **"Chưa xác định trạng thái phê duyệt."** 🔴 **Không mặc định là "chính thức"** — fail-closed.
3. 🔴 **Không hiện số văn bản phê duyệt nếu BE không gửi.** Không bao giờ bịa, không bao giờ hiện placeholder kiểu "QĐ số …".
4. Dùng `InlineAlert` có sẵn trong `src/components/ui/`, đừng tạo component nhãn thứ hai (quy tắc 7).
5. Nhãn phải thấy được **mà không cần cuộn** khi form chấm dài — đây là lý do nó đi vào header chứ không chèn giữa danh sách tiêu chí.

### Bảng trạng thái UI

| `isDemo` | `approvalStatus` | Hiển thị |
|---|---|---|
| `true` | `"DEMO"` | 🟡 chưa phê duyệt, chưa chính thức |
| `false` | `"APPROVED"` | 🟢 đã phê duyệt, chính thức |
| `null`/thiếu | bất kỳ | ⚪ chưa xác định — **không** mặc định chính thức |
| bất kỳ | `"APPROVED"` nhưng không có số văn bản | 🟡 — theo `N7`, thiếu `ApprovalReference` thì BE không coi là chính thức |
| phiếu `FINALIZED` | — | vẫn hiện nhãn, readonly |

### Tiêu chí nghiệm thu

- Mở màn chấm với scheme `IsDemo = true` ⇒ banner vàng hiện **trước** danh sách tiêu chí, thấy ngay không cần cuộn.
- Scheme `APPROVED` + có số văn bản ⇒ banner xanh.
- BE không gửi thông tin ⇒ banner trắng "chưa xác định", **không** xanh.
- Không có chỗ nào trong FE hiện chuỗi số văn bản mà BE không gửi.
- Phiếu đã chốt vẫn hiện nhãn.
- Không thêm component nhãn mới ngoài `InlineAlert`.

### Kiểm chứng

```bash
git grep -c "isDemo\|approvalStatus" -- src/features/evaluations/pages/EvaluatorAssignmentDetailPage.tsx   # kỳ vọng: > 0
npm run test -- evaluator-approval-label
```

### File dự kiến

- `src/features/evaluations/pages/EvaluatorAssignmentDetailPage.tsx:155-157` (header)
- `src/features/evaluations/evaluation-types.ts`
- `src/features/evaluations/__tests__/evaluator-approval-label.test.tsx` (mới)

**Liên quan:** WF-11 / WF-24 · FL-19/20/21 · REQ-11

---

# TASK F5 · 🔴 BLOCKER NGHIỆP VỤ · Dự án liên ngành không bao giờ đạt sẵn sàng bảo vệ ở production

### Bằng chứng — cờ dev dùng làm cờ quyền ghi

`src/app/config/env.ts:94`:

```ts
reviewGatesMockEnabled: !import.meta.env.PROD && import.meta.env.VITE_USE_MOCK_REVIEW_GATES?.trim().toLowerCase() === 'true',
```

Chú ý `!import.meta.env.PROD` — **ở bản production, cờ này LUÔN LUÔN `false`**, không cách nào bật.

Và control quyết định phạm vi liên ngành bị khóa sau đúng cờ đó. `src/features/review-gates/components/InterdisciplinaryGatePanel.tsx:54`:

```ts
if (!projectId || !env.reviewGatesMockEnabled || busyScope) return
```

`:167`:

```tsx
disabled={!env.reviewGatesMockEnabled || busyScope !== null}
```

Trong khi BE **đã sẵn sàng hoàn toàn** — `ReviewGateService.cs` `UpdateScope` có đủ guard ở `:486-499`, và FE cũng đã có hàm gọi: `src/features/review-gates/api/review-gates-api.ts:59-66` `updateHybridScopeDecision`.

Mà `DefenseReadiness` lại **đòi** những quyết định đó — `ReviewGateService.cs:517-541`, điều kiện `decided >= majorCount + 1`.

⇒ **Ở production: không ai bấm được nút, nên quyết định phạm vi không bao giờ có, nên dự án liên ngành không bao giờ đạt sẵn sàng bảo vệ.** BE đúng, API đúng, hàm gọi có — chỉ một dòng cờ dev khóa cả luồng.

### Nghiệp vụ

Dự án liên ngành phải chốt được phạm vi của từng mốc Review (ngành nào nộp gì, ai thẩm định phần nào) trước khi được vào bảo vệ. Đây là một quyết định học thuật thật, và BE đã có đủ phân quyền cho nó.

Quy tắc 5 mục 0.7 sinh ra từ chính lỗi này: **cờ feature chỉ bật/tắt tính năng, không dùng làm cờ quyền ghi.** Ở đây một cờ chọn-nguồn-dữ-liệu (mock vs thật) bị dùng kiêm luôn việc khóa quyền ghi.

### Cách sửa đề xuất

1. **Tách hai khái niệm.** `reviewGatesMockEnabled` chỉ còn một việc: chọn nguồn dữ liệu khi dev. Nó **không** được xuất hiện trong bất kỳ biểu thức `disabled` hay guard quyền nào.
2. Quyền bấm nút lấy từ **BE**, không từ cờ: đọc capability của dự án (`/projects/{id}/my-capabilities` hoặc field capability mà `review-gates/timeline` đã trả). BE đã có `Capabilities(CanSubmitPackage, CanSubmitRevision, BlockingReasons[])` — kiểm xem có field cho scope decision chưa; **nếu chưa thì báo lên** để BE thêm, đừng tự suy ra ở FE.
3. Trong lúc chờ: cho nút **hiện và bấm được**, dựa vào BE để từ chối. BE **đã có guard đầy đủ** ở `:486-499`, nên bấm sai chỉ nhận 403 — đó là fail-closed đúng cách. Ẩn nút vì sợ là sai hướng: nó biến một 403 có thông báo thành một luồng chết im lặng.
4. 403 ⇒ hiện lý do từ `errorCode`, không ẩn nút.
5. 🔴 **Rà cả repo xem còn chỗ nào dùng cờ dev làm cờ quyền:**
   ```bash
   git grep -n "env\.[a-zA-Z]*Mock[a-zA-Z]*" -- src | grep -i "disabled\|canEdit\|readonly\|return"
   ```
   Tìm thấy thì **liệt kê trong mô tả PR**; sửa trong ticket này nếu cùng pattern, mở ticket riêng nếu khác.

### Bảng trạng thái UI

| Trạng thái | Hiển thị |
|---|---|
| BE cho phép quyết định phạm vi | nút **hiện và bấm được** ở production |
| BE trả 403 | nút vẫn hiện, hiện lý do từ `errorCode` |
| Đang gửi | `busyScope` — nút disabled (lý do kỹ thuật, hợp lệ) |
| Đã quyết định | hiện quyết định + ai + khi nào |
| Dự án đơn ngành | panel không hiện (đúng) |
| 409 | giữ lựa chọn người dùng, hiện banner đối chiếu |

### Tiêu chí nghiệm thu

- Build production (`npm run build`) ⇒ nút quyết định phạm vi **hiện và bấm được**.
- `git grep` cờ mock không còn ở bất kỳ biểu thức `disabled` hay guard quyền nào.
- Người không có quyền bấm ⇒ 403 kèm lý do đọc được, nút **không bị ẩn**.
- Dự án liên ngành: sau khi quyết đủ `majorCount + 1` phạm vi ⇒ `DefenseReadiness` chuyển sẵn sàng.
- Mô tả PR liệt kê mọi chỗ còn dùng cờ dev làm cờ quyền.

### Kiểm chứng

```bash
git grep -n "reviewGatesMockEnabled" -- src
# kỳ vọng: chỉ còn ở chỗ chọn nguồn dữ liệu, KHÔNG ở disabled/guard
npm run build
npm run test -- interdisciplinary-scope
```

### File dự kiến

- `src/features/review-gates/components/InterdisciplinaryGatePanel.tsx:54` · `:167`
- `src/app/config/env.ts:94` (nếu cần tách cờ)
- `src/features/review-gates/__tests__/interdisciplinary-scope.test.tsx` (mới)

**Liên quan:** WF-03 · FL-08 · REQ-02

---

# TASK F6 · MEDIUM · Endpoint prefill đã merged từ lâu mà không có đường gọi

### Bằng chứng

```bash
git grep -rn "progress-reports/prefill" e8e9801c -- src
# → RỖNG
```

BE có `ProgressReportPrefillDto` với field `Suggested` khớp **1:1** với các ô của form báo cáo tiến độ. Không ai gọi.

Đây đúng là lỗi `X2` (slice ngang: BE làm API, FE không làm UI) và `X10` (endpoint không có caller).

### Nghiệp vụ

REQ-14 của v3.0: giảm nhập tay. Sinh viên đang phải gõ lại những gì hệ thống đã biết (task đã xong trong kỳ, mốc đã qua, blocker đang mở). Prefill là **gợi ý**, không phải điền tự động — người dùng vẫn phải đọc và sửa.

### Cách sửa đề xuất

1. Khi mở form báo cáo tiến độ **mới** (không phải khi sửa bản nháp đã có), gọi prefill.
2. Điền vào các ô tương ứng, **kèm dấu hiệu rõ là gợi ý**: nền nhạt + dòng *"Gợi ý từ dữ liệu dự án — hãy kiểm tra và sửa trước khi nộp."*
3. Có nút **"Bỏ gợi ý, nhập lại từ đầu"**.
4. 🔴 **Prefill thất bại không được chặn form.** Lỗi ⇒ form trống bình thường, không banner đỏ. Người dùng vẫn phải nộp được báo cáo.
5. 🔴 **Không prefill khi đang sửa bản nháp đã lưu.** Ghi đè cái người ta đã viết là mất dữ liệu.
6. `Suggested` field nào `null` ⇒ để ô **trống**, không điền `0` hay `"—"` (`N6`).

### Bảng trạng thái UI

| Trạng thái | Hiển thị |
|---|---|
| Form mới, prefill thành công | ô có gợi ý: nền nhạt + chú thích + nút bỏ gợi ý |
| Form mới, prefill lỗi/timeout | form trống, **không** báo lỗi |
| Sửa bản nháp đã lưu | **không** gọi prefill |
| `Suggested` field `null` | ô trống |
| Người dùng đã sửa ô | mất dấu hiệu "gợi ý" của ô đó |
| Đang tải prefill | ô disabled kèm skeleton, tối đa ~2s rồi bỏ |

### Tiêu chí nghiệm thu

- Mở form mới ⇒ có request tới `progress-reports/prefill`, các ô được điền kèm dấu hiệu gợi ý.
- Prefill trả 500 ⇒ form dùng được bình thường, không banner lỗi.
- Mở bản nháp đã lưu ⇒ **không** có request prefill.
- Bấm "Bỏ gợi ý" ⇒ mọi ô gợi ý về trống.
- Field `null` ⇒ ô trống, không `0`.

### Kiểm chứng

```bash
git grep -rn "progress-reports/prefill" -- src   # kỳ vọng: CÓ
npm run test -- progress-report-prefill
```

### File dự kiến

- `src/features/execution/` (trang + form báo cáo tiến độ)
- `src/services/api/` (thêm hàm gọi prefill)
- `src/features/execution/__tests__/progress-report-prefill.test.tsx` (mới)

**Liên quan:** FL-07 · REQ-14 · T-09

---

### ✅ CHECKPOINT 0 — sau F1…F6

Báo lại:

1. Sáu PR, trạng thái từng cái.
2. **`F1`: dựng được scheme 11 component trên staging chưa** — đây là thứ chặn cả kỳ, cần xác nhận bằng thao tác thật, không chỉ bằng test.
3. **`F5`: liệt kê mọi chỗ còn dùng cờ dev làm cờ quyền ghi.**
4. `F2`: đã dùng fixture từ `docs/contracts/` của BE chưa, hay vẫn viết tay (`X4`).

---

# PHASE 1 — SAU KHI BE XONG

# TASK F7 · MEDIUM · FE hardcode 20 MiB, BE có bốn mức khác

**Chờ `BE-05`.**

### Bằng chứng

`src/features/review-gates/api/review-gates-api.ts:95`:

```ts
if (req.file.size > 20 * 1024 * 1024) throw new Error('Tệp vượt quá giới hạn 20 MiB.')
```

BE thì có bốn mức, không cái nào là 20:

```bash
git grep -oh "RequestSizeLimit([0-9 *_]*)" 14eecc7a -- src/AIPMS.Api/Controllers | sort | uniq -c
#   3 RequestSizeLimit(11 * 1024 * 1024)
#   4 RequestSizeLimit(22 * 1024 * 1024)
#   2 RequestSizeLimit(6 * 1024 * 1024)
#   2 RequestSizeLimit(9 * 1024 * 1024)
```

⇒ Hai lỗi cùng lúc: endpoint cho 22 MiB thì FE chặn mất 2 MiB hợp lệ; endpoint chỉ nhận 6 MiB thì FE cho người dùng chờ upload xong rồi mới báo lỗi. Đây là `X7`.

### Cách sửa đề xuất

1. Gọi `GET /v1/upload-policies` (từ `BE-05`), cache theo phiên.
2. Validate theo `maxBytes` **của đúng parent type đó**.
3. Hiện giới hạn và loại file cho phép **trước khi** người dùng chọn file: *"PDF, PNG · tối đa 22 MiB"*. Dùng `maxBytesLabel` BE gửi, **không tự format** từ byte.
4. 🔴 **Xóa mọi con số literal.** `git grep "1024 \* 1024"` trong `src/` phải sạch sau ticket này (trừ chỗ đọc từ policy).
5. 413 từ BE ⇒ hiện `maxBytes` BE trả về, không hiện con số client đoán.
6. Policy chưa tải được ⇒ **không chặn upload**, để BE từ chối. Fail-closed ở client ở đây sẽ làm người dùng không nộp được bài.

### Bảng trạng thái UI

| Trạng thái | Hiển thị |
|---|---|
| Policy đã tải | "PDF, PNG · tối đa 22 MiB" trước khi chọn file |
| File quá size | chặn tại client, nêu đúng giới hạn của endpoint đó |
| Sai loại file | chặn tại client, liệt kê loại cho phép |
| Policy chưa tải được | cho chọn file, để BE từ chối |
| 413 từ BE | hiện `maxBytes` của BE |
| Đang upload | progress + nút huỷ |

### Tiêu chí nghiệm thu

- Mỗi chỗ upload hiện giới hạn đúng của endpoint đó, lấy từ API.
- `git grep "1024 \* 1024" -- src` sạch (trừ chỗ đọc policy).
- File 21 MiB lên endpoint 22 MiB ⇒ **qua** (trước đây bị chặn).
- File 10 MiB lên endpoint 6 MiB ⇒ chặn **ngay**, không upload rồi mới lỗi.
- Policy lỗi ⇒ vẫn upload được.

### Kiểm chứng

```bash
git grep -n "1024 \* 1024" -- src          # kỳ vọng: chỉ ở chỗ đọc policy
git grep -rn "upload-policies" -- src      # kỳ vọng: CÓ
npm run test -- upload-policy
```

### File dự kiến

- `src/features/review-gates/api/review-gates-api.ts:95`
- `src/services/api/` (client upload-policies)
- mọi chỗ upload khác (rà bằng `git grep "FormData"`)
- `src/features/files/__tests__/upload-policy.test.tsx` (mới)

**Liên quan:** WF-31 · FL-36 · REQ-33

---

# TASK F8 · HIGH · 🔴 CHỜ VĂN BẢN · Rubric Builder: nhập mô tả mức và gửi duyệt

**Chờ `BE-06` + `BE-07` + `BE-08`. Và ba cái đó chờ bộ môn ký.**

> 🔴 **Ticket này mở PR được, KHÔNG merge được** trước khi `CAU_HOI_BO_MON_2026-10-30.md` mục `Q-A10` và `Q-N02` có chữ ký. Lý do ở `README.md` quy tắc 2.

### Bằng chứng — hiện tại không có chỗ nào nhập được

`RubricManagementPage.tsx:164` — ô mô tả của một tiêu chí là **một `<input>` một dòng**:

```tsx
<label className="text-xs">Mô tả<input className="mt-1 min-h-11 w-full rounded border px-2" value={item.description ?? ''} ... /></label>
```

Form hiện có: Bộ môn · Học kỳ · Mã · Tên · Mô tả · rồi cây tiêu chí với Tên / Trọng số % / Điểm tối đa / Mô tả / Bắt buộc.

**Không có:** hình thức đồ án · giai đoạn · phạm vi · ngành · hiệu lực · văn bản phê duyệt · mô tả các mức chất lượng · đổi thứ tự · xem trước phiếu chấm · gửi duyệt.

Và nút hiện tại là `Lưu bản nháp` + publish trực tiếp — không có bước gửi duyệt.

### Nghiệp vụ

Xem `RUBRIC_DESIGN_2026-10-30.md` §2 và §6.3. Một kỳ liên ngành cần **9 rubric**, phân biệt theo (hình thức × chặng × phạm vi × ngành). Mỗi tiêu chí lá cần **mô tả từng mức chất lượng**, nếu không hai giảng viên chấm lệch nhau và sinh viên khiếu nại thì không có căn cứ.

Và theo `BE-08`, đường đi mới là `DRAFT → PENDING_APPROVAL → PUBLISHED`, với người duyệt **phải khác** người gửi.

### Cách sửa đề xuất

**1. Bảy field scope mới** (từ `BE-07`) ở đầu form: Hình thức đồ án · Giai đoạn · Phạm vi · Ngành · Hiệu lực từ · Hiệu lực đến · Văn bản phê duyệt (**readonly, chỉ hiện cái BE gửi**).

- Phạm vi `COMMON` ⇒ ô Ngành **ẩn** (BE sẽ 422 nếu có ngành).
- Phạm vi `MAJOR_SPECIFIC` ⇒ ô Ngành **bắt buộc**.

**2. Mô tả mức chất lượng** (từ `BE-06`) cho mỗi tiêu chí **lá**:

- N ô mô tả — **N đọc từ dữ liệu BE trả**, không hardcode 4. Khi bộ môn chốt 5 mức thì UI tự theo, không sửa code. (`Q-A10` chưa ký, nên đây là điều kiện bắt buộc của ticket.)
- Mỗi mức: Nhãn · Mô tả (**`<textarea>`**, không phải input một dòng) · Dải điểm gợi ý từ–đến.
- Tiêu chí **cha** không có ô mức (`N5`).
- 🔴 **Soạn dần phải được.** Lưu nháp khi thiếu mức ⇒ 200. Chỉ bước **gửi duyệt** mới đòi đủ. 38 tiêu chí × 4 mức là việc nhiều buổi — chặn lúc lưu là làm bộ môn bỏ.

**3. Đổi thứ tự** `[▲][▼]` trong từng nhóm — BE từ chối `sortOrder` trùng (`RubricRules.cs:26`), nên đây cũng là cách tránh lỗi đó.

**4. Xem trước phiếu chấm:** render đúng cái giảng viên sẽ thấy ở màn chấm, kèm hai con số trọng số (`F2`). Đây là thứ cho bộ môn tự kiểm "phiếu này có dùng được không" trước khi gửi duyệt.

**5. Gửi duyệt:** nút `[Công bố]` → `[Gửi duyệt]`. Trạng thái `PENDING_APPROVAL` ⇒ form readonly + banner *"Đang chờ phê duyệt — người gửi không tự duyệt được."*

> ✅ **Không cần code thêm cho readonly.** BE tính `canEdit = Status == DRAFT && !IsReferenced` (`RubricDtos.cs:42`), nên `PENDING_APPROVAL` **tự động** `canEdit = false`, và `RubricManagementPage.tsx:155` đã rẽ nhánh theo `canEdit`. Chỉ cần thêm banner giải thích.

**6. Người duyệt:** nút `[Phê duyệt]` / `[Từ chối]` (bắt buộc lý do) — chỉ hiện khi BE nói actor được duyệt. 🔴 **Không tự suy "actor !== submittedBy thì cho duyệt"** — đó là suy quyền ở FE (quy tắc 2). Hỏi BE.

**7. So sánh phiên bản:** diff v1 vs v2 — tiêu chí thêm/bớt, trọng số đổi, mức đổi.

### Bảng trạng thái UI

| Trạng thái | Hiển thị |
|---|---|
| `DRAFT`, `canEdit = true` | sửa được, nút `[Lưu bản nháp]` + `[Gửi duyệt]` |
| `DRAFT`, thiếu mức ở tiêu chí lá | lưu nháp **được**; `[Gửi duyệt]` disabled + nêu **tiêu chí nào** thiếu |
| `PENDING_APPROVAL` | readonly + banner chờ duyệt (tự động qua `canEdit`) |
| `PENDING_APPROVAL`, actor được duyệt | thêm `[Phê duyệt]` / `[Từ chối]` |
| Người gửi tự bấm duyệt | BE trả **403** `RUBRIC_SELF_APPROVAL_FORBIDDEN` ⇒ hiện lý do |
| `PUBLISHED` | 🔒 readonly + `[Tạo phiên bản mới]` |
| `IsReferenced` | 🔒 không cho xoá tiêu chí đã dùng chấm; BE trả 409 |
| legacy `departmentId == null` | readonly (đã có ở `:79`) |
| legacy không có mức | **đọc và chấm được**, không đòi mức |
| 403 sai phạm vi | hiện lý do từ `errorCode` |
| 409 | giữ form, hiện diff |
| 422 tổng trọng số | dùng `F3` — chỉ đúng nhóm sai |

### Tiêu chí nghiệm thu

- Dựng được **9 rubric** phân biệt bằng bảy field scope.
- Phạm vi `COMMON` ⇒ ô Ngành ẩn; `MAJOR_SPECIFIC` ⇒ bắt buộc.
- Số ô mức **đọc từ BE**: BE trả 5 mức ⇒ UI hiện 5 ô, **không sửa code**.
- Lưu nháp khi thiếu mức ⇒ 200. `[Gửi duyệt]` disabled + nêu tên tiêu chí thiếu.
- `PENDING_APPROVAL` ⇒ readonly, banner chờ duyệt.
- Người gửi bấm duyệt ⇒ hiện 403 với lý do đọc được.
- Người khác duyệt ⇒ `PUBLISHED`.
- 🔴 Rubric legacy không có mức/scope ⇒ mở được, đọc được, **không** bị đòi mức.
- Văn bản phê duyệt: **readonly**, không có ô nhập tự do, không hiện placeholder bịa.
- Xem trước phiếu chấm khớp màn chấm thật.

### Kiểm chứng

```bash
npm run test -- rubric-builder
git grep -n "levels" -- src/features/evaluations/rubrics-api.ts         # kỳ vọng: CÓ
git grep -n "length === 4\|levels\[3\]\|LEVEL_COUNT = 4" -- src          # kỳ vọng: RỖNG (không hardcode số mức)
git grep -n "submit-for-approval" -- src                                 # kỳ vọng: CÓ
```

### File dự kiến

- `src/features/evaluations/RubricManagementPage.tsx` (form, `CriterionEditor:164`, `RubricReadView`)
- `src/features/evaluations/rubrics-api.ts` (7 field scope + `levels[]` + 3 endpoint mới)
- `src/features/evaluations/components/RubricSheetPreview.tsx` (mới)
- `src/features/evaluations/components/RubricVersionDiff.tsx` (mới)
- `src/features/evaluations/__tests__/rubric-builder.test.tsx` (mới)

**Liên quan:** WF-23 · FL-18 · REQ-24 · REQ-26

---

# TASK F9 · HIGH · Màn chấm hiện mức chất lượng, chặng, và hai trọng số

**Chờ `BE-09`.**

### Bằng chứng — hiện chỉ có một con số trọng số

`EvaluatorAssignmentDetailPage.tsx:174` (trích phần mô tả tiêu chí):

```tsx
<p className="mt-1 break-words text-sm leading-6 text-ink-muted">{criterion.description || 'Không có mô tả.'} · trọng số {formatPercent(criterion.weightPercent)} · tối đa {formatScore(criterion.maxScore)}</p>
```

**Một** nhãn "trọng số", **một** con số. Không phân biệt *trong bộ tiêu chí* với *tỷ trọng trong điểm sinh viên*.

Header `:156` có ba dòng: Đồ án · Bộ tiêu chí · Phạm vi — **không có Stage**.

Và `:164` map Stage từ `evidence.stage` chỉ để hiện trong khối "Tóm tắt gói bàn giao", không ở header.

Những thứ màn này **đã làm đúng và phải giữ:**

| Hành vi | Dòng |
|---|---|
| Ô trống bị **bỏ khỏi payload** ⇒ `null ≠ 0` — `N6` | `:121` `raw ? [...] : []` |
| 409 ⇒ **giữ nháp** + banner đối chiếu | `:128` · `:160` |
| 403/404 ⇒ `permissionLost`, ẩn thao tác | `:129` |
| `denialReason === 'LEGACY_SCOPE_UNKNOWN'` có thông báo riêng | `:167` |
| `itemCount === 0` ⇒ cảnh báo *"tài liệu chung không tự trở thành minh chứng cá nhân"* | `:164` |
| `FINALIZED` ⇒ readonly + thời điểm chốt | `:178` |

🔴 **Đừng phá bất kỳ dòng nào trong bảng trên.**

### Nghiệp vụ

Người chấm cần bốn thứ tại chỗ nhập điểm: tiêu chí này **nặng bao nhiêu trong điểm cuối của sinh viên** · mức nào ứng với điểm nào · đang chấm **chặng** nào · phiếu này chính thức chưa (`F4` đã làm).

Theo `RUBRIC_DESIGN_2026-10-30.md` §2, COLD và DEFENSE chấm **hai thứ khác nhau** trên cùng công trình — COLD chấm sản phẩm, DEFENSE chấm mức làm chủ sản phẩm của tác giả. Không hiện Stage ở header thì người chấm không biết mình đang ở chặng nào.

### Cách sửa đề xuất

**1. Header thêm hai dòng:**
- **Giai đoạn:** "Chấm hồ sơ đã khóa" / "Bảo vệ" — dùng **chung hằng nhãn** với `F1`, đừng khai lần thứ ba.
- **Tỷ trọng phiếu này trong điểm sinh viên:** `componentStudentWeightPercent`.

**2. Mỗi tiêu chí: hai con số trọng số, nhãn phân biệt rõ.**

```
trong bộ tiêu chí: 35%   ·   tỷ trọng trong điểm SV: 12,25%
```

🔴 **Cả hai con số do BE gửi.** Tuyệt đối **không nhân** `effectiveWeightPercent × componentStudentWeightPercent` ở FE — đó chính là `N4` cấm. Nếu BE chỉ gửi một số thì **báo lên**, đừng tự tính số còn lại.

**3. Mô tả mức chất lượng:** mặc định **thu gọn**, bấm mở. Form chấm có 3–6 tiêu chí; mở sẵn 4 đoạn mô tả cho mỗi tiêu chí làm phiếu không dùng được. Khi mở: bảng mức · nhãn · mô tả · dải điểm gợi ý.

> ⚠️ **Dải điểm là GỢI Ý, không phải ngưỡng đạt.** Không được dùng nó để validate ô điểm. Giảng viên cho 8,7 khi dải ghi 7–8,9 là bình thường; cho 9 khi dải ghi 7–8,9 cũng là quyền của họ.

**4. `evidenceGuidance`** hiện cạnh tiêu chí: "chấm trên minh chứng nào".

**5. Nhóm cha–con thụt lề** theo `parentId` (`F2`). Tiêu chí cha **không có ô điểm** (`N5`).

**6. Nút `? Hướng dẫn chấm`** mở panel: hai chặng khác nhau thế nào, hai trọng số nghĩa gì, để trống khác cho 0 thế nào.

**7. Coverage:** "Người chấm đã hoàn tất: 2/3" nếu BE gửi. Kèm một câu: *"Thiếu phiếu thì kết quả ở trạng thái chờ, không phải 0."* (`N6`)

**8. Tablet 768px** phải chấm hết được không cần zoom — hội đồng dùng tablet tại buổi bảo vệ.

### Bảng trạng thái UI

| Trạng thái | Hiển thị |
|---|---|
| Rubric có mức | mỗi tiêu chí lá có mục mức thu gọn |
| 🔴 Rubric legacy `levels = []` | **không** hiện mục mức, chấm bình thường, **không** báo lỗi |
| 🔴 `stage = null` | "Chưa xác định", **không** đoán thành `DEFENSE` |
| BE chỉ gửi một trọng số | hiện một, ghi rõ là con số nào, **không** tự tính số kia |
| Tiêu chí cha | không ô điểm, không mức, không "bắt buộc" |
| `canScore = false` | như hiện tại `:167` |
| `FINALIZED` | readonly + thời điểm, vẫn hiện mức để đối chiếu |
| 409 | **giữ nháp** như `:160` |
| 422 | lỗi tại đúng field như hiện tại |
| Tablet 768 | một cột, mức thu gọn, ô điểm luôn thấy |

### Tiêu chí nghiệm thu

- Header hiện Stage + tỷ trọng phiếu.
- Mỗi tiêu chí hiện **hai** con số với nhãn khác nhau; `git grep` không có phép nhân hai trọng số nào trong `src/`.
- Mức chất lượng thu gọn, bấm mở, nội dung khớp BE.
- 🔴 Dải điểm **không** validate ô điểm: nhập 9 khi dải 7–8,9 ⇒ **chấp nhận**.
- 🔴 Rubric legacy không mức ⇒ chấm và chốt được, không lỗi.
- 🔴 Sáu hành vi trong bảng "đã làm đúng" **không hồi quy** — có test cho từng cái.
- Tablet 768: chấm trọn phiếu không zoom, không cuộn ngang.
- `role="alert"` / `role="status"` giữ như hiện tại.

### Kiểm chứng

```bash
npm run test -- evaluator-assignment
git grep -n "effectiveWeightPercent\|componentStudentWeightPercent" -- src/features/evaluations
git grep -rnE "(effectiveWeightPercent|studentWeightPercent)\s*\*" -- src   # kỳ vọng: RỖNG
# hồi quy sáu hành vi: test riêng cho null≠0, 409 giữ nháp, 403 permissionLost,
# LEGACY_SCOPE_UNKNOWN, itemCount=0, FINALIZED readonly
```

### File dự kiến

- `src/features/evaluations/pages/EvaluatorAssignmentDetailPage.tsx:155-157` · `:174`
- `src/features/evaluations/evaluation-types.ts`
- `src/features/evaluations/components/CriterionLevelPanel.tsx` (mới)
- `src/features/evaluations/components/ScoringGuideDrawer.tsx` (mới)
- `src/features/evaluations/__tests__/evaluator-assignment-levels.test.tsx` (mới)

**Liên quan:** WF-11 / WF-24 · FL-19/20/21 · REQ-11 · REQ-25

---

### ✅ CHECKPOINT 1 — sau F7, F8, F9

Báo lại:

1. `F8` **chưa merge được** — xác nhận PR đã mở và đã review, đang chờ chữ ký.
2. `F9`: output test hồi quy **sáu hành vi** của màn chấm. Đây là phần dễ làm vỡ nhất của đợt này.
3. `F8` + `F9`: xác nhận rubric legacy vẫn đọc và chấm được.

---

# PHASE 2 — HOÀN THIỆN

# TASK F10 · MEDIUM · Tour có một bước chết ở mọi vai, và Skip bị tính là Hoàn thành

### Bằng chứng — hai lỗi độc lập

**Lỗi 1: một bước trỏ vào phần tử không tồn tại.**

Cả **bốn** script đều có bước trỏ `sidebar-primary` — `tour-scripts.ts:19` `:31` `:43` `:55`:

```ts
{ id: 'primary', target: 'sidebar-primary', title: 'Bắt đầu từ Tổng quan lộ trình', ... }
```

Nhưng `data-tour` thật trong toàn bộ DOM chỉ có **sáu** giá trị:

```bash
git grep -rhn "data-tour=" e8e9801c -- src | grep -o 'data-tour="[^"]*"' | sort -u
# data-tour="${step.target}"   ← chỗ TourOverlay gắn, không phải target
# data-tour="breadcrumb"
# data-tour="help"
# data-tour="notifications"
# data-tour="profile"
# data-tour="quick-nav"
# data-tour="sidebar"
```

Có `sidebar`. **Không có `sidebar-primary`.** ⇒ Bước đó **bị bỏ im lặng ở cả bốn vai**, và không có log nào.

**Lỗi 2: bỏ qua tour được tính là đã hoàn thành.**

`TourProvider.tsx:22-25`:

```ts
const handleClose = useCallback((_completed: boolean) => {
  setActive(false)
  if (user && script) markTourSeen(user.id, script.role, TOUR_VERSION)
}, [user, script])
```

Tham số `_completed` **bị bỏ qua hoàn toàn**. ⇒ Người bấm "Bỏ qua" ở bước 1 và người đi hết tour được ghi nhận **giống nhau**, và tour không mở lại nữa.

Thêm nữa: `TourLauncher` chỉ có ở **một** chỗ — `TopHeader.tsx:153`. Không có hướng dẫn theo trang.

### Nghiệp vụ

REQ-19 của v3.0: hướng dẫn có ngữ cảnh. Hiện tour chỉ giới thiệu **vỏ shell** (sidebar, thông báo, hồ sơ) — sáu target đều là khung giao diện, **không một target nào là tính năng nghiệp vụ**. Với một hệ thống mà quy trình là phần khó nhất, tour hiện tại gần như không giúp gì.

Đợt này **không làm lại toàn bộ tour** — chỉ sửa hai lỗi trên và dựng nền để thêm hướng dẫn theo trang về sau.

### Cách sửa đề xuất

1. **Sửa lỗi 1 — và sửa đúng cách:** đổi `sidebar-primary` → `sidebar` ở cả bốn script, **hoặc** thêm `data-tour="sidebar-primary"` vào đúng phần tử mục Tổng quan. Chọn cái nào cũng được, nhưng **phải kiểm bằng lệnh** là không còn target nào mồ côi.
2. 🔴 **Thêm `fallbackBody` cho `TourStep`.** Target không tìm thấy ⇒ hiện bước đó dạng hộp giữa màn hình với `fallbackBody`, **không im lặng bỏ**. Đây là thứ ngăn lỗi 1 tái diễn khi ai đó đổi DOM.
3. **Sửa lỗi 2:** dùng `completed`:
   ```ts
   status: 'NOT_STARTED' | 'IN_PROGRESS' | 'COMPLETED' | 'DISMISSED'
   ```
   `DISMISSED` ⇒ không tự mở lại, **nhưng** `TourLauncher` vẫn mở được và người dùng vào đúng bước đang dở (`lastStepId`).
4. **Guard dev chống tái diễn:** ở dev, `console.warn` liệt kê mọi `step.target` không có phần tử tương ứng. Tốt hơn nữa: một test đọc `tour-scripts.ts`, đối chiếu với tập `data-tour` có trong `src/`, fail nếu mồ côi. **Test này là giá trị lâu dài của ticket.**
5. 🔴 **Tour không được gây mutation nào.** Chỉ điều hướng và tô sáng. Không bấm nút thay người dùng.
6. Nền cho hướng dẫn theo trang: thêm prop `guideId` cho `WorkspacePage`, `TourLauncher` đọc nó. **Chưa cần viết nội dung** cho 83 trang — chỉ dựng đường.

### Bảng trạng thái UI

| Trạng thái | Hiển thị |
|---|---|
| Lần đầu đăng nhập | tự mở, `status = IN_PROGRESS` |
| Đi hết | `COMPLETED`, không tự mở lại |
| Bấm "Bỏ qua" ở bước 2 | `DISMISSED` + `lastStepId = 2`, không tự mở lại |
| Mở lại từ `TourLauncher` sau khi `DISMISSED` | vào **bước 2** |
| Target không tìm thấy | hộp giữa màn hình + `fallbackBody`; **không** bỏ im lặng |
| `TOUR_VERSION` tăng | `NOT_STARTED` lại, tự mở |
| Vai không có script | `TourLauncher` ẩn |

### Tiêu chí nghiệm thu

- `git grep "sidebar-primary"` ⇒ hoặc rỗng, hoặc có phần tử DOM tương ứng.
- **Test mồ côi target xanh:** mọi `step.target` trong `tour-scripts.ts` đều có `data-tour` tương ứng trong `src/`.
- Bỏ qua ở bước 2 ⇒ `DISMISSED`, không tự mở lại; mở lại thủ công ⇒ vào bước 2.
- Đi hết ⇒ `COMPLETED`.
- Target bị xoá giả lập ⇒ hiện `fallbackBody`, không crash, không bỏ im.
- Tour không gọi mutation nào — kiểm bằng network log.

### Kiểm chứng

```bash
git grep -n "sidebar-primary" -- src
npm run test -- tour-targets
npm run test -- tour-persistence
```

### File dự kiến

- `src/features/onboarding/tour-scripts.ts:19,31,43,55`
- `src/features/onboarding/TourProvider.tsx:22-25`
- `src/features/onboarding/tour-types.ts` (`fallbackBody`, `status`)
- `src/features/onboarding/tour-persistence.ts`
- `src/components/ui/WorkspacePage.tsx` (prop `guideId`)
- `src/features/onboarding/__tests__/tour-targets.test.ts` (mới — **phần giá trị nhất**)

**Liên quan:** WF-19 · FL-01 / FL-30 · REQ-19

---

# TASK F11 · MEDIUM · Hạ tầng chống lệch hợp đồng

### Bằng chứng

BE có **37 file example JSON** máy đọc được ở `docs/contracts/cib-v4/`, khuôn `{request: {method, path, headers, body}, response: {status, body}}`.

FE có **7 tài liệu văn xuôi**. Văn xuôi **không fail CI được**.

Kết quả đã thấy: `rubrics-api.ts` bỏ hai field BE gửi (`F2`), và không gì phát hiện. Đây là `X4`.

### Cách sửa đề xuất

1. `scripts/sync-contracts.mjs` — kéo example JSON từ repo BE về `src/__contracts__/`.
2. `contract-lock.json` — hash từng file đã vendor.
3. Job CI `contract-drift`: chạy sync, so hash, **đỏ nếu lệch**.
4. Test dùng example đã vendor làm fixture. **Không viết fixture tay nữa** (quy tắc 6).
5. Thêm một test kiểu "type khớp example": parse example, assert mọi key trong response body đều có trong interface TS tương ứng. Đây là thứ **sẽ bắt được** đúng lỗi `rubrics-api.ts`.

### Tiêu chí nghiệm thu

- `npm run sync-contracts` kéo được example về.
- Sửa tay một file đã vendor ⇒ job `contract-drift` **đỏ**.
- Ít nhất `F2` (rubric) và `F9` (evaluator assignment) dùng example đã vendor làm fixture.
- Test "type khớp example" **fail** khi xoá một field khỏi interface TS ⇒ chứng minh nó thật sự bắt được lỗi.

### Kiểm chứng

```bash
npm run sync-contracts
npm run test -- contract-shape
# giả lập lệch: sửa 1 file trong src/__contracts__/ rồi chạy lại job
```

### File dự kiến

- `scripts/sync-contracts.mjs` (mới) · `contract-lock.json` (mới)
- `.github/workflows/` (job `contract-drift`)
- `src/__contracts__/` (mới, vendored)
- `src/__tests__/contract-shape.test.ts` (mới)

**Liên quan:** REQ-34 · `X3` · `X4`

---

# 2. VERIFICATION TỔNG — chạy trước khi báo xong đợt

```bash
cd ai-pms-frontend && git fetch -q
git log -1 --format='FE %h %ci' origin/develop
gh pr list --repo aipms-sep490/ai-pms-frontend --state open

# F1 — Stage đã có ô nhập
git grep -n "EvaluationStage" -- "src/**/*.tsx"                                     # CÓ

# F2 — hai trường đã nhận, và không tự nhân trọng số
git grep -n "effectiveWeightPercent\|parentId" -- src/features/evaluations/rubrics-api.ts   # CÓ
git grep -rnE "(effectiveWeightPercent|weightPercent)\s*\*" -- src                  # RỖNG

# F3 — validate theo nhóm
git grep -n "topLevelWeightSum" -- src/features/evaluations/RubricManagementPage.tsx # RỖNG

# F4 — nhãn phê duyệt trên màn chấm
git grep -c "isDemo\|approvalStatus" -- src/features/evaluations/pages/EvaluatorAssignmentDetailPage.tsx  # > 0

# F5 — cờ dev không còn làm cờ quyền
git grep -n "reviewGatesMockEnabled" -- src          # chỉ ở chỗ chọn nguồn dữ liệu
git grep -nE "env\.[a-zA-Z]*Mock[a-zA-Z]*" -- src | grep -iE "disabled|canEdit|readonly"   # RỖNG

# F6 — prefill có caller
git grep -rn "progress-reports/prefill" -- src       # CÓ

# F7 — không còn hardcode giới hạn
git grep -n "1024 \* 1024" -- src                    # chỉ ở chỗ đọc policy

# F8 — không hardcode số mức
git grep -nE "LEVEL_COUNT = 4|levels\[3\]|length === 4" -- src   # RỖNG

# F10 — không còn target mồ côi
git grep -n "sidebar-primary" -- src

# Toàn bộ
npm run typecheck && npm run lint && npm run test && npm run build
```

**Không làm được, cần người khác:**

- **`BROWSER_VERIFIED`**: toàn bộ file này ở mức `VERIFIED_SOURCE` + `CI_VERIFIED`. Chưa ai chạy app từ baseline này, chưa đăng nhập vai nào. Mọi mô tả hành vi runtime là **suy ra từ source**. Gặp chỗ app làm khác source thì **app đúng** — báo lại, đừng tự sửa tài liệu.
- **Chạy thật trên staging** (26 tài khoản demo P1/P2/P3 có sẵn): `F1` phải được xác nhận bằng **thao tác thật dựng 11 component**, không chỉ bằng unit test. `F5` phải được xác nhận bằng **build production thật**.
- **`BE-05`, `BE-06`…`BE-09`** cho `F7`, `F8`, `F9`.
- **Chữ ký bộ môn** (`Q-A10`, `Q-N02`) cho `F8`.
