# E05 Acceptance Evidence: Eleven-Component Scheme Stage and Scope Retention
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** EvaluationSchemeManagementPage.test.tsx (Frontend) & EvaluationDraftContractTests (Backend)
- **Action:** Author evaluation scheme with 11 components across STAGE (COLD, DEFENSE) and SCOPE (COMMON, MAJOR_SPECIFIC, INDIVIDUAL).
- **Result:** PASS (All components retain identity: Stage * Scope * MajorId).
- **Verified Invariant:** BR-N3.
