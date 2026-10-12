# Immutable result revision foundation

Expansion PR4, based on develop 6d57fbb (PR #126). Backend/database only.
This slice is the dependency for the approved appeals workflow. It does not expose
an HTTP correction mutation or enable appeals. The next slice must enforce the
versioned approved policy, scoped independent reviewer and lead staff decision.

## Contract for Tin

Existing project/student result GET responses add `version` (original is 1),
`revisionId` and `supersedesVersion` (null on v1). They resolve the latest published
revision even if the future appeals feature is disabled. Disabling a feature must
not hide a previously published correction.

New GET routes under `/api/v1/projects/{projectId}`:

| Route | DTO | Authorization |
| --- | --- | --- |
| `/result/history?page=1&pageSize=20` | PagedResult of ResultRevisionSummary | Existing project result read predicate |
| `/result/versions/{version}` | ProjectResultDto | Same project result predicate |
| `/students/{studentId}/result/history` | PagedResult of ResultRevisionSummary | Own published result or existing scoped staff/platform read |
| `/students/{studentId}/result/versions/{version}` | StudentResultDto | Same student target predicate |

History is newest first, page 1..1000, pageSize 1..100, count includes original v1.
Example summary: `{"version":2,"supersedesVersion":1,"revisionId":42,"studentId":123,"totalScore":8.12}`.
Student version detail always returns `snapshotJson:"{}"`; project detail returns
`frozenInputs:null`. Typed breakdown comes from the historical target. History
never exposes amendment reasons or other students' roster/comments. `reason` and
`bundleId` in public summaries are null; review details belong to the scoped appeal
workflow. No request field grants authority.

400 invalid paging; 401 anonymous; 403 unauthorized actor/department/target;
404 unpublished/missing target or version. Internal correction conflicts use 409:
`RESULT_PROVENANCE_UNKNOWN`, `AMENDMENT_TARGET_MISMATCH`,
`AFFECTED_RESULTS_MISSING`, `RESULT_REVISION_STALE`.

Governance adds `resultVersion` and `resultRevisionId` from the same resolver.
Portfolio/dashboard currently report lifecycle facts, not numeric result scores;
correction does not reopen a completed/archived project or change those facts.
Original publication notifications continue referencing their original event.
Future correction notifications must be distinct events.

## Persistence and integrity

Migration `20261012_add_result_revisions.sql`, additive, rerunnable with
QUOTED_IDENTIFIER ON, mirrored in schema.sql/manifest/model configuration:

- `result_correction_bundles`: maker, reason, original effective-result stamp, token,
  draft/published state and independent publisher.
- `result_score_amendments`: criterion/evaluation score and reason, unique per bundle.
- `result_revisions`: append-only published snapshots, target/version uniqueness,
  previous revision link and correction bundle. Original result constraints remain.

The internal store requires a transaction and takes the shared project lock before
mutation. It recalculates every affected published target, keeps Individual out of
project results, and refuses incomplete affected publications. Common/Major inputs
must agree across historical targets. Preview binds the full current result set,
amendments, token and calculated snapshots. SQL decimal scale is normalized for
stable confirmation across requests. Publisher differs from maker and original
scorer. Concurrent publication has one winner; audit failure rolls everything back.

Calculation reads only frozen approved scheme/policy, evaluator weights, criteria,
target and exact finalized evidence bindings. Both LEGACY_V1 and DECIMAL_V2 retain
their original rounding. Missing values are not zero. Unsupported/legacy provenance
cannot be reconstructed from current profiles or rubrics. No evaluation finalization
or original result is overwritten. Subsequent revisions chain from the effective
published result. This internal store is not a substitute for PR5 authorization.

## Evidence and rollout

`ResultRevisionCalculatorTests`: historical rounding, zero/missing, corrupt scope,
package/weights/totals, demo denial and complete cross-target propagation.
`Correction_foundation_preserves_v1_recalculates_affected_targets_and_redacts_history`:
actual API scheme/scoring/publication, internal correction transaction, actual API
readback/history, independent maker/scorer guards, stale tokens, audit rollback and
two concurrent publishers. Synthetic SQL database only. Evidence explicitly labels
internal correction calls; it does not claim a completed appeal HTTP lifecycle.

SchemaParityTests checks fresh bootstrap, upgrade, rerun, mapped columns and trusted
constraints. Run release warnings-as-errors, all unit tests and integration/security
CI on isolated SQL before merge. Apply/rerun only this additive migration on shared
AI_PMS after merge, compare schema/invariants, then deploy. Never run this test
lifecycle on shared data. Rollback keeps additive tables/data; application rollback
after actual corrections must retain the effective-result resolver.

Frontend adapter/browser acceptance and approved appeal policy remain separate gates.
