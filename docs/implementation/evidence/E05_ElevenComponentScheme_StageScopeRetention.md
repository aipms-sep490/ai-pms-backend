# E05 Acceptance Evidence: Eleven-Component Scheme Stage and Scope Retention
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** ElevenComponentScoringTests.Test01_Filtered5Components_For_SE_Student_Calculates_Total_Correctly, ElevenComponentScoringTests.Test12_TwoMajor_Interdisciplinary_8Components_FilteredTo5_SumsTo100
- **Lệnh kiểm chứng:** `dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~ElevenComponentScoringTests.Test01_Filtered5Components_For_SE_Student_Calculates_Total_Correctly"`
- **Output:** Passed: 1, Failed: 0
- **FE Verification:** FE_VERIFIED_SEPARATELY (EvaluationSchemeManagementPage.test.tsx)
- **Action:** Author evaluation scheme with components across STAGE (ON_GOING, COLD, DEFENSE) and SCOPE (COMMON, MAJOR_SPECIFIC, INDIVIDUAL).
- **Result:** PASS (All components retain identity: Stage * Scope * MajorId).
- **Verified Invariant:** BR-N3.
