# Nhiệm vụ Backend — DongVV (đánh giá, kết quả, gate, AI)

Người phối hợp: **KhaiNQ** (học vụ, phân quyền, dữ liệu) · FE lead (giao diện) · Người duyệt: Vo Van
Nguồn: `AI-PMS_Ke_hoach_Hoan_thien_2026-10-09.md` mục 4 và 6 · Chuẩn nghiệp vụ: CIB v4.0 > SRS
Mã nguồn tham chiếu: `ai-pms-backend` `develop` @ `4c61c98`

## 0. Quy ước chung (giống KhaiNQ)
1. Nhánh `feat/BE-xx-<mo-ta>` từ `develop`; PR tiêu đề `BE-xx: …`; 1 người review (KhaiNQ hoặc Vo Van).
2. **DB-first:** script additive chạy lại được trong `db/changes`, thêm vào `db/e2e/migrations.json`, cập nhật `db/schema.sql`, scaffold lại model.
3. **Hợp đồng trước:** controller + DTO + `docs/<tinh-nang>.md` (request, response, mã lỗi, ví dụ) trong cùng PR. FE nối sau khi merge.
4. **Điểm số luôn do BE tính.** Thiếu điểm = `PENDING`, không bao giờ = 0 (BRX-EVAL-04). Kết quả đã công bố chỉ đổi qua version đính chính (BRX-RESULT-04).
5. Test: unit cho công thức (golden cases), integration cho quyền (1 được phép, 2 bị từ chối), test đồng thời cho finalize/publish (idempotent).
6. Báo FE trước khi đổi field, enum, URL hoặc mã lỗi.

## 1. Sprint 1 (12/10 – 23/10)

### BE-03 · Lead Department công bố kết quả liên khoa — P0, 3 điểm (**đã chốt 09/10/2026**)
- **Vấn đề:** `EvaluationSchemeService.Results.cs:36-43` chỉ cho Admin công bố kết quả liên khoa (blocker `ADMIN_REQUIRED_FOR_CROSS_DEPARTMENT_PUBLICATION`). CIB §3: Admin không có quyền học thuật.
- **Làm:**
  - Bỏ nhánh `actor.IsAdmin`. Người công bố ProjectResult = nhân viên **Lead Department** của phạm vi đã đóng băng.
  - Điều kiện: mọi khoa tham gia đã có quyết định (BRX-REVIEW-01) và mọi assignment bắt buộc đã FINALIZED.
  - StudentResult: nhân viên khoa của ngành sinh viên được preview; công bố do Lead Department.
  - Blocker mới: `LEAD_DEPARTMENT_REQUIRED`, `PARTICIPATING_DECISION_MISSING`.
