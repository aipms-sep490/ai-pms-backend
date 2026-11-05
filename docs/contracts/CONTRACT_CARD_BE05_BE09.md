# CONTRACT CARD: BE-05 (Upload Policies) & BE-09 (Evaluation Assignment Projection & Academic Governance)

> Quy chuẩn theo Quy tắc `X1`, `X3`, `X5`, `X6` phục vụ Frontend integration.
> Cập nhật theo phản hồi PR #141 (05/11/2026).

---

## 1. Machine-Readable API Examples (JSON)

### 1.1. `GET /api/v1/upload-policies` (BE-05)
Tra cứu chính sách kích thước và định dạng tệp tải lên theo từng nghiệp vụ. FE dùng để cấu hình dropzone / file picker, loại bỏ hardcode 20 MiB.

```json
[
  {
    "parentType": "REVIEW_GATE_EVIDENCE",
    "maxBytes": 20971520,
    "maxBytesLabel": "20 MiB",
    "allowedMimeTypes": [
      "application/pdf",
      "image/png",
      "image/jpeg",
      "application/zip"
    ],
    "allowedExtensions": [
      ".pdf",
      ".png",
      ".jpg",
      ".jpeg",
      ".zip"
    ],
    "maxFilesPerRequest": 1,
    "requiresVersionAnchor": true
  },
  {
    "parentType": "DELIVERABLE_VERSION",
    "maxBytes": 20971520,
    "maxBytesLabel": "20 MiB",
    "allowedMimeTypes": [
      "application/pdf",
      "application/zip",
      "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
      "application/x-rar-compressed"
    ],
    "allowedExtensions": [
      ".pdf",
      ".zip",
      ".docx",
      ".rar"
    ],
    "maxFilesPerRequest": 1,
    "requiresVersionAnchor": true
  },
  {
    "parentType": "PROJECT_FILE",
    "maxBytes": 20971520,
    "maxBytesLabel": "20 MiB",
    "allowedMimeTypes": [
      "application/pdf",
      "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
      "application/zip",
      "image/png",
      "image/jpeg"
    ],
    "allowedExtensions": [
      ".pdf",
      ".docx",
      ".zip",
      ".png",
      ".jpg",
      ".jpeg"
    ],
    "maxFilesPerRequest": 1,
    "requiresVersionAnchor": false
  },
  {
    "parentType": "STUDENT_QUALIFICATION",
    "maxBytes": 20971520,
    "maxBytesLabel": "20 MiB",
    "allowedMimeTypes": [
      "application/pdf",
      "image/png",
      "image/jpeg"
    ],
    "allowedExtensions": [
      ".pdf",
      ".png",
      ".jpg",
      ".jpeg"
    ],
    "maxFilesPerRequest": 1,
    "requiresVersionAnchor": false
  },
  {
    "parentType": "COLD_SUBMISSION",
    "maxBytes": 11534336,
    "maxBytesLabel": "11 MiB",
    "allowedMimeTypes": [
      "application/pdf",
      "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
      "application/zip",
      "image/png",
      "image/jpeg"
    ],
    "allowedExtensions": [
      ".pdf",
      ".docx",
      ".zip",
      ".png",
      ".jpg",
      ".jpeg"
    ],
    "maxFilesPerRequest": 1,
    "requiresVersionAnchor": true
  }
]
```

---

### 1.2. `GET /api/v1/rubrics/{id}` (BE-07 / BE-08 / Q-N02)
Chi tiết Rubric bao gồm thông tin phân quyền phạm vi học thuật (Scope, Major, Stage, ProjectMode), trạng thái phê duyệt và danh sách mức chất lượng (Levels).

