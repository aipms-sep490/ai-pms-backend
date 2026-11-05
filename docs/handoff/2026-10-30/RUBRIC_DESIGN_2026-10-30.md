# THIẾT KẾ BỘ TIÊU CHÍ & VẬN HÀNH CHẤM ĐIỂM THEO 4 MỐC REVIEW
## Đơn ngành (Marketing) và Liên ngành (CNTT + Marketing + Thiết kế đồ họa)

> # 🔴 TRẠNG THÁI: ĐỀ XUẤT — CHỜ HỘI ĐỒNG HỌC VỤ PHÊ DUYỆT
>
> Tài liệu này **chưa có hiệu lực**. Mọi con số trọng số, mọi tiêu chí và mọi cách phân công trong đây là **đề xuất của nhóm phát triển**, chưa được bộ môn hay hội đồng ký.
>
> Tám điểm cần quyết nằm ở `CAU_HOI_BO_MON_2026-10-30.md`.

**Ngày:** 30/10/2026
**Nguồn code:** BE `14eecc7a04a1b91bb4c491c5ca85b09edbb99f94` · FE `e8e9801c401bc810ce19299748c332af4f36aec0`
**Người đọc:** giảng viên · hội đồng học thuật · BE dev · FE dev
**Quyết định cần ra:** bộ tiêu chí và cách chấm này có áp dụng được không

**Tài liệu này thay thế §3.3 và §4.3** của `../../templates/AI-PMS_Bo_Mau_Hoc_Thuat_MKT_va_Lien_Nganh_2026-10-29.md`. Các phần khác của bộ mẫu đó (lộ trình gate, danh mục báo cáo chấm nguội, ma trận truy vết, nguồn pháp lý và học thuật) **vẫn còn hiệu lực**.

---

## Context — vì sao có tài liệu này

Cần một bộ tiêu chí và cách vận hành chấm điểm để **giảng viên và hội đồng đọc rồi quyết được là có áp dụng được hay không**.

Bản mẫu trước (`2026-10-29`) đặt **toàn bộ thành phần chấm ở giai đoạn `DEFENSE`** và không gắn vào bốn mốc Review. Ba điều chỉnh sau đó đã đổi hẳn thiết kế:

1. **Review 1, 2, 3 là đánh giá và phản hồi dự án — không sinh ra điểm.**
2. **Review 4 là nơi evaluator chấm điểm**, và nó **tách hai chặng: chấm hồ sơ đã khóa (COLD) trước, rồi bảo vệ (DEFENSE)**, theo kiểu COLD 30 / DEFENSE 70.
3. Liên ngành có đủ ba phạm vi: CHUNG, theo ngành, và cá nhân — trong đó phần cá nhân có theo dõi trong quá trình cho từng sinh viên.

### Ba đính chính so với các bản trước

| # | Bản trước viết | Đúng là |
|---|---|---|
| 1 | Khuyến nghị **không bật `ON_GOING`** như một giai đoạn chấm điểm | **Vẫn đúng** — nhưng thiếu một sự thật: hệ thống **đã có** cơ chế chấm trong-quá-trình cho từng sinh viên là `WeeklyAssessments`. Nó không phải giai đoạn chấm chính thức; nó là **bằng chứng** cho hội đồng Review 4. Xem §4 |
| 2 | Toàn bộ thành phần ở `Stage = DEFENSE`, điểm sinh ra một lần | **Sai.** Review 4 tách COLD rồi DEFENSE. Mỗi phạm vi có **hai** bộ tiêu chí khác nhau, vì COLD và DEFENSE đánh giá hai thứ khác nhau. Xem §2 |
| 3 | Một bộ tiêu chí cho mỗi phạm vi | **Không đủ.** Liên ngành cần **9 bộ / 11 thành phần**; đơn ngành cần **3 bộ / 3 thành phần**. Xem §5, §6 |

---

## 1. Nguyên tắc nền — hai bộ máy, không được lẫn

Hệ thống có **hai bộ máy độc lập**, và chúng không nói chuyện với nhau. Đây là điều đầu tiên hội đồng cần hiểu, vì mọi tranh cãi kiểu "sao làm xong hết việc mà vẫn chưa đạt" đều xuất phát từ việc lẫn hai thứ này.

| | Bộ máy Review Gate | Bộ máy Evaluation |
|---|---|---|
| **Dùng ở** | Review 1 · 2 · 3 | **Review 4** |
| **Sinh ra** | Quyết định *đạt / phải sửa* + nhận xét + danh mục việc phải khắc phục | **Điểm** → kết quả sinh viên, kết quả đồ án |
| **Ai tác động** | Người thẩm định được phân công | Evaluator được phân công |
| **Trọng số dùng để làm gì** | trọng số của từng mốc → **phần trăm tiến độ học thuật** | trọng số của thành phần → **phần trăm điểm** |
| **Code** | `ReviewGatesController` · `ReviewGateService` | `PolicyEvaluationController` · `EvaluationDraftWorkflow` · `VersionedResultCalculator` |

> 🔴 **Bất biến tuyệt đối:** trọng số tiến độ và trọng số điểm là **hai hệ số khác nhau, không bao giờ cộng hay nhân vào nhau**. Một nhóm có thể đạt 75% tiến độ học thuật (qua 3/4 mốc) mà **chưa có một điểm nào**, vì điểm chỉ sinh ra ở Review 4. Và ngược lại, điểm cao ở Review 4 không làm tiến độ nhảy lên.

**Vì sao thiết kế thế này là đúng:** nó tách *điều kiện được bảo vệ* khỏi *chất lượng khi bảo vệ*. Review 1–3 trả lời "nhóm này đã đủ điều kiện ra hội đồng chưa"; Review 4 trả lời "làm tốt đến mức nào". Gộp hai câu đó vào một con số là nguồn gốc của mọi tranh chấp điểm.

---

## 2. Vì sao COLD và DEFENSE phải là hai bộ tiêu chí khác nhau

