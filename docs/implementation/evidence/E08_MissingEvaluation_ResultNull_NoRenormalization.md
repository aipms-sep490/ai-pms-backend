# E08 Acceptance Evidence: Missing Evaluation Yields Null Without Weight Renormalization
- **Environment:** LOCAL_UNIT_TEST
- **Test Reference:** ElevenComponentScoringTests.Test04_Valid5Components_With_One_Score_Null_Returns_Null_N6, ElevenComponentScoringTests.Test05_Component_With_Incomplete_Evaluators_Returns_Null_No_Reweighting_N6
- **Lệnh kiểm chứng:** `dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~ElevenComponentScoringTests.Test04_Valid5Components_With_One_Score_Null_Returns_Null_N6"`
- **Output:** Passed: 1, Failed: 0
- **Action:** Evaluate with 4 of 5 components finalized; 1 component has Score = null.
- **Result:** PASS (Overall total score is null, not recalculated or renormalized to 80%).
- **Verified Invariant:** BR-N6.
