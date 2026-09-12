# BE-16 final-submission drafts (slice 1)

Issue #21, based on SRS Report 3 sections 3.16.1-3.16.3 and BR-140/141/142.
This slice lets the current student leader prepare selections before the official
submission. It does not implement the complete final-submission use cases.

## Contract

All routes require an authenticated ACTIVE persisted STUDENT account with active
department/organization and current project team membership. Team members can
read; only the current leader can write. Staff, admins, supervisors and evaluators
do not gain draft access merely from their roles. Official locked-package read
scope is a later slice (SRS 3.16.3). Old token roles never override persisted roles.

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/v1/projects/{projectId}/final-submission-periods` | Discover the project's semester FINAL_SUBMISSION windows and reasons preparation is blocked; page/pageSize, stable startAt/id descending order |
| POST | `/api/v1/projects/{projectId}/final-submission-draft` | Create the project's single draft, 201; duplicate 409 |
| GET | `/api/v1/projects/{projectId}/final-submission-draft` | Read selected versions and file metadata, deadline, edit permissions; absent draft 404 |
| PUT | `/api/v1/projects/{projectId}/final-submission-draft` | Replace notes, period and the complete version selection using the current concurrency token |

POST body:

```json
{
  "projectPeriodId": 12,
  "notes": "Prepared for final submission",
  "deliverableVersionIds": [101, 203]
}
```

PUT uses the same fields plus `concurrencyToken` from the latest response. A
successful write rotates the GUID token. Empty selections are valid drafts;
omitting a previously selected ID from PUT removes it. Null selection collections,
duplicate/nonpositive IDs, more than 100 selections and notes over 10,000 UTF-16
code units return 400. Invalid/stale references, periods or states return 409.
Notes are trimmed. Period pages start at 1, maximum 1,000,000; pageSize 1-100.
All errors use ProblemDetails and all responses disable caching. Dates are UTC.

## State and time

Writing requires an ACTIVE project with active academic scope, an ACTIVE semester
within its inclusive date range, and an ACTIVE FINAL_SUBMISSION period in that
semester with `startAt <= now < endAt`. Exactly one active final window may cover
now. Execution/evaluation periods are not substitutes and no late/grace override
is inferred. These rules apply to draft writes as the boundary of this slice.

The team can still read an existing draft after the window closes or the project
leaves ACTIVE. `canEdit` is false and `editBlockers` explains why. Period discovery
works before a draft exists, including upcoming/closed windows. No windows returns
an empty list. A later valid final window can be explicitly selected on PUT using
the current token; the prior period is retained in the audit. No automatic rebinding.

`canEdit` / `canPrepareDraft` describe preparation access, not final submission
readiness. They must never enable an official Submit button: there is no submit
endpoint in this slice and configured final checklist validation is not available.

## BE-08 version/file integration

Select explicit deliverable-version IDs from the existing BE-08 deliverable and
version-list APIs. Each selected version must belong to this project, be SUBMITTED
or ACCEPTED, and contain files with a single version parent, a nonempty name/storage
reference, positive size and a valid SHA-256 metadata value. Select at most one
version per deliverable. Selecting an older eligible version is intentional: SRS
specifies selected versions, not automatic replacement with the latest version.

The draft references versions, not public URLs, local paths or arbitrary report/
meeting attachment IDs. BE-08 version content is immutable through its API. Files
are displayed as safe BE-08 metadata and downloaded through the existing authorized
`GET /api/v1/files/{id}/download`. No content upload, copy or storage-provider change
is introduced here. Upload artifacts using BE-08 before preparing final selections;
BE-08's own execution/deadline rules continue to apply.

This is not a locked snapshot. Version review status and descriptive metadata are
read live; if a selected version is subsequently rejected, its item is returned
with `isEligible=false`. The leader can replace/remove it, but cannot save it again
as eligible. Newer uploads never replace the selected ID. Storage content existence
and the complete configured checklist must be revalidated by the future submit flow.

## Persistence and concurrency

Apply `db/changes/20260912_add_final_submission_drafts.sql` before deploying this
slice. It adds only `final_submission_drafts` (unique project) and
`final_submission_draft_items` (selected version references); it is rerunnable and
does not backfill legacy projects, modify Generated/schema.sql or submit projects.

All operations use a serializable transaction. Writes lock the project before
reading authorization, state or version metadata, coordinating with BE-08. Persisted
membership/roles, academic configuration and referenced rows stay protected until
commit. Draft creation/selection/token and audit share one transaction. Deadlocks,
expected unique/FK conflicts and EF concurrency errors map to 409. Time is checked
again after persistence/audit so a request crossing the deadline rolls back.

## Traceability and remaining work

- SRS 3.16.1: team scope and final-window discovery implemented; configured required
  deliverables/reports/evidence and exact checklist completeness remain for slice 2.
- SRS 3.16.2 / BR-141: leader-only preparation and audited draft changes implemented.
  Official FINAL_SUBMISSION/FINAL_SUBMISSION_ITEM records, snapshot/lock,
  ACTIVE -> FINAL_SUBMISSION and notifications remain for slice 2.
- SRS 3.16.3 / BR-142: draft detail and private BE-08 file access implemented;
  immutable official package and staff/supervisor/assigned-evaluator scope deferred.
- BE-09 locked-package precondition is not connected by this slice. Existing draft
  grading remains as previously accepted; finalize/result publication/complete/
  archive are later work. Issue #21 remains partial.

Foundation currently has no authoritative required-artifact checklist configuration.
Do not equate all deliverables with required artifacts or invent a fixed document
list. This contract needs to be defined before slice 2 can safely open submission.

Tests cover real BE-08 upload/download and exact version retention, scope/role/leader
changes, period boundaries and invalid configuration, rejected/foreign/missing
versions, concurrent creates/updates, audit rollback, deadline crossing, project
state races and rerunning the migration. SQL tests use owned isolated databases.
