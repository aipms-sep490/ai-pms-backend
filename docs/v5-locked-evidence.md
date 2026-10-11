# V5 locked evidence and PR #110 takeover

The user authorized takeover of PR #110 at `ce42e1362aef23f560b1c1a4f906ad58e4da4859`
on 2026-10-11. Its task filter and contract/test work is retained. This phase builds
on academic authority PR #112 (`942edb2c30deb55c8a544d994d3fa013576ef577`).
Frontend changes remain Tin's responsibility.

Task search additionally accepts `disciplineRole=PRIMARY|SUPPORTING`, with or
without `majorId`. Both predicates match the same discipline row using EXISTS
before count/paging, preventing cross-row matches and duplicate tasks. Unknown
roles return 400. `TaskDisciplineFilterSqlTests` verifies this against SQL via HTTP.

## Declaring provenance

Existing deliverable create/update requests accept optional `evidenceScope`,
`targetMajorId` and `targetStudentId`:

```json
{
  "title": "Individual research report",
  "deliverableType": "REPORT",
  "evidenceScope": "INDIVIDUAL",
  "targetMajorId": 203,
  "targetStudentId": 405
}
```

These IDs are synthetic examples. COMMON has no target, MAJOR_SPECIFIC requires a
major only, INDIVIDUAL requires both. Targets must belong to the frozen registration
roster/major scope. A leader or Primary may declare project targets; a Mentor may
declare only their active major's MAJOR_SPECIFIC target. Students submit only for
their own target/major. Authorization is checked in the project transaction.

Omitted fields preserve legacy behavior and do not infer provenance. Existing
targeted records retain their target when an older client omits it. Once the first
version exists, the deliverable target is immutable, including legacy records:
historical uploads cannot be relabeled into proven evidence. Use a new deliverable.

Migration `20261011_add_deliverable_evidence_targets.sql` adds three nullable
columns, a shape check, two FKs and an index on the existing deliverables table.
No target backfill or new binary-storage model. Bootstrap/manifest/EF mapping are
updated. Locked `files_json` copies the typed target alongside file ID, exact
deliverable version, checksum, size, name and private storage reference. Existing
snapshots without targets remain unproven for major/individual access.

## Reading evidence

- GET `/api/v1/evaluation-assignments/{id}/evidence` retains `items[]`.
- GET `/api/v1/evaluation-assignments/{id}/evidence/files/{fileId}` streams the
  exact locked object. No fallback to live/latest file paths.
- Each item now includes `deliverableVersionId`, `versionNumber` and `sha256`.
- Tin maps `items[]` to the existing panel's `files[]` in the FE adapter.
- No response exposes the private storage reference.

Every evidence request rechecks active account/assignment, frozen project scope,
published component, period/policy, rubric department, major/student target and
locked package. New assignments pin `FinalSubmissionId` in their internal scope
snapshot. Older scoped assignments use the existing unique final package per
project; the current schema prohibits replacing it with a second package.
COMMON can read the locked package; major assignments filter frozen target majors;
individual assignments require both frozen student and major. Free text, uploader,
current profiles and mutable file rows do not establish evidence ownership.

Managers can still read assignment history, but a revoked assignment's evidence
route is denied even for a manager. Scoped evaluators cannot use the generic
project final-package read/download route to bypass their assignment filter.
An independently authorized Primary retains their existing project authority.

| HTTP | Meaning |
|---|---|
| 400 | Invalid target shape or query |
| 401 | Unauthenticated |
| 403 | Wrong/revoked assignment, component, department or file target |
| 404 | File absent from the locked package or object missing |
| 409 | Target cannot be changed after submission, unknown source roster, or missing locked storage reference |

No new concurrency token is invented: deliverable target changes serialize under
the existing project lock; submissions use expectedLatestVersion; package locking
uses draft/requirements tokens; assignment revocation uses its assignment token.

## Validation boundaries

`TargetedEvidenceJourneyTests` drives declare/upload/draft/requirements/lock/scheme/
assignment/download/revoke through HTTP for major and individual targets, then
checks DB snapshots and audit. Organization, roster and rubric inputs are synthetic
fixtures; that is not proof of the entire registration lifecycle.
`LockedEvidenceSecurityTests` checks component/package mismatch, inactive scope,
missing storage reference and major/student isolation. Existing PR #110 tests
retain legacy-negative, cross-project and task-filter coverage. SchemaParityTests
compare fresh/upgrade/rerun on isolated databases.

The full v5 scoring/COLD/weekly/collaboration acceptance remains separate. No FE
browser acceptance, official rubric approval or P0_P1 readiness is implied.
