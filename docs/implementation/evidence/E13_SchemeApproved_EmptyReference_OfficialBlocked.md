# E13 Acceptance Evidence: Official Publication Blocked When Approval Reference Empty
- **Environment:** LOCAL_UNIT_TEST
- **Test Reference:** ElevenComponentScoringTests.Test09_Validate_Throws_When_IsDemo_False_Approved_And_ApprovalReference_Is_Empty_N7
- **Lệnh kiểm chứng:** `dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~ElevenComponentScoringTests.Test09_Validate_Throws_When_IsDemo_False_Approved_And_ApprovalReference_Is_Empty_N7"`
- **Output:** Passed: 1, Failed: 0
- **Action:** Scheme set to IsDemo = false and ApprovalStatus = "APPROVED" but ApprovalReference is whitespace.
- **Result:** PASS (Rejected as invalid; official graduation grade publication blocked).
- **Verified Invariant:** BR-N7.