Đây là lập luận cốt lõi của toàn bộ thiết kế, và là chỗ chống chấm trùng.

**COLD chấm *sản phẩm*. DEFENSE chấm *mức làm chủ sản phẩm của tác giả*.** Hai cấu trúc đánh giá khác nhau trên cùng một công trình:

| | COLD — chấm hồ sơ đã khóa | DEFENSE — bảo vệ trước hội đồng |
|---|---|---|
| Đối tượng đánh giá | Hiện vật: tài liệu, dữ liệu thô, mã nguồn, prototype | Con người: khả năng giải thích, chống đỡ phản biện, nhận giới hạn |
| Chấm khi nào | **Trước** buổi bảo vệ, trên gói đã khóa | Tại buổi bảo vệ |
| Chấm được bằng | Đọc, mở file, chạy thử, đối chiếu số liệu | Quan sát và vấn đáp |
| Không chấm được nếu | Thiếu hiện vật | Sinh viên không có mặt |
| Có thể làm lại | Không — gói đã khóa | Không |

**Hệ quả quan trọng:** vì tiêu chí của hai chặng khác nhau, **một năng lực chỉ xuất hiện ở đúng một ô**. Ví dụ "tích hợp liên ngành": COLD chấm *hợp đồng tích hợp có được viết và truy vết được không*; DEFENSE chấm *nhóm có giải thích được quyết định tích hợp khi bị chất vấn không*. Đó là hai việc khác nhau, **không phải chấm hai lần**.

Nếu dùng **cùng một bộ tiêu chí** cho cả hai chặng thì mới là chấm trùng — và tài liệu v5.0 đã cấm nguyên văn: *"Never double-apply 30/70 Stage and 50/30/20 Scope weights."*

---

## 3. Bốn mốc — vai trò từng mốc

Áp cho **cả hai hình thức**. Số mốc là **cấu hình của bộ môn**, không phải hằng số trong phần mềm.

| Mốc | Tên | Bộ máy | Sinh điểm? | Chặn mốc sau | Chặn bảo vệ | Trọng số tiến độ (đơn ngành / liên ngành) |
|---|---|---|---|---|---|---|
| **Review 1** | Đề cương & phạm vi | Gate | ❌ | ✅ | ✅ | 15% / 10% |
| **Review 2** | Nền tảng chuyên ngành & hợp đồng tích hợp | Gate | ❌ | ✅ | ✅ | 25% / 20% |
| **Review 3** | Tích hợp & kiểm chứng giữa kỳ | Gate | ❌ | ✅ | ✅ | 25% / 25% |
| **Review 4** | **Nghiệm thu: COLD → DEFENSE** | **Evaluation** | ✅ **100% điểm** | — | ✅ | 35% / 45% |

**Review 1–3 làm gì cụ thể:** nhóm nộp minh chứng theo danh mục của mốc → người thẩm định xác minh từng minh chứng (*nhận* / *từ chối*) → ra quyết định *đạt* hoặc *phải sửa* kèm nhận xét → nếu phải sửa thì sinh ra danh mục việc khắc phục, nhóm nộp lại, thẩm định lại. **Không có ô nhập điểm nào ở ba mốc này.**

**Chặn bảo vệ ở cả bốn mốc** nghĩa là: chưa đạt Review 1, 2 hoặc 3 thì **không được vào Review 4**. Đây là cách ba mốc đầu có sức mạnh mà không cần cho điểm.

---

## 4. Theo dõi cá nhân trong quá trình — và vì sao nó không phải điểm

Hệ thống **đã có** cơ chế này, và nó **cố ý** nằm ngoài kết quả chính thức.

**`WeeklyAssessments`** — `WeeklyAssessmentsController` + `WeeklyAssessmentService`:

- **Chỉ GVHD chính** được đánh giá — `WeeklyAssessmentService.cs:54`: *"Only the current Primary can assess students"*
- **Từng sinh viên, từng tuần**: có `StudentId`, `MajorId`, `WeekNumber`, `StartDate/EndDate`, `Scores[]`, `TotalScore`, `Version`
- Sinh viên phải thuộc **roster đã đóng băng** và team đang hoạt động (`:62`)
- Dùng **chính bộ máy rubric**: snapshot bộ tiêu chí theo từng phiếu, chỉ chấm tiêu chí lá
- Sinh viên xem được phiếu của chính mình (`:43`)
- 🔴 **`AffectsOfficialResult = false`** và `IsDemo = true`

**Và đây là chỗ then chốt:** `EvaluationDraftWorkflow.cs:116-117` chặn cứng — *"The current primary supervisor cannot evaluate the same project."* Cùng với `:114-115`: loại `EvaluationType = "SUPERVISOR"` — *"New SUPERVISOR evaluation assignments are retired"*.

⇒ **GVHD chính bị cấm chấm chính thức dự án mình hướng dẫn.** Không phải thiếu tính năng — đó là guard chống xung đột lợi ích, enforce ở phần mềm.

**Vậy hệ thống vận hành thế nào cho hợp lý:**

```
Cả kỳ:  GVHD chính chấm tuần cho từng sinh viên  →  tích lũy BẰNG CHỨNG
                                                     (không vào điểm)
                                                            │
Review 4:  hội đồng độc lập ĐỌC bằng chứng đó khi vấn đáp cá nhân
           → cho điểm cá nhân theo bộ tiêu chí IND-DEF
```

Phiếu tuần trở thành hồ sơ mà hội đồng mở ra ở mục "đóng góp đã xác nhận". **Người nuôi bằng chứng và người cho điểm là hai bên khác nhau** — đó chính là thứ làm điểm cá nhân có thể bảo vệ được trước khiếu nại.

**Mentor ngành thì KHÔNG bị cấm.** `git grep -in "mentor"` trong toàn bộ `Features/Evaluations` trả về **0**. Nên Mentor ngành **có thể** là evaluator — §7 dùng điều này.

