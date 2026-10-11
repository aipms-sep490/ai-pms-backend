# v5 phase 6: scoped COLD evaluation and progress

Tickets BE-N02/BE-N03. Reuses evaluation assignments, draft scores/details,
finalizations, versioned rubrics, schemes and result lineage. No cold_scores
table and no implicit change to project lifecycle or official results.

## Contract for Tin

Scheme component input adds `stage`: ON_GOING, COLD, DEFENSE. New requests that
omit it default to DEFENSE; historical database rows remain null (unknown stage),
with no inferred backfill. Stage is independent of COMMON/MAJOR_SPECIFIC/INDIVIDUAL.
Published components are immutable. Existing legacy scoring keeps its recorded
scope/calculator rules; no historical result is recalculated.

Assignment input accepts COMMITTEE in addition to LECTURER. It does not grant
access by itself and does not automatically add defense committee membership.
New SUPERVISOR assignments remain retired. The same D17 project lock and
Primary/evaluator conflict checks apply. Candidate types list both supported new
assignment types. Stage comes from the published component, not an actor's role.

Example tracking component (illustrative IDs, not approved policy):

```json
{
  "name": "COLD individual report",
  "scope": "INDIVIDUAL",
  "majorId": 12,
  "rubricId": 34,
  "projectWeightPercent": 0,
  "studentWeightPercent": 0,
  "requiredEvaluators": 2,
  "stage": "COLD"
}
```

Zero result weights are allowed only for a COLD tracking component. Positive
weights explicitly map that component into the existing scheme calculation;
all existing 100-percent and individual/project rules remain enforced. COLD
does not introduce an extra 30/70 multiplier, automatic reweighting or default
result contribution. A tracking component does not block official publication;
mapped components retain all required-evaluator guards. Demo schemes still
cannot publish official results.

Existing endpoints carry the extension:

| Endpoint | Change |
|---|---|
| POST/GET `/api/v1/evaluation-schemes` and scheme detail | Component stage |
| POST `/api/v1/projects/{id}/evaluation-assignments` | COMMITTEE accepted, server pins stage/COLD package and manifest |
| GET `/api/v1/evaluation-assignments/{id}` | Stage, coldPackageId, CanScore checks current window/scheme/evidence |
| GET `/api/v1/evaluation-assignments/{id}/evidence` | Stage, coldPackageId and items[] from exact locked COLD versions |
| GET `/api/v1/evaluation-assignments/{id}/evidence/files/{fileId}` | Active assignment and exact target/version download |
| POST assignment evaluation; PUT evaluation draft; POST evaluation finalize | Existing concurrency and rubric workflow; COLD binding checked on each mutation |
| GET `/api/v1/projects/{id}/cold/progress` | Scoped report/lock counts, grading slots, component scores and blockers |

COLD evidence DTO `finalSubmissionId` is null: COLD IDs must never masquerade as
final-submission IDs. Each item has sourceType COLD_SUBMISSION, coldSubmissionId,
versionNumber, checksum, metadata and authorized download URL. deliverableVersionId
is null. Storage keys and internal binding hashes are not returned. Keep items[];
map it to files[] only in the FE adapter.

Evaluation finalization retains its real finalSubmissionId for existing lineage
and adds stage, coldPackageId and coldSubmissionIds. For COLD, artifact/file counts
refer to the scoped COLD versions and deliverableVersionIds is empty. The private
audit snapshot also binds the exact COLD manifest (target, template, version,
checksum, opaque key), so later tampering fails closed.

## Authorization and readiness

The existing project FINAL_SUBMISSION, locked final package and single active
EVALUATION period are still prerequisites to configuring/scoring a scheme.
Additionally, COLD candidate/assignment/scoring needs the locked COLD package
with complete reports for the selected target. This phase does not add a separate
early-evaluation project lifecycle or reopen a completed project.

Frozen lead manages the whole scheme/project. Participating department staff
manage only their rubric/major components. Only the active assigned lecturer can
score. Admin alone cannot assign/score/publish through academic authority. Existing
platform assignment-inspection behavior is retained. Revoked assignments cannot
read evidence even through staff inspection. Generic COLD routes do not grant
evaluators project-wide access; they use assignment evidence routes instead.

COMMON sees the locked COLD package; MAJOR_SPECIFIC sees that major; INDIVIDUAL
sees only that student in the assigned major. A file outside the selected COLD
manifest returns 404. Replaced live files never change the locked download key.
Altered locked manifest returns 409 and CanScore=false.

Progress uses the same COLD project read scope: lead/Primary all, participating
staff/Mentor their major, students their own major/individual targets. Students
never receive another student's individual row or evaluator comments/identity.
Missing slot/finalization produces null, not zero. Component mean is returned
only after all required evaluators finalized against the pinned evidence; actual
zero remains 0. There is no invented aggregate across differently scoped tracking
components; official mapped totals continue through result preview/publication.

Blockers include COLD_PACKAGE_REQUIRED, COLD_SCHEME_REQUIRED,
COLD_COMPONENT_REQUIRED, COLD_GRADING_PENDING and COLD_EVIDENCE_CHANGED.
401 requires login; 403 denies actor/assignment scope; 404 hides a foreign file;
400 rejects invalid stage/weights/type; 409 handles stale tokens, changed evidence,
missing package/window or frozen state. Reload capability/data after 409.

## Database and verification

`20261011_add_cold_evaluation_stage.sql`: nullable component/assignment stage and
assignment cold_package_id, trusted stage checks, FK/index, COMMITTEE assignment
constraint. Existing evaluations already permit COMMITTEE. Additive and rerunnable
with QUOTED_IDENTIFIER ON; no data backfill. Bootstrap, manifest, EF and selected
readiness checks change together. Deploy schema before compatible application.

Read models batch assignments/finalizations and locked manifests; no storage or
SQL query per evaluator slot. Limits inherit the bounded project checklist (100
items), scheme (100 components), roster and 20 evaluator slots/component.

ColdEvaluationEndpointTests cover all three target scopes, pre-lock refusal,
exact file read, revoked assignment denial, required slots/zero, explicit result
mapping and tamper denial. ColdAcceptanceJourneyTests call actual final-package,
COLD upload/submit/lock, rubric, scheme, assignment, draft and concurrent finalize
APIs, followed by scoped readback and SQL/audit checks. Fixtures contain synthetic
accounts/configuration; storage is deterministic memory, not an external provider
claim. Full-suite/CI/shared rollout status is recorded in the v5 tracker.

Local validation: Release warnings-as-errors and all 1,054 unit/1,192 integration
tests passed. Additional interdisciplinary/rollback hardening is checked in the
39-test focused API suite. Fresh/upgrade/rerun schema parity and isolated bootstrap
readiness/ownership guards passed. CI runs the entire suite including the added
rollback test before merge and shared migration.

No FE code or browser acceptance is included. Tin should retain feature flags
until adapter and browser lifecycle validation pass.
