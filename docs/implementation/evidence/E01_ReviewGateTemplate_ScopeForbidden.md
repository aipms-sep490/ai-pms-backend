# E01 Acceptance Evidence: Review Gate Template Authority Enforcement
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** ReviewGateTemplateAuthorityTests.Staff_marketing_cannot_save_template_for_se_major_returns_403_scope_forbidden
- **Lệnh kiểm chứng:** `dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~ReviewGateTemplateAuthorityTests.Staff_marketing_cannot_save_template_for_se_major_returns_403_scope_forbidden"`
- **Output:** Passed: 1, Failed: 0
- **Actor:** Department Staff (Marketing Department ID=2)
- **Action:** POST /api/v1/review-gate-templates with Major = "SE" (Software Engineering Dept ID=1)
- **Result:** PASS (HTTP 403 Forbidden with code TEMPLATE_SCOPE_FORBIDDEN)
- **Verified Invariant:** Staff cannot mutate review gate templates for academic majors outside their department.
