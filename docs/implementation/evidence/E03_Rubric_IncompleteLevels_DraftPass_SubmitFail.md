# E03 Acceptance Evidence: Incomplete Levels Gating
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** RubricLevelTests.SubmitForApproval_WhenLeafCriterionHasFewerThanRequiredLevels_Returns422UnprocessableEntity
- **Action 1:** Save Draft with 0 or partial levels -> HTTP 200 OK.
- **Action 2:** Submit for approval with 3 levels when RequiredLevelCount = 4 -> HTTP 422 RUBRIC_LEAF_LEVELS_INCOMPLETE.
- **Result:** PASS.