---

## 5. HÌNH THỨC A — ĐƠN NGÀNH MARKETING

### 5.1. Cấu trúc điểm

Đơn ngành thì **cả đồ án là ngành đó**, nên không tách CHUNG / theo-ngành. Chỉ còn hai phạm vi: **toàn đồ án** và **cá nhân**.

| Thành phần | Phạm vi | Chặng | Trọng số điểm SV | Số người chấm | Bộ tiêu chí |
|---|---|---|---|---|---|
| Chấm hồ sơ Marketing | `COMMON` | `COLD` | **30** | **2** | `MKT1-COLD-2026A` |
| Bảo vệ đồ án | `COMMON` | `DEFENSE` | **50** | **3** | `MKT1-DEF-2026A` |
| Vấn đáp cá nhân | `INDIVIDUAL` | `DEFENSE` | **20** | **2** | `IND-DEF-2026A` |

**Kiểm tổng:** `30 + 50 + 20 = 100` ✅ · Theo chặng: **COLD 30 / DEFENSE 70** ✅ · Theo phạm vi: toàn đồ án 80 / cá nhân 20 ✅

**Vì sao cá nhân 20%:** giữ đúng con số tham chiếu của v5.0 (*kết quả sinh viên = 50% CHUNG + 30% ngành + 20% cá nhân*). Đơn ngành không có thành phần "theo ngành" riêng, nên 30% đó dồn vào phần toàn đồ án; phần cá nhân giữ nguyên 20 để không phá tính liên tục.

Trọng số điểm **đồ án**: COLD 37,5 · DEFENSE 62,5 · cá nhân **0** (theo v5.0: phần cá nhân không cộng vào điểm đồ án).

### 5.2. `MKT1-COLD-2026A` — chấm hồ sơ (30% điểm)

Năm tiêu chí, trọng số **giữ nguyên bản v5.0** (`Q-D13` còn chờ ký) để bộ môn Marketing dễ đối chiếu. Điểm tối đa 10 cho mọi tiêu chí lá.

| Mã | Tiêu chí | % trong bộ | **Hiệu lực trong điểm SV** | Chấm trên minh chứng |
|---|---|---:|---:|---|
| `MKT1C-RSCH-01` | Nghiên cứu thị trường & khách hàng | 25 | **7,50%** | thiết kế nghiên cứu, dữ liệu thô và phân tích |
| `MKT1C-STPO-02` | Phân khúc – mục tiêu – định vị | 20 | **6,00%** | tài liệu STP |
| `MKT1C-STRX-03` | Chiến lược & triển khai | 25 | **7,50%** | kế hoạch marketing, tài sản chiến dịch |
| `MKT1C-KPIV-04` | KPI & kiểm chứng | 20 | **6,00%** | tài liệu KPI và kiểm chứng |
| `MKT1C-INTG-05` | Liêm chính nghiên cứu & chất lượng hồ sơ | 10 | **3,00%** | đạo đức nghiên cứu, trích dẫn + toàn bộ gói |
| | **Tổng** | **100** | **30,00%** | |

### 5.3. `MKT1-DEF-2026A` — bảo vệ (50% điểm)

Bốn tiêu chí. Trọng số dồn vào chống đỡ bằng chứng, vì đó là chỗ phân biệt rõ nhất giữa sinh viên làm thật và sinh viên đọc slide.

| Mã | Tiêu chí | % trong bộ | **Hiệu lực trong điểm SV** |
|---|---|---:|---:|
| `MKT1D-PRES-01` | Trình bày & lập luận giải pháp | 20 | **10,00%** |
| `MKT1D-DATA-02` | **Mở dữ liệu thô và chống đỡ kết luận khi bị chất vấn** | 35 | **17,50%** |
| `MKT1D-DECI-03` | Giải thích quyết định STP/kênh khi đổi giả định | 25 | **12,50%** |
| `MKT1D-LIMT-04` | Phân biệt đo được vs dự báo; nhận giới hạn | 20 | **10,00%** |
| | **Tổng** | **100** | **50,00%** |

**Mô tả bốn mức của `MKT1D-DATA-02`** — tiêu chí nặng nhất toàn bộ đơn ngành, 17,5% điểm cuối:

| Mức | Dải gợi ý | Dấu hiệu quan sát được |
|---|---|---|
| Xuất sắc | 9 – 10 | Trả lời độc lập, đi thẳng câu hỏi; **mở đúng file dữ liệu của mình tại chỗ** để dẫn chứng; bị phản biện thì lập luận lại có cơ sở **hoặc thừa nhận đúng chỗ sai và nêu cách sửa**; không cần đồng đội nhắc |
| Tốt | 7 – 8,9 | Trả lời được phần lớn câu hỏi, có dẫn chứng; cần gợi ý nhỏ ở câu khó; xử lý phản biện tương đối |
| Cơ bản | 5 – 6,9 | Trả lời được câu "đã làm gì", khó với câu "vì sao / nếu khác thì sao"; dẫn chứng chung, không mở được tài liệu cụ thể; đồng đội bổ sung nhiều lần |
| Chưa đạt | 0 – 4,9 | Không trả lời được câu hỏi về chính nghiên cứu của nhóm; hoặc trả lời sai và không nhận ra khi được chỉ; hoặc đồng đội trả lời thay phần lớn |

*Câu hỏi mẫu cho hội đồng:* "Con số này lấy từ dữ liệu nào, cỡ mẫu bao nhiêu, chọn mẫu thế nào — mở file cho hội đồng xem." · "Nếu phân khúc mục tiêu phản hồi khác kỳ vọng, bạn đổi gì trong kế hoạch?" · "Đâu là kết quả đo được, đâu là dự báo?"

---

## 6. HÌNH THỨC B — LIÊN NGÀNH CNTT + MARKETING + THIẾT KẾ ĐỒ HỌA

### 6.1. Cấu trúc điểm — ma trận hai chiều, tổng 100 đúng một lần

