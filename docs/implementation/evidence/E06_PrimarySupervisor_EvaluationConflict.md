# E06 Acceptance Evidence: Primary Supervisor Evaluation Assignment Conflict
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** AcademicAssignmentConflictTests.D17_primary_is_excluded_before_paging_and_cannot_be_assigned, AcademicAssignmentConflictTests.Mentor_and_revoked_evaluator_do_not_block_the_opposite_assignment
- **Lệnh kiểm chứng:** `dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~AcademicAssignmentConflictTests.D17_primary_is_excluded_before_paging_and_cannot_be_assigned"`
- **Output:** Passed: 1, Failed: 0
- **FE Verification:** FE_VERIFIED_SEPARATELY (EvaluatorAssignmentManagementPage.test.tsx)
- **Action:** Attempt to assign Primary Supervisor as official evaluator for their supervised project.
- **Result:** PASS (Assignment rejected with PRIMARY_EVALUATOR_CONFLICT).
- **Verified Invariant:** BR-N9.
