# E04 Acceptance Evidence: Legacy Rubric Backward Compatibility
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** RubricLevelTests.LegacyRubric_WithoutLevels_CanBeReadWithoutError, EvaluationDraftContractTests.GetAssignmentDetail_LegacyRubric_ReturnsEmptyLevelsAndNullStage
- **Lệnh kiểm chứng:** `dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~RubricLevelTests.LegacyRubric_WithoutLevels_CanBeReadWithoutError"`
- **Output:** Passed: 1, Failed: 0
- **Context:** Rubric created before 20261030_add_rubric_criterion_levels.sql with no rows in rubric_criterion_levels.
- **Action:** Retrieve rubric and perform evaluation draft creation.
- **Result:** PASS (Returns levels = [], evaluation proceeds normally without level gating).