Đây là chỗ dễ sai nhất. Hai hệ trọng số (theo chặng và theo phạm vi) **không được nhân nhau**; chúng phải được **giải vào từng ô** sao cho tổng bằng 100 một lần duy nhất.

| Phạm vi | COLD | DEFENSE | **Tổng theo phạm vi** |
|---|---:|---:|---:|
| **CHUNG** (toàn dự án) | 15 | 35 | **50** |
| **Theo ngành** (ngành của SV đó) | 15 | 15 | **30** |
| **Cá nhân** (chính SV đó) | **0** | 20 | **20** |
| **Tổng theo chặng** | **30** | **70** | **100** |

**Ma trận này thỏa đồng thời cả ba điều kiện:**

- Theo chặng: COLD 30 / DEFENSE 70 ✅
- Theo phạm vi: 50 / 30 / 20 — đúng con số tham chiếu v5.0 ✅
- Tổng 100 **một lần**, không nhân hai hệ số ✅

**Vì sao ô `COLD × Cá nhân = 0`:** gói hồ sơ đã khóa hầu như không có minh chứng phân định được từng sinh viên. Chính giao diện của hệ thống đã phải cảnh báo điều này — `EvaluatorAssignmentDetailPage.tsx:164`:

> *"Không có tài liệu trong gói đã khóa khớp phạm vi bài chấm này… tài liệu chung không tự trở thành minh chứng cá nhân."*

Ép chấm ô này sẽ tạo **đánh giá giả**. Điểm cá nhân vì vậy chỉ sinh ra ở buổi bảo vệ, dựa trên vấn đáp + bằng chứng tuần mà GVHD đã tích lũy.

### 6.2. Mười một thành phần

Mỗi ô × mỗi ngành là một thành phần. Sinh viên ngành CNTT chỉ tính 5 trong số đó.

| # | Thành phần | Phạm vi | Ngành | Chặng | Trọng số SV | Trọng số đồ án | Số người chấm | Bộ tiêu chí |
|---|---|---|---|---|---:|---:|---:|---|
| 1 | Hồ sơ chung | COMMON | — | COLD | 15 | 18,75 | **2** | `CMN-COLD-2026A` |
| 2 | Bảo vệ giải pháp chung | COMMON | — | DEFENSE | 35 | 31,25 | **3** | `CMN-DEF-2026A` |
| 3 | Hồ sơ ngành CNTT | MAJOR_SPECIFIC | SE | COLD | 15 | 8,33 | **2** | `SE-COLD-2026A` |
| 4 | Bảo vệ chuyên môn CNTT | MAJOR_SPECIFIC | SE | DEFENSE | 15 | 8,34 | **2** | `SE-DEF-2026A` |
| 5 | Hồ sơ ngành Marketing | MAJOR_SPECIFIC | MKT | COLD | 15 | 8,33 | **2** | `MKT-COLD-2026A` |
| 6 | Bảo vệ chuyên môn Marketing | MAJOR_SPECIFIC | MKT | DEFENSE | 15 | 8,33 | **2** | `MKT-DEF-2026A` |
| 7 | Hồ sơ ngành Thiết kế | MAJOR_SPECIFIC | GD | COLD | 15 | 8,33 | **2** | `GD-COLD-2026A` |
| 8 | Bảo vệ chuyên môn Thiết kế | MAJOR_SPECIFIC | GD | DEFENSE | 15 | 8,34 | **2** | `GD-DEF-2026A` |
| 9 | Vấn đáp cá nhân — SV ngành CNTT | INDIVIDUAL | SE | DEFENSE | 20 | 0 | **2** | `IND-DEF-2026A` |
| 10 | Vấn đáp cá nhân — SV ngành Marketing | INDIVIDUAL | MKT | DEFENSE | 20 | 0 | **2** | `IND-DEF-2026A` |
| 11 | Vấn đáp cá nhân — SV ngành Thiết kế | INDIVIDUAL | GD | DEFENSE | 20 | 0 | **2** | `IND-DEF-2026A` |

**Kiểm tổng cho một sinh viên ngành CNTT** — bộ thành phần đã lọc theo ngành em ấy:

```
#1 (15) + #2 (35) + #3 (15) + #4 (15) + #9 (20) = 100 ✅
```

> 🔴 **Rủi ro kỹ thuật phải test — đây là `BE-03` trong `BE_DEV_TASKS.md`:** `VersionedResultCalculator.Total()` cộng **mọi** thành phần được truyền vào và trả `null` nếu tổng ≠ 100 (`:64`). Bộ thành phần truyền vào cho một sinh viên **phải đã được lọc** còn `{CHUNG, ngành của em ấy, cá nhân của em ấy}`. Truyền cả thành phần của ngành khác ⇒ tổng = **215** ⇒ `null`.

**Không dùng được công thức `DEMO_50_30_20`:** nó yêu cầu đúng `1 + số_ngành × 2 = 7` thành phần (`VersionedResultCalculator.cs:31`). Cấu trúc này có 11 ⇒ phải dùng `COMPONENT_WEIGHTS`. Tin tốt: `COMPONENT_WEIGHTS` + `IsDemo = true` là **hợp lệ ngay hôm nay** — chạy thử được trước khi hội đồng ký, rồi chuyển sang chính thức khi có số văn bản.

### 6.3. Chín bộ tiêu chí