```json
{
  "id": 101,
  "departmentId": 1,
  "academicSemesterId": 2,
  "majorId": 5,
  "projectMode": "INTERDISCIPLINARY",
  "evaluationStage": "DEFENSE",
  "evaluationScope": "MAJOR_SPECIFIC",
  "effectiveFrom": "2026-09-01",
  "effectiveTo": "2026-12-31",
  "approvalReference": "QD-2026-HDHV-092",
  "code": "R_SE_DEF_FA26",
  "name": "Software Engineering Defense Evaluation Rubric",
  "description": "4-level quality criteria for SE final defense",
  "status": "PUBLISHED",
  "isActive": true,
  "rootRubricId": 101,
  "version": 1,
  "concurrencyToken": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "canEdit": false,
  "createdAt": "2026-10-30T08:00:00Z",
  "updatedAt": "2026-10-30T08:00:00Z",
  "criteria": [
    {
      "id": 1001,
      "criterionId": 501,
      "criterionCode": "RC_ARCH_DESIGN",
      "name": "Architecture & System Design",
      "description": "Quality and modularity of the system architecture",
      "learningOutcome": "PLO-1: Ability to architect scalable and modular software solutions",
      "evidenceGuidance": "Inspect C4 diagrams, ADR records, and module boundaries in repo",
      "weightPercent": 50.0,
      "effectiveWeightPercent": 50.0,
      "maxScore": 10.0,
      "sortOrder": 0,
      "isRequired": true,
      "parentId": null,
      "levels": [
        {
          "levelOrder": 1,
          "label": "Poor",
          "descriptor": "No clear architectural pattern or excessive coupling between components.",
          "scoreFrom": 0.0,
          "scoreTo": 4.99
        },
        {
          "levelOrder": 2,
          "label": "Satisfactory",
          "descriptor": "Basic layered architecture with documented components but minor boundary leaks.",
          "scoreFrom": 5.0,
          "scoreTo": 6.99
        },
        {
          "levelOrder": 3,
          "label": "Good",
          "descriptor": "Clean modular architecture with well-defined contracts and architectural decision records.",
          "scoreFrom": 7.0,
          "scoreTo": 8.49
        },
        {
          "levelOrder": 4,
          "label": "Excellent",
          "descriptor": "Exemplary Clean Architecture with comprehensive ADRs and boundary enforcement.",
          "scoreFrom": 8.5,
          "scoreTo": 10.0
        }
      ]
    }
  ]
}
```

---

### 1.3. `GET /api/v1/evaluation-assignments/{id}` (BE-09)
Chi tiết phân công chấm dành cho Giảng viên / Ban đánh giá. Bao gồm đầy đủ thông tin Header (Stage, Approval, IsDemo, Completion status) và cây tiêu chí 2 cấp (ParentId, ParentName) phục vụ thụt lề UI.

```json
{
  "assignment": {
    "id": 101,
    "projectId": 42,
    "evaluatorId": 3001,
    "rubricId": 5,
    "projectPeriodId": 10,
    "departmentId": 1,
    "evaluationType": "LECTURER",
    "status": "ACTIVE",
    "assignedBy": 2001,
    "assignedAt": "2026-10-10T02:00:00Z",
    "revokedAt": null,
    "concurrencyToken": "b2c3d4e5-f6a7-8b9c-0d1e-2f3a4b5c6d7e",
    "scope": "COMMON",
    "majorId": null,
    "studentId": null,
    "componentId": 12,
    "policyVersionId": 1,
    "stage": "DEFENSE",
    "coldPackageId": null,
    "projectTitle": "AI-Powered Interdisciplinary Project Management System",
    "componentName": "Final Project Defense",
    "rubricName": "Software Engineering Defense Evaluation Rubric",
    "majorName": null,
    "studentName": null,
    "dueAt": "2026-10-15T17:00:00Z",
    "componentStudentWeightPercent": 30.0,
    "schemeApprovalStatus": "APPROVED",
    "schemeIsDemo": false,
    "completedEvaluators": 2,
    "requiredEvaluators": 3
  },
  "canScore": true,
  "legacyReadOnly": false,
  "denialReason": null,
  "stage": "DEFENSE",
  "componentStudentWeightPercent": 30.0,
  "schemeApprovalStatus": "APPROVED",
  "schemeIsDemo": false,
  "completedEvaluators": 2,
  "requiredEvaluators": 3,
  "items": [
    {
      "rubricCriterionId": 501,
      "name": "Backend Service Implementation",
      "description": "API endpoints, Clean Architecture layers, EF Core queries",
      "weightPercent": 25.0,
      "effectiveWeightPercent": 25.0,
      "maxScore": 10.0,
      "sortOrder": 1,
      "isRequired": true,
      "criterionCode": "RC_BE_IMPL",
      "learningOutcome": "PLO-2: Implement modular backend microservices",
      "evidenceGuidance": "Check controller contracts, unit tests, and CI/CD pipelines",
      "parentId": 500,
      "parentName": "Technical Implementation",
      "levels": [
        {
          "levelOrder": 1,
          "label": "Poor",
          "descriptor": "Incomplete implementation, compilation errors, lacking tests",
          "scoreFrom": 0.0,
          "scoreTo": 4.99
        },
        {
          "levelOrder": 4,
          "label": "Excellent",
          "descriptor": "Full test coverage, clean code boundaries, idempotent migrations",
          "scoreFrom": 8.5,
          "scoreTo": 10.0
        }
      ]
    }
  ],
  "draft": {
    "assignmentId": 101,
    "feedback": "Draft saved progress",
    "concurrencyToken": "d4e5f6a7-0000-1111-2222-333344445555",
    "scores": [
      {
        "rubricCriterionId": 501,
        "score": 8.5,
        "comment": "Exemplary implementation",
        "name": "Backend Service Implementation",
        "description": "API endpoints, Clean Architecture layers",
        "maxScore": 10.0,
        "weightPercent": 25.0,
        "criterionCode": "RC_BE_IMPL",
        "learningOutcome": "PLO-2: Implement modular backend microservices",
        "evidenceGuidance": "Check controller contracts, unit tests, and CI/CD pipelines",
        "parentId": 500,
        "parentName": "Technical Implementation"
      }
    ]
  }
}
```

