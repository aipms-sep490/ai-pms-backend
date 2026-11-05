# E03 Acceptance Evidence: Incomplete Levels Gating
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** RubricApprovalWorkflowTests.SubmitForApproval_WithIncompleteLeafLevels_Returns422, RubricLevelTests.Draft_CanBeSaved_WithNoLevelsOnLeafCriteria
- **Lệnh kiểm chứng:** `dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~RubricApprovalWorkflowTests.SubmitForApproval_WithIncompleteLeafLevels_Returns422"`
- **Output:** Passed: 1, Failed: 0
- **Action 1:** Save Draft with 0 or partial levels -> HTTP 200 OK.
- **Action 2:** Submit for approval with 3 levels when RequiredLevelCount = 4 -> HTTP 422 RUBRIC_LEAF_LEVELS_INCOMPLETE.
- **Result:** PASS.