| Bộ tiêu chí | Ô | % điểm SV | Số tiêu chí | Chủ sở hữu |
|---|---|---:|---:|---|
| `CMN-COLD-2026A` | CHUNG × COLD | 15 | 6 | Bộ môn **chủ trì** |
| `CMN-DEF-2026A` | CHUNG × DEFENSE | 35 | 4 | Bộ môn **chủ trì** (cần đồng thuận liên bộ môn) |
| `SE-COLD-2026A` | CNTT × COLD | 15 | 5 | Bộ môn CNTT |
| `SE-DEF-2026A` | CNTT × DEFENSE | 15 | 3 | Bộ môn CNTT |
| `MKT-COLD-2026A` | Marketing × COLD | 15 | 5 | Bộ môn Marketing |
| `MKT-DEF-2026A` | Marketing × DEFENSE | 15 | 3 | Bộ môn Marketing |
| `GD-COLD-2026A` | Thiết kế × COLD | 15 | 5 | Bộ môn Thiết kế |
| `GD-DEF-2026A` | Thiết kế × DEFENSE | 15 | 3 | Bộ môn Thiết kế |
| `IND-DEF-2026A` | Cá nhân × DEFENSE | 20 | 4 | Bộ môn chủ trì — **dùng chung cả hai hình thức và cả ba ngành** |

**Tổng 38 tiêu chí**, nhưng **một sinh viên chỉ bị chấm 17 tiêu chí**:

```
6 (chung-COLD) + 4 (chung-DEFENSE) + 5 (ngành-COLD) + 3 (ngành-DEFENSE) + 4 (cá nhân) = 17
```

> Đây là con số hội đồng cần nghe, **không phải 38**.

### 6.4. `CMN-COLD-2026A` — hồ sơ chung (15% điểm)

| Mã | Tiêu chí | % trong bộ | **Hiệu lực** | Nguồn |
|---|---|---:|---:|---|
| `CMNC-PROB-01` | Xác định & kiểm chứng bài toán | 18 | 2,70% | v5.0 |
| `CMNC-INTG-02` | **Hợp đồng tích hợp & truy vết quyết định liên ngành** | 27 | 4,05% | v5.0 · Borrego *integration* |
| `CMNC-OUTC-03` | Kết quả tích hợp & kiểm chứng | 22 | 3,30% | v5.0 |
| `CMNC-FEAS-04` | Khả thi liên ngành & quản trị rủi ro | 13 | 1,95% | v5.0 |
| `CMNC-GOVN-05` | Hồ sơ quản trị & truy vết | 8 | 1,20% | v5.0 |
| `CMNC-COLL-06` | **Phối hợp & giao tiếp liên ngành** | 12 | 1,80% | 🆕 **`Q-N11`** — Borrego *teamwork*+*communication* · ABET (3)(5) |
| | **Tổng** | **100** | **15,00%** | |

### 6.5. `CMN-DEF-2026A` — bảo vệ giải pháp chung (35% điểm, ô nặng nhất)

| Mã | Tiêu chí | % trong bộ | **Hiệu lực** |
|---|---|---:|---:|
| `CMND-PRES-01` | Trình bày giải pháp tích hợp | 20 | 7,00% |
| `CMND-EVID-02` | **Chống đỡ kết luận bằng minh chứng khi bị chất vấn** | 35 | **12,25%** |
| `CMND-CROSS-03` | **Trả lời câu hỏi vượt ranh giới ngành mình** | 25 | 8,75% |
| `CMND-LIMT-04` | Nhận thức giới hạn giải pháp & hướng phát triển | 20 | 7,00% |
| | **Tổng** | **100** | **35,00%** |

`CMND-CROSS-03` là tiêu chí **riêng của liên ngành**, không có bản tương đương ở đơn ngành. Nó trả lời câu hỏi phân loại tốt nhất: **nhóm này thực sự học liên ngành, hay chỉ là ba nhóm đơn ngành đóng gói chung?**

*Mô tả bốn mức của `CMND-CROSS-03`:*

| Mức | Dải | Dấu hiệu |
|---|---|---|
| Xuất sắc | 9 – 10 | Thành viên ngành A trả lời được câu hỏi thuộc ngành B ở mức **ảnh hưởng tới phần việc của mình**; nêu đúng cái mình phụ thuộc vào ngành B và điều gì sẽ vỡ nếu ngành B đổi; chủ động chỉ sang đồng đội đúng lúc câu hỏi vượt phạm vi |
| Tốt | 7 – 8,9 | Trả lời được ở mức khái niệm; biết mình phụ thuộc gì; chuyển câu hỏi cho đồng đội hợp lý |
| Cơ bản | 5 – 6,9 | Biết ngành khác làm gì nhưng không nói được nó ảnh hưởng phần mình thế nào; mỗi câu hỏi liên ngành đều phải chuyển hết cho người khác |
| Chưa đạt | 0 – 4,9 | Không biết ngành khác đã làm gì trong chính dự án của mình; hoặc mô tả sai phần việc của đồng đội |

### 6.6. Ba cặp bộ tiêu chí theo ngành — `Q-N09` chờ ký

**CNTT** — neo vào ABET CAC Criterion 3 (2025–2026).

| `SE-COLD-2026A` (15%) | % | Hiệu lực | ABET |
|---|---:|---:|---|
| `SEC-REQS-01` Phân tích yêu cầu phần mềm | 15 | 2,25% | (1) |
| `SEC-ARCH-02` Kiến trúc & thiết kế dữ liệu | 20 | 3,00% | (2) |
| `SEC-IMPL-03` Hiện thực & tích hợp | 30 | 4,50% | (2) |
| `SEC-QUAL-04` Kiểm thử, bảo mật & độ tin cậy | 20 | 3,00% | (2)(4) |
| `SEC-DOCS-05` Tài liệu kỹ thuật & bàn giao | 15 | 2,25% | (3) |

| `SE-DEF-2026A` (15%) | % | Hiệu lực | ABET |
|---|---:|---:|---|
| `SED-TECH-01` Giải thích quyết định kỹ thuật & trade-off khi bị chất vấn | 40 | 6,00% | (1)(2) |
| `SED-DEMO-02` Chạy và chứng minh hệ thống tại chỗ | 30 | 4,50% | (2) |
| `SED-PROF-03` **Trách nhiệm nghề nghiệp: pháp lý, đạo đức, giấy phép, dữ liệu cá nhân** | 30 | 4,50% | 🆕 **`Q-N11`** — **(4)** |