---

## 2. Bảng Mã Lỗi Nghiệp Vụ Mới (Error Code Table)

| Error Code | HTTP Status | Nguyên nhân từ Backend | Frontend phải làm gì |
|---|:---:|---|---|
| `TEMPLATE_SCOPE_FORBIDDEN` | **403** | Trưởng bộ môn cố gắng tạo/sửa Review Gate Template thuộc chuyên ngành khác hoặc vi phạm quyền hạn bộ môn chủ trì. | Hiển thị thông báo lỗi bảo mật phân quyền: *"Bạn chỉ có quyền chỉnh sửa mẫu Review Gate thuộc phạm vi bộ môn của mình hoặc khi là bộ môn chủ trì."* Vô hiệu hóa nút Save. |
| `TEMPLATE_AUTHORITY_ADMIN_NOT_ACADEMIC` | **403** | Tài khoản Admin hệ thống (phi học vụ) cố gắng can thiệp/lưu Template đánh giá học thuật. | Hiển thị modal cảnh báo phân quyền Four-Eyes: *"Mẫu đánh giá học thuật chỉ được phê duyệt bởi Hội đồng / Bộ môn học thuật có thẩm quyền."* Không cho phép ghi đè. |
| `RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT` | **403** | Bộ môn không phải bộ môn chủ trì (`IsLead = false`) cố gắng phê duyệt Rubric phạm vi `COMMON` liên ngành. | Hiển thị thông báo: *"Rubric dùng chung liên ngành chỉ được phê duyệt bởi Bộ môn chủ trì dự án."* Ẩn các thao tác Phê duyệt / Từ chối. |
| `RUBRIC_APPROVAL_ADMIN_NOT_ACADEMIC` | **403** | Quyền `ADMIN` cố gắng thực hiện hành động phê duyệt hoặc từ chối Rubric thay cho Hội đồng học vụ. | Hiển thị lỗi phân quyền: *"Thao tác phê duyệt Rubric yêu cầu thẩm quyền học vụ chuyên môn (Chủ nhiệm bộ môn / Hội đồng thẩm định)."* |
| `RUBRIC_LEAF_LEVELS_INCOMPLETE` | **422** | Tiêu chí lá (leaf criterion) chưa khai báo đủ số lượng mức chất lượng theo quy định (`RequiredLevelCount`, mặc định 4). | Focus vào tiêu chí bị thiếu mức, highlight màu đỏ các ô mức còn trống và hiển thị thông báo: *"Mỗi tiêu chí đánh giá phải có đủ 4 mức chất lượng trước khi gửi duyệt."* |
| `RUBRIC_PARENT_HAS_LEVELS` | **422** | Tiêu chí nhóm cha (parent group criterion) lại chứa mức chất lượng (`levels`). Chỉ tiêu chí lá mới được phép có levels. | Báo lỗi cấu trúc cây tiêu chí: *"Tiêu chí nhóm cha không được định nghĩa thang điểm mức chất lượng. Hãy thêm mức vào tiêu chí con."* |
| `RUBRIC_SCOPE_REQUIRED` | **400** | Rubric thiếu thông tin bắt buộc về phạm vi học thuật (`EvaluationScope`, `EvaluationStage`, hoặc `ProjectMode`). | Form validate các trường Scope/Stage/Mode trước khi submit. Đánh dấu đỏ các dropdown bắt buộc. |
| `UPLOAD_TOO_LARGE` | **413** | Tệp tin tải lên vượt quá giới hạn hiệu lực `EffectiveFileLimit` (ví dụ 20 MiB) hoặc trần cấu hình route. | Đọc trường `maxBytes` trả về từ body lỗi (`response.data.maxBytes`), hiển thị thông báo thân thiện: *"Kích thước tệp vượt quá giới hạn cho phép ([maxBytesLabel]). Vui lòng giảm dung lượng tệp và thử lại."* |
| `REPORT_ALREADY_EXISTS_FOR_CYCLE` | **409** | Sinh viên nộp báo cáo tiến độ lần 2 cho cùng một kỳ báo cáo (Reporting Cycle) đã có báo cáo nộp trước đó. | Hiển thị cảnh báo xung đột: *"Báo cáo tiến độ cho chu kỳ này đã được nộp và không thể ghi đè (Quy tắc lưu trữ bất biến)."* Hướng dẫn sinh viên nộp minh chứng bổ sung nếu cần. |

