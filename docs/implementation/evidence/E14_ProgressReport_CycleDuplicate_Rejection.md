# E14 Acceptance Evidence: Duplicate Progress Report Cycle Rejection
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** Frontend ProgressReports.test.tsx & Backend ReportingCycle tests
- **Action:** Submit second report for the same cycle in project execution.
- **Result:** PASS (Backend rejects with REPORT_ALREADY_EXISTS_FOR_CYCLE, Frontend maps code to: "Báo cáo tiến độ cho chu kỳ này đã tồn tại.").