**Marketing** — `MKT-COLD-2026A` dùng đúng 5 tiêu chí v5.0 (25/20/25/20/10); `MKT-DEF-2026A` gồm `MKTD-DATA-01` (40) · `MKTD-DECI-02` (30) · `MKTD-LIMT-03` (30).

**Thiết kế đồ họa** — neo vào NASAD BFA Communication Design (2022).

| `GD-COLD-2026A` (15%) | % | Năng lực NASAD |
|---|---:|---|
| `GDC-USRR-01` Nghiên cứu người dùng | 20 | *support design decisions with quantitative and qualitative research* |
| `GDC-IAFL-02` Kiến trúc thông tin & luồng người dùng | 20 | *plan the design process; construct narratives/scenarios* |
| `GDC-UIVS-03` Hệ thống thiết kế & giao diện | 20 | *fluency in formal design vocabulary* |
| `GDC-PROT-04` Prototype & kiểm chứng usability | 25 | *interpret research findings and apply them* |
| `GDC-HAND-05` Bàn giao & khớp bản triển khai | 15 | *purpose-based choices among creative approaches* |

| `GD-DEF-2026A` (15%) | % |
|---|---:|
| `GDD-RATI-01` Chống đỡ quyết định thiết kế bằng phát hiện nghiên cứu | 40 |
| `GDD-ITER-02` Chỉ ra vòng lặp đã sửa gì vì phát hiện nào | 30 |
| `GDD-ETHI-03` Đạo đức nghề & sở hữu trí tuệ | 30 |

### 6.7. `IND-DEF-2026A` — vấn đáp cá nhân (20%, dùng chung cả hai hình thức)

| Mã | Tiêu chí | % | **Hiệu lực** | Nguồn |
|---|---|---:|---:|---|
| `INDD-OWNK-01` | Làm chủ phần việc & kiến thức chuyên môn | 30 | 6,00% | v5.0 |
| `INDD-DEFN-02` | **Vấn đáp độc lập** | 35 | 7,00% | v5.0 |
| `INDD-CONT-03` | Đóng góp đã xác nhận & trách nhiệm | 20 | 4,00% | v5.0 · Aurand & Wakefield (2006) |
| `INDD-CRIT-04` | **Nhận thức giới hạn ngành mình** | 15 | 3,00% | 🆕 **`Q-N11`** — Borrego *critical awareness* |
| | **Tổng** | **100** | **20,00%** | |

**Minh chứng cho `INDD-CONT-03`** — đây là nơi phiếu tuần của GVHD được dùng: `WeeklyAssessments` của sinh viên đó cả kỳ · vòng đánh giá đồng đội · sổ đóng góp · cam kết nhóm đã ký.

> ⚠️ **Không được quy số đếm thành điểm.** v5.0 ghi rõ chỉ số đóng góp *"is not a multiplier of the 50/30/20 StudentResult"*. Hội đồng **đọc** phiếu tuần và sổ đóng góp rồi **đánh giá chất lượng và độ khó**, không cộng số task.

---

## 7. VẬN HÀNH — ai chấm gì, khi nào

### 7.1. Ràng buộc từ phần mềm và từ luật — không thương lượng được

| # | Ràng buộc | Nguồn |
|---|---|---|
| R1 | Evaluator phải là **giảng viên đang hoạt động thuộc một bộ môn của dự án** | `EvaluationDraftWorkflow.cs:110-113` |
| R2 | **GVHD chính bị cấm** chấm chính thức dự án mình hướng dẫn | `:116-117` |
| R3 | `EvaluationType = "SUPERVISOR"` **đã khai tử** | `:114-115` |
| R4 | **Mentor ngành KHÔNG bị cấm** — dùng được làm evaluator | grep `mentor` trong `Features/Evaluations` ⇒ 0 |
| R5 | Bộ tiêu chí phải **đã công bố** trong phạm vi dự án | `:95`, `:118-124` |
| R6 | Thiếu bất kỳ evaluator nào ⇒ **kết quả = `null`**, không phải 0, **không chia lại trọng số** | `VersionedResultCalculator.cs:54` |
| R7 | Bảo vệ **trực tuyến**: hội đồng **≥ 3 thành viên** · **đồng thuận của cả hội đồng và sinh viên** · **ghi hình ghi âm đầy đủ và lưu trữ** | **Thông tư 08/2021/TT-BGDĐT Điều 9 khoản 1** |
| R8 | Mỗi đồ án đánh giá bởi **≥ 2 giảng viên** | QĐ 43/2007 Điều 25, sửa bởi TT 57/2012 — tiền lệ |

**Phân biệt quan trọng cho hội đồng:** R7 quy định **hội đồng** phải ≥3 người. Nó **không** quy định mỗi thành phần điểm phải 3 người chấm. Nên 2 người chấm cho các ô theo-ngành và cá nhân là hợp lệ, miễn hội đồng được triệu tập có ≥3 thành viên và tất cả đồng thuận hình thức trực tuyến.

> 🔴 **Và xem `Q-T4`:** hệ thống hiện **không có chức năng ghi hình và lưu trữ**. Chưa có thì **không nên tổ chức bảo vệ trực tuyến**.

### 7.2. Phân công giảng viên — liên ngành CNTT + Marketing + Thiết kế

**Hội đồng Review 4 (4 người):** 1 chủ tịch (bộ môn chủ trì) + 1 đại diện mỗi ngành. Thỏa R7. Chủ tịch có thể trùng đại diện ngành chủ trì nếu bộ môn cho phép — khi đó hội đồng 3 người.

