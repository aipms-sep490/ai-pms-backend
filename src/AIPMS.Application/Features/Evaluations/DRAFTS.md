# Evaluator assignment and draft scoring

## Document alignment and accepted temporary scope

Sources: Report 3 SRS sections 3.16.4 (Assign Evaluator), 3.16.5 (View Assigned
Evaluations), 3.16.6 (Evaluate Project by Rubric), 3.16.7 (Submit Evaluation),
BR-143/144/145; issue #14; Report 2's dependency-first evaluation lifecycle.

The SRS requires a locked final-submission package before assigning evaluators.
Develop currently has the FINAL_SUBMISSION project status but no BE-16 final-package
module. The user explicitly approved the following temporary scope on 2026-09-11:
implement assignment and drafts, require FINAL_SUBMISSION plus a valid evaluation
window, defer verification of the locked final package to BE-16, and do not expose
finalize. A project status is NOT evidence of a locked package. This is draft-only
foundation, not a complete final-evaluation or SRS acceptance flow.

| Requirement | Implemented boundary |
| --- | --- |
| Staff assigns eligible evaluator | Persisted active staff in the project/assignment department, or active admin; candidate must be a persisted active lecturer in that department |
| Supervisor when assigned evaluator role | An explicit SUPERVISOR evaluation assignment plus current primary supervision is required; supervision alone grants no grading right |
| Verify final submission and evaluation window | FINAL_SUBMISSION and active academic scope/window enforced; locked-package verification deferred by explicit user agreement |
| Published rubric version | New assignments resolve the period's PUBLISHED rubric, validate its criteria, and bind immutable rubric ID, department and period |
| Assigned list and resource access | Active eligible evaluator sees own assignments/drafts; staff sees own department, admin can read/manage; students cannot read unpublished grades |
| Score bounds | 0 <= score <= criterion.maxScore, <=2 decimal places, only the protected rubric's criterion IDs |
| Deterministic preview | Weighted decimal calculation on scale 10; one final rounding away from zero to 2 decimals; no client-provided total |
| Missing criteria | Draft can be incomplete; missing scores return null total and missing/all-required ID lists; no implicit zeros or weight redistribution |
| Save draft | Full score-set replacement plus comments, concurrency token and transactionally persisted audit |
| Finalize/publication/AI | No finalize, publish-result or AI score-write API in this slice |

Assignment notifications (SRS 3.16.4 step 5), evaluator discovery UI/lookups,
evidence-summary/final-package linkage, finalize notifications, committee/common/
major-specific/individual assignments and per-student results remain separate.
Existing SUPERVISOR and LECTURER types are supported; COMMITTEE/FINAL type requests
are rejected until their assignment/lifecycle rules are implemented. Issue #14 is
not fully Done. Progress Reports & Meetings issue #7 remains Dong's work.

## API

Every endpoint requires authentication and returns ProblemDetails for errors.
Responses use UTC times and disable caching. Controllers only dispatch MediatR.

| Method | Route | Purpose |
| --- | --- | --- |
| POST | /api/v1/projects/{projectId}/evaluation-assignments | Staff/admin assigns evaluator; 201 |
| GET | /api/v1/projects/{projectId}/evaluation-assignments | Staff/admin list, optional status ACTIVE/REVOKED, page/pageSize |
| GET | /api/v1/evaluation-assignments/my | Lecturer's eligible active assignments, page/pageSize |
| POST | /api/v1/evaluation-assignments/{id}/revoke | Staff/admin revokes assignment with concurrencyToken and reason |
| POST | /api/v1/evaluation-assignments/{id}/evaluation | Assigned evaluator creates draft; 201; duplicate returns 409 |
| GET | /api/v1/evaluations/{id} | Authorized managed evaluation detail and protected rubric criteria |
| PUT | /api/v1/evaluations/{id}/draft | Assigned evaluator replaces draft score set/comments |
| GET | /api/v1/projects/{projectId}/evaluations | Staff/admin scoped list or lecturer's own eligible evaluations |

Assign body: evaluatorId, projectPeriodId, evaluationType (LECTURER or SUPERVISOR).
The server derives rubricId from BE-12's period configuration. There is one active
assignment per project/evaluator/type, and one evaluation per assignment. Revocation
retains history; a subsequent assignment is a new ID, not a rewrite of old grades.
Assignments do not automatically create scores or send external messages.

Save body example:

```json
{
  "concurrencyToken": "token-returned-by-the-latest-read",
  "comments": "Overall draft feedback",
  "scores": [
    { "rubricCriterionId": 101, "score": 9, "comments": "Good design" },
    { "rubricCriterionId": 102, "score": 16, "comments": "Clear presentation" }
  ]
}
```

PUT replaces the full set: omit a criterion to clear its draft score/comment;
send an empty scores array to clear all scores. Each supplied entry must have an
explicit non-null score; criterion-only comments without a score are not stored
in this slice. Overall comments may be saved with no scores. Duplicate criterion
IDs/null entries are invalid. Score comments <=2000, overall comments <=10000,
scores <=100, pageSize <=100 (default20), page>=1 (default1).

The detail response includes rubricName, rubricId, rootRubricId, rubricVersion,
criterion descriptions/weights/maxima, stored score/comments, totalScore (preview),
scoreScale, calculationRule, missingCriterionIds and missingRequiredCriterionIds.
The evaluator does not need access to rubric-management APIs to render the rubric.
Latest concurrencyToken is required for Save and Revoke. Stale/duplicate operations
return 409; foreign criterion/max-range errors return 409, malformed input returns
400, unauthorized resources return 403 and missing/unmanaged IDs return 404.

## Calculation and state

Implementation default proposed to the user: scale10, two decimal places,
MidpointRounding.AwayFromZero. This is a stated implementation policy, not a
rounding rule quoted from the SRS. Rule ID is persisted as
WEIGHTED_10_AWAY_FROM_ZERO_2DP_V1 and exposed in responses.

`totalPreview = Round(sum(score / maxScore * weightPercent) / 10, 2, AwayFromZero)`

Use decimal arithmetic, stable criterion-ID summation order and no intermediate
rounding. Example: 9/10 at 60% plus 16/20 at 40% gives 8.60/10. Zero is a score,
not missing data. Total stays null whenever any criterion lacks a score, including
optional criteria: this avoids silently deciding how unscored optional weights
affect a final result. Future finalize must explicitly settle that rule.

Create/save requires project FINAL_SUBMISSION; active organization, departments
and majors; active semester with current UTC date inside its dates; the assigned
period is ACTIVE/EVALUATION, startAt <= now < endAt, same semester, and exactly one
active evaluation window covers now. Revalidate each write. Ending the window
does not remove authorized read access. Students receive results only through
future BE-16 publication, never these draft APIs.

New assignment requires the selected rubric be PUBLISHED and match the evaluator's
department and project's semester. Existing assignments keep their original rubric
even if BE-12 selects a new one or the original is retired. This follows the frozen
version contract: retirement prevents new assignments, not use of an already-bound
protected version. Existing rubric weights/score ranges must still be valid.

Revoke is allowed outside the evaluation window so staff can remove access, but
not on COMPLETED/ARCHIVED projects or evaluations already SUBMITTED/FINALIZED.
Revoked evaluators immediately lose access to the draft; authorized department
staff/admin retain history. Removed persisted lecturer roles, inactive accounts,
department changes and ended primary supervision revoke eligibility on each call.
Admin/staff read/manage permission alone never authorizes entering scores.

## Persistence, concurrency and deployment

Run `db/changes/20260911_add_evaluation_assignments_and_drafts.sql` after schema.sql
and the rubric-version migration. The additive, rerunnable script creates
evaluation_assignments and evaluation_draft_states. Existing evaluations/details
and generated models are unchanged; state/assignment mapping uses context partials.
The application never auto-migrates on startup.

No historical assignments are invented for legacy evaluations. Those records are
preserved but excluded from these managed draft endpoints; a separate reviewed
legacy-import/read contract is needed. Re-running migration changes no existing
assignment, grade, token or rubric reference.

Serializable transactions keep academic eligibility, assignments, protected rubric,
evaluation status, scores and audit consistent. Writes lock project then rubric
family/source; existing row reads retain locks through commit. SQL deadlocks,
uniqueness/FK/concurrency conflicts map to 409, allowing reload/retry. Read endpoints
also use a consistent transaction so score content and token cannot come from
different writes. Score changes and any later audit failure roll back together.

Future finalize must use the same project/evaluation transaction discipline,
revalidate assignment/window/required criteria/final package, calculate totals
server-side and protect the result. This slice rejects edits when persisted state
is SUBMITTED/FINALIZED, but does not implement the finalization endpoint itself.
