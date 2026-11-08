# BÀN GIAO ĐỢT 30/10/2026 — AI-PMS

> Lưu trữ lịch sử: các thứ tự và trạng thái bên dưới chỉ đúng tại thời điểm 30/10/2026. Prompt copy/paste cho agent đã được bỏ khỏi cây file vì chứa SHA và bước khởi động cũ; bản gốc vẫn có trong Git history. Bàn giao Gate 3/4 hiện hành ở [đây](../BE16_GATE3_GATE4_FE_E2E_UAT_20261108.md).

**Mục tiêu đợt này:** hệ thống **chạy thật được một kỳ**. Không làm bản demo dùng một lần.

**Baseline:** BE `14eecc7a04a1b91bb4c491c5ca85b09edbb99f94` · FE `e8e9801c401bc810ce19299748c332af4f36aec0` — cả hai CI xanh, 0 PR mở, chốt lúc viết tài liệu.

---

## Ai đọc gì

| File | Người nhận | Đọc để làm gì |
|---|---|---|
| **`BE_DEV_TASKS.md`** | BE dev | 9 ticket, 4 phase. Tự chứa — không cần mở file nào khác để code |
| **`FE_DEV_TASKS.md`** | FE dev | 11 ticket, 3 phase. Tự chứa |
| **`CAU_HOI_BO_MON_2026-10-30.md`** | Bộ môn / hội đồng học vụ | 8 quyết định. Hai cái đầu **chặn BE viết dòng code đầu tiên** của ba ticket rubric |
| **`RUBRIC_DESIGN_2026-10-30.md`** | Bộ môn + cả hai dev | Thiết kế rubric 4 mốc, ma trận trọng số, phân công giảng viên. Trạng thái `PROPOSED` |
| **`COVERAGE_MATRIX_2026-10-30.md`** | Cả hai dev | 116 ID (34 REQ · 36 FL · 32 WF · 14 T) — cái gì dùng lại, cái gì mở rộng, cái gì chờ |

---

## Thứ tự nên làm

```
NGAY HÔM NAY
  → gửi CAU_HOI_BO_MON cho bộ môn. Hai mục Q-A10 và Q-N02 chặn đường găng.
  → giao BE_DEV_TASKS.md và FE_DEV_TASKS.md.

BE bắt đầu được NGAY với PHASE 0 (BE-01, BE-02, BE-03) — không chờ bộ môn.
FE bắt đầu được NGAY với PHASE 0 (F1…F6) — không chờ BE, không chờ bộ môn.

CHỜ BỘ MÔN KÝ mới merge được: BE-06, BE-07, BE-08 và F8.
```

---

## Ba quy tắc của bộ tài liệu này

### 1. Hai khối giống nhau từng chữ — sửa một bên phải sửa bên kia

`BE_DEV_TASKS.md` và `FE_DEV_TASKS.md` **lặp lại nguyên văn** hai khối:

- **14 bất biến nghiệp vụ** `N1`–`N14` (mục 0.5 của BE · mục 0.6 của FE)
- **10 quy tắc chống nhầm lẫn BE↔FE** `X1`–`X10` (mục 0.7 của BE · mục 0.8 của FE)

Cố ý lặp. Người nhận chỉ mở file của mình; đặt hai khối đó ở một file thứ ba là đặt vào chỗ không ai đọc. Cái giá là hai bản có thể trôi lệch, nên: **hai khối đó là bản sao bắt buộc giống nhau. Sửa một bên thì phải sửa bên kia cùng lúc.**

### 2. Ticket dán nhãn 🔴 CHỜ VĂN BẢN: code được, mở PR được, KHÔNG merge

`BE-06` · `BE-07` · `BE-08` (phía BE) và `F8` (phía FE) phụ thuộc quyết định bộ môn chưa có. Chúng được đặc tả đầy đủ để làm trước và review trước, nhưng:

> **Không merge khi chưa có chữ ký.** Backend chặn cứng ở `VersionedResultCalculator.cs:17` — `IsDemo = false` đòi `ApprovalStatus = "APPROVED"` **và** `ApprovalReference` không rỗng. **Không ai được điền số văn bản giả.** Khi bộ môn ra văn bản, việc cần làm chỉ là **nhập số văn bản** — một thao tác dữ liệu, không phải sửa code.

### 3. Mức bằng chứng của bộ tài liệu này

Toàn bộ ở mức `VERIFIED_SOURCE` + `CI_VERIFIED`: mọi trích dẫn `file:dòng` đã được đọc lại từ source tại đúng hai SHA trên, và kèm lệnh `git grep` để chạy lại.

> ⚠️ **Không có một dòng `BROWSER_VERIFIED` nào.** Chưa ai chạy app từ hai SHA này, chưa đăng nhập vai nào, chưa đọc DB staging. Mọi mô tả hành vi runtime trong bộ tài liệu này là **suy ra từ source**, không phải quan sát. Gặp chỗ source nói một đằng app làm một nẻo thì app đúng — báo lại ngay, đừng tự sửa tài liệu.

---

## Nguồn

Bộ tài liệu này được tách từ plan chủ, kèm bốn tài liệu nền:

| Nguồn | Hiệu lực |
|---|---|
| `AI-PMS_FULL_Requirements_Workflows_UIUX_Claude_Plan_v3.0_2026-10-30.md` | Đề bài — phạm vi 34 REQ · 36 FL · 32 WF · 14 T |
| Audit v1.1 (plan chủ) | Hiện trạng AS-IS và bằng chứng |
| `RUBRIC_DESIGN_2026-10-30.md` (file này) | Có thẩm quyền cho thiết kế chấm điểm |
| `../../templates/AI-PMS_Bo_Mau_Hoc_Thuat_MKT_va_Lien_Nganh_2026-10-29.md` | Còn hiệu lực **trừ §3.3 và §4.3** — đã thay bằng `RUBRIC_DESIGN_2026-10-30.md` |
| `../../process/AI-PMS_Quy_Trinh_Boc_Tach_Va_Phat_Trien_Chuc_Nang_2026-10-30.md` | Còn hiệu lực toàn bộ — 4 làn, Contract Card, 7 cổng DoD |

Đợt trước ở `../2026-10-24/`. Đợt này là thư mục riêng vì baseline, plan và mục tiêu đều đã khác.
