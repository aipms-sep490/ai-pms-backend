# BE-09 evaluation finalization

SRS Report 3 sections 3.16.6-3.16.7, BR-143/144/145; issue #14.
Builds on protected rubric/draft scoring and BE-16 locked packages (PR48).

## Confirmed scoring policy

On 2026-09-12 the user explicitly required a score for every weighted criterion
at finalization, including criteria marked optional. No missing score becomes
zero and no weight is redistributed. Drafts can remain incomplete. Final score
uses the same rule as preview: sum(score / maxScore * weightPercent) / 100 * 10,
decimal arithmetic in criterion-ID order, one final rounding to two decimal
places with MidpointRounding.AwayFromZero. Zero is a valid explicit score.

Each evaluator finalizes their own evaluation independently. This does not
average/combine evaluators, determine pass/fail, publish results or transition
the project to COMPLETED. The policy for which evaluations must be completed and
how to aggregate them belongs to result publication and remains to be agreed.

## API and authorization

POST /api/v1/evaluations/{id}/finalize
```json
{"concurrencyToken":"<current evaluation GUID token>"}
```
No score, total or actor fields are accepted as authoritative input. Save scores
through the draft route, read/confirm the current preview, then finalize using
that token. Success returns200 with EvaluationDraftDto status FINALIZED and a
new token. Existing GET/project list also return final evaluations under existing
academic/evaluator permissions. The DTO adds nullable finalization metadata:
finalizedBy/At and an evidence summary (locked final package ID, final period,
submittedAt, artifact/file counts and exact selected version IDs). Drafts have
null finalization. Dates are UTC, responses no-store, errors ProblemDetails.

Only the active persisted assigned lecturer may finalize; SUPERVISOR evaluation
also needs active primary supervision. Staff/admin read/manage authority never
grants scoring/finalization. Revalidate project FINAL_SUBMISSION, active academic
scope, one current EVALUATION period in its active semester, locked package,
assignment and bound PUBLISHED/RETIRED protected rubric. The assigned version
remains in use when BE-12 changes its selection. Existing score bounds and total
weight100 rules are revalidated. Missing/stale/invalid conditions return409;
wrong actor/role/scope returns403, invalid request400, missing evaluation404.

## Atomic lock and evidence

Serializable transaction and project XLOCK coordinate draft save, revoke and
finalize. Both token and DRAFT status must still match. Server recomputes total
from criterion scores, ignoring any saved preview total, keeps existing criterion
detail rows and IDs, sets FINALIZED/EvaluatedAt and rotates the token. A separate
evaluation_finalizations row stores a fixed scoring/rubric/actor/time/evidence
snapshot and a foreign key to the locked final package. GET after finalization
reads this snapshot so later criterion label changes cannot alter the displayed
evaluation. No normal edit/delete/reopen route exists; save and revoke reject
final evaluations, including when a live status was changed administratively.

Audit EVALUATION_FINALIZED and in-app EVALUATION_FINALIZED notification are saved
in the same transaction. Recipients are active staff in the assignment department
and project/organization scope; related entity EVALUATION/id links to existing
GET. No grade/comment text in the notification, no student delivery before
publication. Source locking deduplicates internal event replay. Repeat HTTP
finalize returns409 without a second snapshot/audit/notification. Deadline is
checked again after audit/notification writes, rolling back the whole operation
if it crosses the boundary. Deadlock/unique/FK/concurrency errors return409.

Evidence is deterministic from the locked final package. Full progress/feedback/
contribution dashboards and optional AI summaries remain separate; no AI scoring
or publishing is introduced. Privileged DB/storage maintenance is outside the
application immutability guarantee.

## Deployment

Apply db/changes/20260912_add_evaluation_finalizations.sql after evaluation draft
and BE-16 locked-package migrations, before deploying this API. It adds one table,
three foreign keys, a JSON check and submission index. Rerunnable, no historical
backfill, no changes to Generated/schema.sql or existing grades. Legacy finalized
rows are not re-finalized or given fabricated metadata. SQL is tested only on
owned isolated databases in the implementation task; shared-server deployment
is a separate step. The API does not auto-migrate.
