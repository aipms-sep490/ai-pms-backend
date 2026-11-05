# E09 Acceptance Evidence: Pure Lecturer Assignment Detail Projection
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** EvaluationDraftContractTests.GetAssignmentDetail_WithQualityLevels_ReturnsAllNewHeaderAndCriterionFields_InAscendingOrder
- **Lệnh kiểm chứng:** `dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~EvaluationDraftContractTests.GetAssignmentDetail_WithQualityLevels_ReturnsAllNewHeaderAndCriterionFields_InAscendingOrder"`
- **Output:** Passed: 1, Failed: 0
- **Actor:** Pure Lecturer (role LECTURER, non-staff, non-admin)
- **Action:** GET /api/v1/evaluation-assignments/{id}
- **Result:** PASS (Returns schemeIsDemo, schemeApprovalStatus, componentStudentWeightPercent, completedEvaluators, requiredEvaluators, canScore).
