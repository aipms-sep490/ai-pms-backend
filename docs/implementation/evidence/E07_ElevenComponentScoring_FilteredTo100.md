# E07 Acceptance Evidence: Eleven-Component Scoring Filtered to 100%
- **Environment:** LOCAL_UNIT_TEST
- **Test Reference:** ElevenComponentScoringTests.Test01_Filtered5Components_For_SE_Student_Calculates_Total_Correctly
- **Lệnh kiểm chứng:** `dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~ElevenComponentScoringTests.Test01_Filtered5Components_For_SE_Student_Calculates_Total_Correctly"`
- **Output:** Passed: 1, Failed: 0
- **Action:** Evaluate single SE student in 11-component interdisciplinary project. Select 5 relevant components (Common Cold 15% + Common Defense 35% + SE Cold 15% + SE Defense 15% + Individual Defense 20%).
- **Result:** PASS (Effective component weight sum = 100.0%, calculates student score correctly).
- **Verified Invariant:** BR-N4.