- **Test:** Admin 403, Lead 200, Participating 403 khi công bố, khoa ngoài 404.
- **Báo FE:** sau khi merge, FE gỡ CTA công bố trên route Admin (PR #53) và mở cho Lead.

### BE-02 (phần chấm điểm) · Seed scheme/assignment cho 3 kịch bản — P0, 3 điểm
- Nối tiếp script của KhaiNQ: rubric đã publish, scheme đã publish cho A/B/C, assignment COMMON + MAJOR + INDIVIDUAL, gói bàn giao đã khóa, một số bài chấm nháp để FE thấy trạng thái PENDING.
- Golden data: bảng điểm đầu vào và kết quả mong đợi (`docs/samples/golden-scoring.md`).

## 2. Sprint 2 (26/10 – 06/11)

### BE-09 · DTO kết quả của chính sinh viên — P0, 3 điểm
- **Vấn đề:** `EvaluationSchemeService.Results.cs:152` trả `SnapshotJson = "{}"` cho sinh viên; FE không có tên scheme và thành phần điểm.
- **Làm:** thêm trường typed `schemeName`, `schemeVersion`, `components[] { scope, majorName?, weightPercent, score|null, status }`, `passThreshold`, `outcome`, `publishedAt`. Không lộ điểm sinh viên khác, không lộ audit.
- **Test:** chính chủ 200; sinh viên khác 403/404; chưa công bố 404; hai sinh viên cùng ngành có điểm cá nhân khác nhau.

### BE-17 · Một mô hình kết quả duy nhất — P1, 3 điểm
- Chọn `evaluation_schemes` + `student_results` làm chuẩn. `project_result_policies`, `project_result_policy_items`, `project_results`, `project_result_evaluations` chuyển sang chế độ chỉ đọc (LEGACY), đánh dấu trong tài liệu và DTO (`isLegacy`).
- Endpoint `ProjectResultsController` (`result-policy`) chỉ đọc cho dữ liệu cũ.

### BE-08 · Minh chứng theo phạm vi người chấm — P1, 3 điểm
- `GET /api/v1/evaluation-assignments/{id}/evidence` (`EvaluationAssignmentAccessService.cs`) trả đủ danh sách item/version/file của gói đã khóa **trong phạm vi** component/ngành/sinh viên của assignment, kèm link tải có kiểm quyền.
- **Test:** assignment bị thu hồi 403; file ngoài phạm vi không xuất hiện.

## 3. Sprint 3 (09/11 – 20/11)

### BE-12 · Checkpoint & Review Gate — P1, 5 điểm
- Bảng: `project_checkpoints(id, project_id, policy_version_id, gate_code G1..G6, is_required, due_at, status PLANNED|IN_REVIEW|PASSED|REVISION_REQUIRED|MISSED, concurrency_token)`, `gate_reviews(id, checkpoint_id, reviewer_id, major_id?, decision, reason, decided_at)`, `gate_review_evidence(gate_review_id, evidence_id)`.
- Policy kỳ khai báo danh sách gate và cờ bắt buộc. **Mặc định MVP: chế độ theo dõi** (không chặn giai đoạn sau). Khi `is_required = true`, gate không PASS nếu thiếu evidence hoặc reviewer (BRX-GATE-01, 422).
- Quá hạn → `MISSED` + thông báo rủi ro, **không** làm project FAIL (BRX-GATE-02).
- API: `GET/POST /api/v1/projects/{id}/checkpoints`, `POST /api/v1/checkpoints/{id}/reviews`.
- *Mức "bắt buộc hay theo dõi" chờ Vo Van chốt D2.*

### BE-14 · Hội đồng & lịch bảo vệ — P1, 5 điểm
- Bảng: `evaluation_committees(id, project_period_id, department_id, name)`, `committee_members(committee_id, user_id, role CHAIR|SECRETARY|MEMBER|INDUSTRY)`, `defense_sessions(id, committee_id, project_id, start_at, end_at, location, online_url, status)`.
- Tạo assignment chấm từ hội đồng (mỗi thành viên một assignment theo phạm vi), vẫn đi qua guard hiện có.
- Kiểm tra trùng lịch thành viên; thông báo cho nhóm và hội đồng.

## 4. Sprint 4 (23/11 – 04/12)

### BE-15 · Lưu kết quả phân tích AI — P1, 5 điểm
- Bảng: `ai_analysis_runs(id, project_id, kind RISK|SUMMARY|CONTRIBUTION, engine rule|llm, engine_version, input_hash, sufficiency SUFFICIENT|INSUFFICIENT, status, created_by, created_at, output_json)`, `ai_risk_factors(run_id, code, weight, explanation, evidence_ref)`.
- `RuleBasedProgressAnalysisService` ghi run mỗi lần chạy theo yêu cầu hoặc theo lịch; API `GET /api/v1/projects/{id}/ai/runs`, `GET /api/v1/ai/runs/{id}`.
- AI không được đổi trạng thái hay gán người (BRX-AI-01); dữ liệu không đủ → `INSUFFICIENT`, không bịa số.

## 5. Backlog P2
- **BE-18** Gợi ý GVHD: lọc capacity + chuyên môn trước, sau đó xếp hạng; lưu `supervisor_recommendation_runs/items`; không tự tạo request/assignment (BR-60–63).
- **BE-19b** Peer evaluation (`peer_evaluations`, chỉ là evidence) và phúc khảo/đính chính (`result_correction_requests`, tạo version kết quả mới).

## 6. Điểm phối hợp
| Với | Nội dung |
|---|---|
| KhaiNQ | Dùng chung script seed BE-02; cung cấp mã quyền chấm/công bố cho BE-10; `evidence_reviews` (BE-13) là đầu vào cho gate (BE-12). |
| FE | Màn kết quả SV chờ BE-09; công bố kết quả chờ BE-03; màn chấm điểm dùng BE-08; màn gate chờ BE-12; trang AI chờ BE-15. |