---

## 3. Bảng Ngữ Nghĩa Các Trường Nullable (Nullable Fields Table)

| Tên trường | Kiểu dữ liệu | Ý nghĩa khi nhận giá trị `null` | Frontend hiển thị / xử lý thế nào |
|---|---|---|---|
| `Score` (trong `EvaluationScoreDto`) | `decimal?` | **Chưa chấm.** (Tuyệt đối **KHÔNG ĐƯỢC COI LÀ 0**). | Hiển thị ô nhập điểm trống. Nếu tính tổng điểm hiển thị, không đưa tiêu chí này vào tổng hoặc hiển thị trạng thái *"Đang chấm (Chưa hoàn thành)"*. Không tính là điểm liệt 0. |
| `Stage` | `string?` | Khung đánh giá legacy hoặc phân công chung không gán cố định cho stage cụ thể. | Hiển thị badge mặc định hoặc nhãn *"Chung / Toàn khóa"*, không cố parse enum. |
| `SchemeApprovalStatus` | `string?` | Khung đánh giá legacy chưa áp dụng quy trình kiểm soát phê duyệt chính thức. | Hiển thị badge trạng thái màu xám *"Chưa áp dụng luồng duyệt"*, không hiển thị icon bảo chứng Four-Eyes. |
| `ParentId` | `long?` | **Tiêu chí gốc cấp 1 (Root Criterion).** | Render ở lề trái ngoài cùng (`paddingLeft: 0`). Nếu là nút cha chứa tiêu chí con, render tiêu đề nhóm in đậm và KHÔNG hiển thị ô nhập điểm. |
| `ParentName` | `string?` | Tiêu chí cấp 1 không có tên cha. | Hiển thị trực tiếp tên tiêu chí `Name`. Đối với tiêu chí con có `ParentName`, FE có thể hiển thị breadcrumb nhóm: `ParentName > Name`. |
| `EffectiveWeightPercent` | `decimal?` | Tiêu chí cha không trực tiếp tính điểm nên không có trọng số hiệu lực lá. | Đối với tiêu chí cha: không hiển thị ô điểm, chỉ hiển thị trọng số tổng của nhóm. Đối với tiêu chí lá: hiển thị trọng số thật đã nhân tỷ lệ nhóm (ví dụ: 25.0%). |
| `effectiveFrom` / `effectiveTo` | `DateOnly?` | Rubric không giới hạn thời gian bắt đầu hoặc kết thúc hiệu lực. | Hiển thị nhãn *"Không giới hạn"* hoặc *"Vô thời hạn"*. |
| `approvalReference` | `string?` | Rubric chưa được phê duyệt chính thức hoặc đang ở chế độ bản nháp / demo (`IsDemo = true`). | Khóa hiển thị watermark *"Chính thức"*; hiển thị watermark *"Bản nháp / Thử nghiệm"* trên bảng điểm. Bất biến N7: Cấm xuất điểm chính thức khi trường này `null`. |
