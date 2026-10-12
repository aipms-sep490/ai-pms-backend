# Result appeals and independent corrections

Expansion PR5, based on develop 3a3e5cd (PR #127). Backend/database only; Tin owns FE.
`Appeals:Enabled` defaults false (all example environments). No approved appeal
policy is seeded. Recording a reference is an accountable lead staff operation,
not a declaration that a demo or technical test is academically approved.

## Workflow and authority

Lead staff records an existing academic decision/source as an immutable project
appeal policy version. An explicit expected version prevents competing policy
writes. Lead then opens a time window with explicit published target/version pairs.
No automatic seven-day deadline. Target count 1..1000, window at most 366 days;
overlapping windows for the same target/version are rejected. Student lists filter
targets in SQL before count/paging and only expose that student's targets.

Student submits `STUDENT_APPEAL` for their own published result and selected
component. Lead can submit `STAFF_CORRECTION` with an explicit PROJECT or STUDENT
target; this never impersonates a student. One PENDING/UNDER_REVIEW request per
project/target/result version. A missing target, unknown frozen provenance or an
outdated result cannot be guessed or retargeted.

Lead assigns an active scoped lecturer/staff reviewer in the component's frozen
department (Common belongs to lead). Reviewer cannot be the affected student,
current Primary or any original scorer of the appealed component. Reassignment
removes the previous reviewer's assignment access immediately and invalidates their
proposal; existing history/bundles remain. A new review creates a new immutable
amendment bundle; it never edits finalized evaluations.

Reviewer submits KEEP with reason, or AMEND with per-evaluation/per-criterion scores
and reasons. The GET review context exposes only the selected component's historical
rubric/scores/evidence binding, never the complete result snapshot. Preview totals
across targets are lead-only. Decisions use the request concurrency token plus the
correction confirmation token. The lead decision maker must differ from reviewer/
bundle maker and original scorers. ACCEPTED is committed only with the published
revision bundle, history, audit and notification queue entries in one transaction.
KEEP can be closed as REJECTED with a reason; acceptance never pretends a revision
was published. Stale requests can be explicitly rejected, then a new version reviewed.

The specialized workflow operates on COMPLETED/ARCHIVED projects without reopening
them. Ordinary mutation permissions are unchanged. Admin alone grants no appeal
management or score correction. Participating staff see only non-Common requests
in their department; students see only their own submitted requests. A generic
lecturer or old reviewer has no read permission. Actor status, scope and reviewer
conflicts are checked again during mutation under the shared project lock.

## API for Tin

All routes below start `/api/v1/projects/{projectId}/result`.

| Routes | Contract | Screen/action |
| --- | --- | --- |
| GET/POST `/appeal-policy` | AppealPolicyDto / RecordAppealPolicyRequest | Lead policy/source |
| GET/POST `/appeal-windows`, POST `/{id}/close` under windows | AppealWindowDto; explicit Targets; token | Lead windows, student eligibility |
| GET/POST `/correction-requests` | PagedResult<CorrectionRequestDto> / CreateCorrectionRequest | Correction list, student appeal form |
| GET `/correction-requests/{id}` | CorrectionRequestDto | Detail, allowedActions/reasons |
| POST `/{id}/reviewer` under correction-requests | AssignAppealReviewerRequest | Lead assign/replace reviewer |
| GET/PUT `/{id}/review` | AppealReviewContextDto / ReviewAppealRequest | Frozen criteria, KEEP/AMEND |
| GET `/{id}/preview` | AppealPreviewDto | Independent lead preview affected targets |
| POST `/{id}/decision` | AppealDecisionRequest | ACCEPTED/REJECTED |
| GET `/{id}/history` | PagedResult<AppealEventDto> | Append-only request history |
| POST `/{id}/files`, GET `/{id}/files/{fileId}` | Multipart file+token / private stream | Requester's evidence |
| GET `/{id}/evidence`, GET `/{id}/evidence/{fileId}` | AppealFileDto[] / private stream | Scoped reviewer locked evidence |

GET list/history page 1..1000, pageSize 1..100. DTOs never return storage keys or
raw result snapshots. Student history hides bundle IDs and score amendments;
reviewers/scoped staff get linked historical amendments in one batched read.
File upload is requester-only while PENDING, before the student window closes;
max ten files, 10 MiB each, validated name/MIME/content through existing storage.
Upload failure cleans the newly created object. Files cannot be read through the
project file endpoint: they have no generic project parent.

Locked package files resolve the frozen package/version and target; never today's
latest artifact. COLD validates the frozen submission IDs and evidence hash.
Download rechecks current reviewer/account/department access. Student request access
does not grant access to the full locked Common package. A missing object returns
404; absent exact reference fails closed. Storage paths are never returned.

Example create: `{"source":"STUDENT_APPEAL","policyId":1,"windowId":2,"targetType":"STUDENT","studentId":123,"resultVersion":1,"componentId":4,"reason":"Please verify transcription"}`.
Example review: `{"concurrencyToken":"...","proposal":"AMEND","reason":"Verified transcription","amendments":[{"evaluationId":5,"criterionId":6,"score":8,"reason":"Correct criterion"}]}`.
Example decision: `{"concurrencyToken":"...","decision":"ACCEPTED","reason":"Independent verification","confirmationToken":"..."}`.

400 invalid input/paging/reason; 401 anonymous; 403 actor/department/target/reviewer
conflict or self approval; 404 disabled module/missing scoped resource; 409 stale
token/result, duplicate active request, closed window or unavailable provenance.
Domain codes: APPEAL_STALE, APPEAL_POLICY_REQUIRED, APPEAL_WINDOW_EXISTS,
APPEAL_WINDOW_CLOSED, APPEAL_DUPLICATE, APPEAL_CLOSED, APPEAL_AMENDMENT_REQUIRED,
RESULT_REVISION_STALE, RESULT_PROVENANCE_UNKNOWN and AFFECTED_RESULTS_MISSING.
No endpoint accepts a directly edited total score.

## Database, evidence and rollout

`20261013_add_result_appeals.sql`: policies, windows, requests, files and events.
Additive, rerunnable, QUOTED_IDENTIFIER ON; bootstrap/manifest/model synchronized.
Unique active request prevents duplicates; checks/FKs protect versions, target,
student origin, independent decision and bundle references. Event links retain
previous amendment bundles after reassignment/review. Generic notifications contain
no scores, reasons or artifact data and use the existing post-commit delivery queue.

`Appeals_*` tests in PolicyEvaluationEndpointTests use isolated SQL and actual HTTP
handlers: original publication, window/request/upload, reviewer, amendment, preview,
decision, latest result/history, audit rollback and competing decisions. They cover
archived staff correction, outdated version, original scorer/Primary/wrong-department
reviewers, removed reviewer with a still-valid JWT, disabled account, no Admin bypass,
deadline, duplicate request, self approval and scoped IT+Marketing read/download.
API evidence is redacted and labels synthetic data and test JWT/storage adapter.
SchemaParityTests covers fresh/upgrade/rerun and mapped columns. Unit/regression CI
remains mandatory before merge and shared rollout.

Requests, reviewer eligibility and current result data are loaded in bounded batches
for list capabilities; no per-row SQL. Calculations are bounded to 1000 student result
targets. Publication revalidates all affected targets transactionally.

After green CI/review: merge, apply/rerun additive migration on AI_PMS, verify schema
and invariants, deploy with Appeals disabled. Keep old image and published revisions.
Enable only after an actual academic policy source and Tin's adapter/browser tests
are accepted. No real appeal, real student data or academic approval is created by
the rollout. This does not close external provider/browser P0/P1 acceptance gaps.
