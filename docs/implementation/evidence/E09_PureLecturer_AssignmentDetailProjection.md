# E09 Acceptance Evidence: Pure Lecturer Assignment Detail Projection
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** EvaluationDraftContractTests.GetAssignmentDetail_PureLecturer_ReturnsCompleteProjectionWithoutAdminListing
- **Actor:** Pure Lecturer (role LECTURER, non-staff, non-admin)
- **Action:** GET /api/v1/evaluation-assignments/{id}
- **Result:** PASS (Returns schemeIsDemo, schemeApprovalStatus, componentStudentWeightPercent, completedEvaluators, equiredEvaluators, canScore).
