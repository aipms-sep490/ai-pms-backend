# E10 Acceptance Evidence: Two-Level Rubric ParentId Indentation
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** EvaluationDraftContractTests.GetAssignmentDetail_TwoLevelRubric_CriteriaContainParentIdAndParentName
- **Lệnh kiểm chứng:** `dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~EvaluationDraftContractTests.GetAssignmentDetail_TwoLevelRubric_CriteriaContainParentIdAndParentName"`
- **Output:** Passed: 1, Failed: 0
- **Action:** Rubric with parent group criterion and leaf children. Evaluator opens scoring sheet.
- **Result:** PASS (Backend only evaluates leaf criteria but exposes parentId and parentName on each score and item for UI hierarchy indentation; parent criteria have no score inputs).
- **Verified Invariant:** BR-N5.