| Thành phần | Ai chấm | Số người | Vì sao thế |
|---|---|---:|---|
| CHUNG × COLD | 1 GV bộ môn chủ trì + 1 GV bộ môn tham gia | 2 | Phán xét về tích hợp không được là của một ngành duy nhất |
| CHUNG × DEFENSE | **Cả hội đồng** | 3 | Mọi thành viên đều dự buổi bảo vệ chung; R7 |
| Ngành X × COLD | 2 GV bộ môn của ngành X | 2 | R8; chỉ bộ môn đó đủ chuyên môn |
| Ngành X × DEFENSE | Đại diện ngành X + 1 trong 2 người chấm COLD của ngành X (có dự) | 2 | Người chấm hồ sơ dự buổi bảo vệ ⇒ nối được hiện vật với lời giải thích |
| Cá nhân (từng SV) | Đại diện ngành của SV đó + **chủ tịch** | 2 | Đại diện ngành hiểu nội dung; chủ tịch đảm bảo nhất quán giữa các sinh viên |

**Khối lượng thật — con số hội đồng sẽ hỏi.** Dự án liên ngành 6 sinh viên (2 mỗi ngành):

| Hạng mục | Số phiếu phải chốt | Số tiêu chí mỗi phiếu |
|---|---:|---:|
| CHUNG × COLD | 2 | 6 |
| CHUNG × DEFENSE | 3 | 4 |
| 3 ngành × COLD | 6 | 5 |
| 3 ngành × DEFENSE | 6 | 3 |
| 6 SV × cá nhân | 12 | 4 |
| **Tổng** | **29 phiếu** | — |

Trong đó **8 phiếu COLD** (chấm trước, không áp lực thời gian) và **21 phiếu DEFENSE**. Phần DEFENSE chia cho 4 người: chủ tịch 9 phiếu, mỗi đại diện ngành 4 phiếu. Làm được trong một buổi — **nhưng chỉ nếu phiếu gọn**, nên mẫu phiếu ở §8 là yếu tố quyết định "áp dụng được không".

### 7.3. Phân công — đơn ngành Marketing

**Hội đồng Review 4 (3 người):** chủ tịch + 2 thành viên, tất cả bộ môn Marketing, **không ai là GVHD chính của nhóm**.

| Thành phần | Ai chấm | Số người |
|---|---|---:|
| COLD (hồ sơ) | 2 GV bộ môn Marketing | 2 |
| DEFENSE (bảo vệ) | Cả hội đồng | 3 |
| Cá nhân | Chủ tịch + 1 thành viên | 2 |

Nhóm 4 sinh viên: `2 + 3 + 4×2 = 13 phiếu`. Nhẹ hơn liên ngành đáng kể.

### 7.4. Trình tự thời gian

```
Cả kỳ ──────────────────────────────────────────────────────────────────
  GVHD chính: phiếu tuần cho từng SV  →  BẰNG CHỨNG (không vào điểm)
  Mentor ngành: theo dõi trong phạm vi ngành mình

Review 1 ──► Gate: nộp minh chứng → xác minh → đạt / phải sửa + nhận xét
Review 2 ──► Gate: như trên
Review 3 ──► Gate: như trên
   │  Chưa đạt R1/R2/R3  ⇒  KHÔNG được vào Review 4
   ▼
Review 4 ═══════════════════════════════════════════════════════════════
  Bước 1  Nhóm khóa gói hồ sơ — sau đó không sửa được
  Bước 2  Bộ môn phân công evaluator cho 8 phiếu COLD
          ⇒ hệ thống CHẶN nếu chọn GVHD chính
  Bước 3  Chấm COLD trên hồ sơ đã khóa  →  lưu nháp  →  CHỐT
  Bước 4  Buổi bảo vệ. Nếu trực tuyến: hội đồng ≥3, đồng thuận của cả
          hội đồng và sinh viên, ghi hình ghi âm và lưu trữ (R7 · Q-T4)
          4a. Bảo vệ giải pháp chung      → cả hội đồng chấm
          4b. Vấn đáp chuyên môn từng ngành → đại diện ngành + người chấm COLD
          4c. Vấn đáp cá nhân từng SV     → đại diện ngành + chủ tịch
                                             (mở phiếu tuần + sổ đóng góp)
  Bước 5  Chốt toàn bộ phiếu DEFENSE
  Bước 6  Hệ thống tính kết quả
          ⇒ thiếu BẤT KỲ phiếu nào  ⇒  kết quả = null (CHỜ), không phải 0
  Bước 7  Bộ môn công bố
          ⇒ chưa có số văn bản  ⇒  hiển thị DEMO, không phải điểm chính thức
```

**Hai chốt an toàn hội đồng nên biết:**

1. **Chốt phiếu không phải công bố điểm.** Công bố là hành vi riêng của bộ môn.
2. Chừng nào chưa có **số văn bản phê duyệt thật** thì hệ thống **vẫn gắn nhãn DEMO** trên mọi màn kết quả, và phần mềm **chặn không cho đi ngược** (`VersionedResultCalculator.cs:17`).

---

## 8. Phiếu chấm — thứ quyết định "áp dụng được không"

29 phiếu × 3–6 tiêu chí là làm được. 29 phiếu × 6 tiêu chí × 4 đoạn mô tả in kèm là **không**. Nên:

- **Phiếu chấm:** một dòng một tiêu chí — mã · tên · trọng số trong bộ · **tỷ trọng trong điểm sinh viên** · ô điểm `/10` · ô nhận xét. **Vừa một trang.**
- **Bảng tra mức chất lượng:** một trang riêng, phát chung cho hội đồng, chứa 4 mức của mọi tiêu chí trong phiên đó. **Không in lại trên từng phiếu.**
- **Trên màn hình:** mô tả 4 mức **thu gọn** cạnh ô điểm, bấm mở — không chiếm chỗ mặc định.

**Hai con số phải hiện cạnh nhau trên mọi phiếu**, và hiện đang là một lỗ hổng của giao diện: `rubrics-api.ts:4-7` **bỏ cả `parentId` và `effectiveWeightPercent`** mà phần mềm đã gửi, nên hôm nay không hiển thị được tỷ trọng hiệu lực. Đây là ticket `F2` trong `FE_DEV_TASKS.md`.

---

## 9. Những gì còn lại

