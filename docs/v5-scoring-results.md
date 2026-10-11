# v5 phase 4: versioned calculation and historical results

Scope: BE-09, BE-17 and BE-N11. No frontend changes. COLD stage mapping is phase 6;
weekly demo scores, industry advisory and course-grade conversion are not included
in final results. Technical validation does not approve an academic rubric.

## Contract and authority

Existing `/api/v1/evaluation-schemes` create/update/get/list/version/publish routes
remain canonical. Only frozen lead-department staff manages a whole scheme and
publishes results. Participating staff may manage its own draft components and
preview its own student target; Admin alone cannot act as academic publisher.

`SaveEvaluationSchemeRequest.policy` is additive. Omission on create defaults to
`isDemo=true`, `approvalStatus=DEMO`, `formula=COMPONENT_WEIGHTS`,
`calculationVersion=DECIMAL_V2`, `roundingMode=AWAY_FROM_ZERO`, `decimalPlaces=2`.
Omission on update retains existing metadata. Official use requires
`isDemo=false`, `approvalStatus=APPROVED`, and a nonempty `approvalReference` from
the responsible academic authority (maximum 1000 characters). The reference is
an explicit recorded source, not automated verification of a school's approval.
No reference is fabricated for historical schemes.

`PUBLISHED` still means the scheme is frozen. `lockedAt` records that instant;
demo schemes can be locked and scored but cannot publish official results.
A successor starts DRAFT/DEMO without inherited approval or lock time.

Optional component `evaluatorWeights` is an array of
`{ evaluatorId, weightPercent }`. It must contain unique positive IDs, exactly
`requiredEvaluators` slots and exactly 100 percent, with at most four decimal
places. Omission retains equal-evaluator mean behavior. Candidates filter these
slots before paging/count; assignment revalidates the selected slot and all
existing department, D17, component and student guards. Missing slots are pending;
their weight is never redistributed.

The existing project/student result preview, publish and GET routes gain
`breakdown`: schemeName, schemeVersion, calculationVersion, isLegacy, isDemo,
approvalStatus, readiness, unroundedTotal, commonScore, majorAggregate,
majorBreakdown and components. Component rows include scoped identity, weight,
nullable score and required/finalized evaluator counts. `readiness=READY` means
the score is calculable; publication still requires `canPublish=true` and no
blockers. `canPublish`, governance readiness and mutation use the same checker.

Students only GET their own published result. The public breakdown contains no
student roster, other student's individual scores, evaluator identity/comments or
raw audit JSON. Student and participating-staff `snapshotJson` remains `{}`.
Department visibility for a student result uses the historical publication target.
Project GET excludes `frozenInputs` and individual breakdown rows.

## Calculation and immutability

`DECIMAL_V2` recomputes decimal criterion totals from protected finalization
snapshots, keeps unrounded evaluation/component/total values in the publication
snapshot and rounds the final result once. The finalized evaluation's V1 total is
still checked for integrity. Historical `LEGACY_V1` explicitly retains rounded
evaluation inputs and the original equal-mean calculation. Existing results are
read without recalculation from current rubric, live profile or live score rows.

`COMPONENT_WEIGHTS` uses the frozen project/student component weights. Individual
components cannot contribute to a project result. `DEMO_50_30_20` is demo-only:
one Common, one Major and one Individual component per major; student weights
50/30/20 and project Common 50 plus 50 times the equal mean of major scores.
The group formula determines the exact equal-major mean even when three stored
component percentages require four-decimal representation. It never adds an
industry component or an extra 30/70 stage multiplier.

Demo examples: Common 8; Major IT/Marketing/Design 9/7/6; Individual 7/9/8 gives
students 8.10/7.90/7.40 and project 7.67. Missing differs from explicit zero.
Rounding mode is versioned (`AWAY_FROM_ZERO` or `TO_EVEN`), two decimal places.

Publication locks the project, re-reads scope/package/policy/finalized inputs,
checks a SHA-256 confirmation token over those inputs, then writes result,
lineage, audit and queued workflow notification in the existing transaction.
Project publication also checks every mandatory student slot before completion.
Finalization snapshots load in a single project query, avoiding per-slot SQL.
Published result uniqueness and no-overwrite behavior remain unchanged.

## Errors, examples and tokens

| Situation | HTTP / response |
|---|---|
| Invalid approval metadata, formula, weights or rounding | 400 validation problem |
| Missing login | 401 |
| Admin-only, wrong department/project/student | 403/404 according to existing resource visibility |
| Missing evaluators/score | Preview 200 with null total, PENDING and deterministic blockers |
| Demo result ready | Preview 200 with score and DEMO_SCHEME_NOT_OFFICIAL; publish 409 |
| Legacy scheme with no approval provenance | ACADEMIC_APPROVAL_UNKNOWN, isLegacy=true, no official publication |
| Policy not locked/bound to scheme period | FROZEN_POLICY_REQUIRED; publish 409 |
| Missing approval source | ACADEMIC_APPROVAL_REQUIRED; publish 409 |
| Stale preview token / already published | 409; load a fresh preview, never blindly retry publication |
| Changed scheme token / attempt to update a locked component | 409 |

Example default/demo request fragment:

```json
{"policy":{"isDemo":true,"approvalStatus":"DEMO","formula":"DEMO_50_30_20","calculationVersion":"DECIMAL_V2","roundingMode":"AWAY_FROM_ZERO","decimalPlaces":2}}
```

Scheme writes require the current scheme concurrency token; publish results
requires the exact confirmation token from the same project/student preview.
There is no token-expiry timer or server token store. Changed inputs invalidate
the token. Never infer permission from frontend role or a numeric result alone.

## Database and validation

Additive rerunnable migration `20261011_add_scheme_governance_metadata.sql` adds
nullable scheme governance JSON/lock time and component evaluator-weight JSON,
with trusted JSON checks. No backfill, EF migration or new score/result table.
Bootstrap, ordered manifest, EF configuration and selected readiness checks agree.
NULL legacy metadata remains UNKNOWN.

Tests: `VersionedResultCalculatorTests` covers the golden scores, missing/zero,
V1/V2 rounding, invalid weights/approval and typed historical redaction.
`VersionedResultEndpointTests` covers default demo denial, locked metadata,
weighted slots, pending scores, stale token, repeated publication, immutable
readback and legacy successor behavior on owned isolated SQL databases.
Existing tests cover two departments/individual visibility, concurrent publication
and audit/notification rollback. Regression fixtures that publish officially now
declare an explicitly synthetic approval reference; it is never a real policy.

Release/full-suite/CI and shared rollout status are tracked in
`v5-backend-implementation.md`. Do not enable an FE feature solely from this file.
