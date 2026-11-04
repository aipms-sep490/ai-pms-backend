# E02 Acceptance Evidence: Rubric Four-Eyes Controlled Publish
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** RubricApprovalWorkflowTests.Approve_ByDifferentUser_TransitionsToPublished
- **Actor:** Submitter Staff A (ID=101), Approver Staff B (ID=102)
- **Action:** Staff A submits rubric for approval; Staff B approves with approvalReference QD-2026/DH-CNTT.
- **Negative Action:** Staff A attempts to self-approve.
- **Result:** PASS (Staff A self-approve receives HTTP 403 RUBRIC_SELF_APPROVAL_FORBIDDEN; Staff B approve succeeds HTTP 200 and transitions status to PUBLISHED).
- **Verified Invariant:** BR-N8, BR-N13, Four-Eyes workflow.