**Đã quyết trong tài liệu này** — là lựa chọn của nhóm phát triển, hội đồng có thể đổi:

- Review 1–3 không sinh điểm; Review 4 = COLD + DEFENSE
- Ma trận liên ngành 15/35 · 15/15 · 0/20 (thỏa cả 30/70 và 50/30/20)
- Đơn ngành 30 / 50 / 20
- `COLD × Cá nhân = 0`
- 9 bộ / 11 thành phần cho liên ngành; 3 bộ / 3 thành phần cho đơn ngành
- Số người chấm: DEFENSE-CHUNG 3, còn lại 2
- Dải điểm gợi ý 9–10 / 7–8,9 / 5–6,9 / 0–4,9 — **gợi ý, không phải ngưỡng đạt**

**Chưa soạn, cần làm tiếp:** mô tả 4 mức cho **toàn bộ 38 tiêu chí** (tài liệu này có 2 bộ mẫu đầy đủ: `MKT1D-DATA-02` và `CMND-CROSS-03`); câu hỏi mẫu theo ngành cho hội đồng; bảng tra mức in được.

### 9.1. Bốn chỗ nhóm phát triển tự chốt — ghi ra để soát

| # | Chỗ hở | Chốt |
|---|---|---|
| 1 | Hội đồng 4 người nhưng CHUNG × DEFENSE ghi 3 người chấm — ai là 3 người đó? | **Số người chấm = đúng số thành viên hội đồng được phân công**, không phải hằng số. Hội đồng 3 ⇒ 3; hội đồng 4 ⇒ 4. Lý do: R6 trả `null` khi thiếu bất kỳ phiếu nào, nên con số này phải khớp thực tế, nếu không kết quả treo vĩnh viễn. Khuyến nghị triệu tập **3 người** cho gọn |
| 2 | Đơn ngành: 2 người chấm COLD có được trùng hội đồng 3 người? | **Được, và nên trùng.** Người đã đọc hồ sơ sẽ hỏi được câu hỏi có dẫn chứng ở buổi bảo vệ |
| 3 | `WeeklyAssessments` dùng bộ tiêu chí nào? | **Dùng chính `IND-DEF-2026A`.** Phiếu tuần và vấn đáp cá nhân cuối kỳ khi đó cùng một trục tiêu chí, nên bằng chứng cả kỳ đọc được trực tiếp vào đúng ô hội đồng đang chấm. Không phải bộ thứ 10 |
| 4 | Dự án liên ngành chỉ 2 ngành thì ma trận có đổi? | **Không đổi.** Giữ 15/35 · 15/15 · 0/20; chỉ bớt số thành phần (2 ngành ⇒ 8 thay vì 11). Tổng cho một sinh viên vẫn 100 |

### 9.2. Chặn bởi hội đồng — xem `CAU_HOI_BO_MON_2026-10-30.md`

| ID | Câu hỏi | Mặc định của tài liệu này |
|---|---|---|
| `Q-D13` | Bộ tiêu chí Marketing 25/20/25/20/10 | dùng nguyên |
| `Q-N09` | Bộ tiêu chí CNTT và Thiết kế | trình hai bộ môn |
| `Q-N11` | Ba tiêu chí mới `CMNC-COLL-06` · `INDD-CRIT-04` · `SED-PROF-03` | đề xuất chấp nhận |
| `Q-A9` | **Ngưỡng đạt** | **chưa đề xuất** |
| `Q-A10` | **4 mức hay 5 mức** | 4 mức — 🔴 **chặn schema** |
| `Q-N02` | Ai sở hữu / ai duyệt bộ tiêu chí | 🔴 **chặn phân quyền** |
| `Q-T4` | Ghi hình bảo vệ trực tuyến | chưa đề xuất — cần pháp chế |

---

## Verification — cách kiểm chứng tài liệu này

```bash
cd ai-pms-backend && SHA=14eecc7a04a1b91bb4c491c5ca85b09edbb99f94

# 1. GVHD chính bị cấm chấm + loại SUPERVISOR type
git show $SHA:src/AIPMS.Application/Features/Evaluations/Services/EvaluationDraftWorkflow.cs | sed -n '110,120p'
# kỳ vọng: EvaluatorIneligible · SUPERVISOR_EVALUATION_TYPE_RETIRED · PRIMARY_EVALUATOR_CONFLICT

# 2. Mentor KHÔNG bị cấm
git grep -in "mentor" $SHA -- src/AIPMS.Application/Features/Evaluations   # kỳ vọng: rỗng

# 3. Phiếu tuần chỉ Primary, không vào kết quả
git show $SHA:src/AIPMS.Infrastructure/Services/Projects/WeeklyAssessmentService.cs | sed -n '54p'

# 4. COMPONENT_WEIGHTS cần tổng đúng 100; thiếu evaluator ⇒ null
git show $SHA:src/AIPMS.Application/Features/Evaluations/Services/VersionedResultCalculator.cs | sed -n '52,66p'

# 5. DEMO_50_30_20 không dùng được cho 11 thành phần
git show $SHA:src/AIPMS.Application/Features/Evaluations/Services/VersionedResultCalculator.cs | sed -n '31,38p'

# 6. Rubric chưa có trường mô tả mức  (→ BE-06)
git grep -in "descriptor" $SHA -- src/AIPMS.Application/Features/Evaluations   # kỳ vọng: rỗng

# 7. Nhãn chính thức đòi số văn bản thật
git show $SHA:src/AIPMS.Application/Features/Evaluations/Services/VersionedResultCalculator.cs | sed -n '17p'
```

**Kiểm chứng vận hành — chưa làm được, cần người có quyền trên staging:** dựng một scheme 11 thành phần, phân công thử, và chạy một phiếu COLD + một phiếu DEFENSE để xem tổng có ra 100 và thiếu phiếu có trả *chờ* đúng không. Đây là `BE-03` trong `BE_DEV_TASKS.md`, hiện **chưa có test nào**.
