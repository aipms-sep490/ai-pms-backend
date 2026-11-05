# E08 Acceptance Evidence: Missing Evaluation Yields Null Without Weight Renormalization
- **Environment:** LOCAL_UNIT_TEST
- **Test Reference:** ElevenComponentScoringTests.Calculate_MissingOneComponentScore_ReturnsNull
- **Action:** Evaluate with 4 of 5 components finalized; 1 component has Score = null.
- **Result:** PASS (Overall total score is 
ull, not recalculated or renormalized to 80%).
- **Verified Invariant:** BR-N6.
