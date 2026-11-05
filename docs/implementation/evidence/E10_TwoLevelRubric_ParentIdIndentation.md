# E10 Acceptance Evidence: Two-Level Rubric ParentId Indentation
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** EvaluationDraftContractTests.GetAssignmentDetail_TwoLevelRubric_CriteriaContainParentId
- **Action:** Rubric with parent group criterion and leaf children. Evaluator opens scoring sheet.
- **Result:** PASS (Backend only evaluates leaf criteria but exposes parentId on each score and item for UI hierarchy indentation; parent criteria have no score inputs).
- **Verified Invariant:** BR-N5.
