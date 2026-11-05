# E11 Acceptance Evidence: Upload Policy Enforcement and 413 Handling
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** UploadPolicyConsistencyTests.Upload_WhenPayloadExceedsEffectiveLimit_Returns413UploadTooLarge
- **Action:** Upload file of 21 MiB to route with effective limit of 20 MiB (STUDENT_QUALIFICATION_CERTIFICATE).
- **Result:** PASS (HTTP 413 Payload Too Large with errorCode = "UPLOAD_TOO_LARGE" and body containing maxBytes).
