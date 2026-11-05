# E11 Acceptance Evidence: Upload Policy Enforcement and 413 Handling
- **Environment:** LOCAL_INTEGRATION_TEST
- **Test Reference:** UploadPolicyConsistencyTests.Upload_PayloadBetweenPublishedLimitAndValidatorCeiling_IsRejectedConsistently, UploadPolicyConsistencyTests.Upload_ExceedingSizeLimit_Returns413WithErrorCodeAndMaxBytes
- **Lệnh kiểm chứng:** `dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~UploadPolicyConsistencyTests.Upload_PayloadBetweenPublishedLimitAndValidatorCeiling_IsRejectedConsistently"`
- **Output:** Passed: 1, Failed: 0
- **Action:** Upload file of 21 MiB to route with effective limit of 20 MiB (STUDENT_QUALIFICATION).
- **Result:** PASS (HTTP 413 Payload Too Large with errorCode = "UPLOAD_TOO_LARGE" and response maxBytes strictly matches published policy maxBytes 20 MiB = 20971520).
- **Pre-Fix Failure Evidence (Red Test Caught Contract Drift):**
```text
[xUnit.net 00:00:03.42]     AIPMS.IntegrationTests.Files.UploadPolicyConsistencyTests.Upload_PayloadBetweenPublishedLimitAndValidatorCeiling_IsRejectedConsistently [FAIL]
  Failed AIPMS.IntegrationTests.Files.UploadPolicyConsistencyTests.Upload_PayloadBetweenPublishedLimitAndValidatorCeiling_IsRejectedConsistently [2 s]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 23068672
Actual:   20971520
  Stack Trace:
     at AIPMS.IntegrationTests.Files.UploadPolicyConsistencyTests.Upload_PayloadBetweenPublishedLimitAndValidatorCeiling_IsRejectedConsistently() in D:\AI_PMS\ai-pms-backend\tests\AIPMS.IntegrationTests\Files\UploadPolicyConsistencyTests.cs:line 233
```
- **Post-Fix Verification (FIX-01 Applied):**
```text
Passed!  - Failed:     0, Passed:    25, Skipped:     0, Total:    25, Duration: 2 s
```
- **Verified Invariant:** Published upload policy maxBytes never exceeds effective server validator ceiling (`min(RequestSizeLimit, UploadValidator.MaxBytes)`).
