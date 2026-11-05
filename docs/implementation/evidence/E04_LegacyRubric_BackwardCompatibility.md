# E04 Acceptance Evidence: Legacy Rubric Backward Compatibility
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** RubricLevelTests.LegacyRubric_WithoutLevels_LoadsAndEvaluatesNormally
- **Context:** Rubric created before 20261030_add_rubric_criterion_levels.sql with no rows in ubric_criterion_levels.
- **Action:** Retrieve rubric and perform evaluation draft creation.
- **Result:** PASS (Returns levels = [], evaluation proceeds normally without level gating).
