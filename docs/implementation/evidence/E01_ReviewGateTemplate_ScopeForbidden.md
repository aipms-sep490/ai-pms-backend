# E01 Acceptance Evidence: Review Gate Template Authority Enforcement
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** ReviewGateTemplateAuthorityTests.SaveTemplate_DifferentMajorDepartment_Returns403Forbidden
- **Actor:** Department Staff (Marketing Department ID=2)
- **Action:** POST /api/v1/review-gate-templates with Major = "SE" (Software Engineering Dept ID=1)
- **Result:** PASS (HTTP 403 Forbidden with code TEMPLATE_SCOPE_FORBIDDEN)
- **Verified Invariant:** Staff cannot mutate review gate templates for academic majors outside their department.
