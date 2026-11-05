# E12 Acceptance Evidence: Interdisciplinary Multi-Major Defense Readiness
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** ReviewGateServiceBehaviorTests.Test20_DefenseReadiness_When_Missing_Interdisciplinary_Scope_Decisions_Is_Not_Ready
- **Lệnh kiểm chứng:** `dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~ReviewGateServiceBehaviorTests.Test20_DefenseReadiness_When_Missing_Interdisciplinary_Scope_Decisions_Is_Not_Ready"`
- **Output:** Passed: 1, Failed: 0
- **Action:** Project with 2 majors (SE + MKT) requires decision across Common, SE major, and MKT major (majorCount + 1 = 3).
- **Result:** PASS (If any scope is missing decision, defenseReady = false; when all 3 scopes pass, defenseReady = true).
