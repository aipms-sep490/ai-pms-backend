# E13 Acceptance Evidence: Official Publication Blocked When Approval Reference Empty
- **Environment:** LOCAL_UNIT_TEST
- **Test Reference:** ElevenComponentScoringTests.Validate_OfficialSchemeWithoutApprovalReference_Fails
- **Action:** Scheme set to IsDemo = false and ApprovalStatus = "APPROVED" but ApprovalReference is whitespace.
- **Result:** PASS (Rejected as invalid; official graduation grade publication blocked).
- **Verified Invariant:** BR-N7.
