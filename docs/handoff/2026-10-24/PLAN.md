# KẾ HOẠCH ĐIỀU PHỐI — AI-PMS về trạng thái Complete

**Ngày lập:** 24/10/2026 · **Nguồn:** self-test 24/10/2026 + đọc lại trực tiếp source BE và FE
**Đội:** 1 BE dev · 2 FE dev · tất cả dùng AI coding agent

> ### Bản sửa 24/10 — sau khi nhận package self-test đầy đủ
>
> Bản đầu của kế hoạch này dựng từ **file HTML đơn lẻ**. Sau đó nhận được **package đầy đủ** gồm `results.json`, `ROADMAP.md`, `VALIDATION.md`, `COVERAGE.md`, `FIXTURE_LEDGER.md`, `PHOTO_INDEX.csv`, `photos.json` và 272 ảnh. Đối chiếu lại, mọi số liệu khớp chính xác, nhưng **bốn** điểm phải sửa:
>
> | # | Bản đầu nói | Thực tế theo `results.json` + source |
> |---|---|---|
> | 1 | BE-01 (FUNC-08) phải xây mô hình slot mới + thêm endpoint, **scope L** | Mô hình slot **đã có và đã đúng** ở enforcement. Chỉ là capability viết sai luật. **Scope S/M**, không endpoint mới |
> | 2 | Review Gates có thể đã test trên dữ liệu giả — "vùng tối lớn nhất" | `baseline.reviewGatesMock: false` — **đã test bằng API thật**. Bỏ nghi vấn, G7 thu lại còn việc đổi mặc định flag |
> | 3 | 20 vấn đề OPEN, **6 mức HIGH** | **8 mức HIGH**. Con số 6 là shortlist "Ưu tiên xử lý tiếp" của báo cáo, thiếu ENV-04 và FUNC-01 |
> | 4 | Đợt test chạy với "cấu hình trống" | Cấu hình **có chủ đích và được ghi lại** (`baseline.flags`). Nhưng vì sao tắt thì chưa ai nói → **câu hỏi Q6** |
>
> Thêm **một task mới**: **BE-23** — ghim capability↔enforcement, xuất phát từ nguyên nhân lớp dưới của FUNC-08 (xem mục 3.6).

---

## 1. File nhiệm vụ

| Người | File | Số task |
|---|---|---|
| **Mọi dev — đọc trước** | [DECISIONS.md](DECISIONS.md) | 6 quyết định nghiệp vụ, chốt 24/10 |
| BE dev | [BE_DEV_TASKS.md](BE_DEV_TASKS.md) | 23 |
| FE dev 1 | [FE_DEV_1_TASKS.md](FE_DEV_1_TASKS.md) | 8 |
| FE dev 2 | [FE_DEV_2_TASKS.md](FE_DEV_2_TASKS.md) | 10 |

**Tổng 41 task.** Mỗi file tự chứa đủ ngữ cảnh — một dev (hoặc agent của họ) đọc **một** file là làm được, không cần đọc file của người khác.

---

## 2. Điểm xuất phát

| Chỉ số | Giá trị |
|---|---|
| Ca test | 92 — 82 PASS · **9 FAIL** · **1 BLOCKED** |
| UC của Report 3 | 134 — **0 UC đạt đầy đủ bằng chứng** |
| UC chưa một ca test nào chạm tới | **25** (19%) |
| Vấn đề OPEN | **20** (**8 mức HIGH**: ENV-04, FUNC-01, FUNC-03, FUNC-04, FUNC-05, FUNC-07, FUNC-08, FUNC-09) |
| Module v5 không có UC nào trong danh sách 134 | **14** |
| Nhánh có code nhưng chưa PR, chưa chạy CI, 0 test | **2** (BE +605 dòng có migration DB · FE +49 dòng) *(Đã merge vào develop ngày 24/10)* |

---

## 3. Bản đồ phụ thuộc liên đội

```
NGÀY 1 — song song, không ai chờ ai
├── BE: BE-05 (AuthUserDto +Title/Phone)   ← FE1 F5 chờ cái này
├── BE: BE-22 (TeamInvitationDto +tên)     ← FE1 F6 chờ cái này
├── BE: BE-03 (CORS origin)                 ← độc lập
├── FE1: F1, F2, F3, F7                     ← không chờ BE
└── FE2: G9, G7                             ← không chờ BE

TUẦN 1
├── BE: BE-01 (FUNC-08 capability drift) ← scope S/M, LÀM TRƯỚC TIÊN
├── BE: BE-02 (email), BE-04 (DateTime UTC), BE-11, BE-21
├── FE1: F4 (nộp báo cáo — mở 5 UC)
└── FE2: G1, G6, G5, G4

TUẦN 2
├── BE: Phase 2 (BE-06, BE-07 — ĐÃ CHỐT Q1, Q2), BE-19 (seed Design)
├── BE: BE-15 (evaluator đa scope)   ← FE2 G3 chờ mục 3 của task này
├── FE1: F5 (sau BE-05), F6 (sau BE-22)
└── FE2: G2, G3 (phần công thức sau BE-15)

TUẦN 3
├── BE: Phase 3 (BE-08 risk alert, BE-09 prefill, BE-10, BE-16)
├── BE: BE-13 (đã merge PR #132)
└── FE: G8 (đã merge PR #100)

TUẦN 4 — nghiệm thu
├── BE: Phase 4 (BE-12, BE-14, BE-17, BE-18, BE-20)
├── FE1: F8 (nghiệm thu liên role 22 bước)
└── FE2: G10 (nghiệm thu 5 kịch bản)
```

---

## 4. Định nghĩa "Complete" cho toàn dự án

### Kỹ thuật
- [ ] BE: `dotnet build -warnaserror` + `dotnet test` xanh trên `develop`
- [ ] FE: `typecheck` + `lint` + `test` (toàn bộ 261 file) + `build` xanh trên `develop`
- [ ] **Không còn nhánh lạc**: cả 2 nhánh AI đã merge
- [ ] `/api/v1/system` trả commit SHA, xác nhận staging đúng commit

### Chức năng
- [ ] **9 ca FAIL + 1 BLOCKED đều PASS**: ADM-29 · COM-05 · STU-13 · STU-15 · EXE-16 · EXE-20 · LEC-23 · HYB-15 · COM-11 · LEC-30
- [ ] **20 vấn đề OPEN** mỗi mục là một trong ba: đã sửa · đã có quyết định nghiệp vụ bằng văn bản · đã ghi rõ không làm và vì sao
- [ ] **25 UC chưa có bằng chứng** đều có kết luận thật
- [ ] **13 UC AI** không còn mục nào ở trạng thái "chặn vì cấu hình"
- [ ] **4 UC chặn phụ thuộc** (UC-078/079/080/081) đã nghiệm thu được sau khi FUNC-05 sửa

### Luồng end-to-end
- [ ] Lifecycle chạy **một lần liên tục, qua UI, trên staging thật** cho:
  - [ ] `SINGLE_MAJOR` — IT
  - [ ] `SINGLE_MAJOR` — Marketing
  - [ ] `INTERDISCIPLINARY` — IT + BA(Marketing) + Design
- [ ] Mọi bước qua **UI thật**, không bước nào dựng fixture bằng API
- [ ] Bước cuối (**công bố điểm chính thức** và **Student đọc được điểm**) đã chạy
