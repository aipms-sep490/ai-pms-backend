# BE-16 project result publication

SRS Report 3 section 3.16.8, BR-145/160; issue #21. Builds on BE-09 finalized
evaluation snapshots and BE-16 locked final packages. Archive remains separate.

## Confirmed policy (2026-09-12)

Department staff configure each project's required evaluation assignment IDs,
positive weights totaling exactly 100%, and a pass threshold from 0 to 10.
Threshold/weights accept at most two decimal places; 1-100 unique assignments.
Assignments must be active, belong to the project and have eligible evaluators.
Staff must manage every included assignment department; only an administrator
can configure/preview/publish a policy spanning multiple departments. Staff in
one department cannot replace a cross-department policy to omit other grades.

Configure before ANY project evaluation finalizes, including an evaluation not
in the required set. Finalize now requires a policy and all listed assignments
to remain active. The first finalized evaluation freezes the list, weights and
threshold. Required assignments cannot then be revoked, even while their own
evaluation is still a draft. Before the freeze, revoke is allowed but the policy
must be repaired before anyone finalizes. Extra assignments can finalize normally;
only explicitly selected assignments contribute or block publication.

Each required evaluation must have a finalization snapshot referencing the same
locked package, assignment, evaluator and rubric. Recalculate its criterion total
and verify the finalized score. Aggregate those two-decimal scores as
sum(score * weight / 100), then round once to two decimals AwayFromZero.
Compare the rounded total >= passThreshold for PASSED, otherwise FAILED.
Both outcomes transition to COMPLETED: this status means evaluation finished.
No outlier removal, missing-as-zero, automatic redistribution, or AI scoring.

SRS does not specify aggregation or a pass threshold; the rules above are the
user-confirmed extension, not an inferred SRS requirement. There is no additional
publication deadline: grading windows govern scoring/finalization, and staff may
publish after they close. Project must still be FINAL_SUBMISSION in active
academic scope. A result policy is never fabricated for legacy finalized grades;
projects already finalized without a policy need a separate reviewed migration.

## API

All routes start with /api/v1/projects/{projectId}:

| Method | Route | Result |
| --- | --- | --- |
| GET | /result-policy | Policy/token/isLocked; 204 if not yet configured |
| PUT | /result-policy | Configure with threshold, assignments and current token |
| GET | /result/preview | Blockers, total, outcome, contributions, confirmationToken |
| POST | /result | Publish using the preview confirmationToken; 201 |
| GET | /result | Immutable published result; 404 before publication |

First policy PUT uses concurrencyToken:null. Updates require the current GUID.
Publication requires a 64-character hex token calculated from current policy,
finalized inputs, package and project state. A changed preview or repeat publish
returns 409. Invalid requests return 400, unauthenticated 401, wrong authority 403.
Errors use ProblemDetails; all endpoints are no-store. Actors come from persisted
active roles and academic scope, not client claims or submitted actor fields.

Team members can read only the published result. Active assigned evaluators,
primary supervisor, scoped staff and administrators retain academic read access.
The published snapshot exposes total/outcome and numeric contributions with
assignment/evaluation/rubric IDs, weights and tokens. Private criterion comments
and general evaluator feedback are not released; feedback-release policy remains
separate. There is no edit, delete, reopen or republish route.

## Persistence and delivery

Serializable transactions and the shared project write lock coordinate policy,
finalize, revoke and publication. One result per project is enforced by SQL.
The publication snapshot contains the exact threshold, policy token, formula
version, scores, weights, final package, publisher and UTC time. Foreign keys
retain the underlying finalization records/package. Reads use this snapshot,
never recompute results from later live rubric/policy/evaluation changes.

Result insert, FINAL_SUBMISSION -> COMPLETED, audit and in-app notification
commit atomically. Failure rolls everything back. SQL deadlock, uniqueness,
foreign-key and concurrency failures return 409. Active student members of the
current team receive PROJECT_RESULT_PUBLISHED, including nonleaders; left/inactive
members and the actor are excluded. Notification uses PROJECT/projectId for
navigation, contains no grades/comments and source-row locking deduplicates replay.
No email, external delivery or outbox is introduced.

Apply db/changes/20260912_add_project_results.sql AFTER the evaluation finalization
migration and BEFORE deploying the API (including the new finalize gate).
It adds four tables, nine foreign keys, three checks and seven indexes, with no
historical backfill. Rerunning preserves existing data. No Generated files or
canonical schema.sql changes. Privileged SQL maintenance is outside application
immutability guarantees. Shared SQL application and API deployment are separate
from local implementation/testing.
