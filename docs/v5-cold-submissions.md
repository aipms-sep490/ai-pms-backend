# v5 phase 5: COLD reports by major and immutable version

Ticket BE-N01. Scope: template, checklist, draft, file upload, submit, resubmit,
lock, history and authorized download. This phase does not grade COLD, create an
evaluation assignment, change project status or publish an official result.
Stage/COMMITTEE assignment, evaluator read and score mapping are phase 6.

## API and FE integration

All routes below are under `/api/v1`; require an authenticated active account.

| Method / route | DTO / purpose |
|---|---|
| GET `/cold-report-templates/examples/it` | Seven explicit IT examples; no automatic policy assignment |
| POST `/cold-report-templates` | `CreateColdTemplateRequest` -> immutable `ColdTemplateDto` version |
| GET `/cold-report-templates?majorId=&projectPeriodId=&page=&pageSize=` | Paged template versions in the staff's department |
| POST `/projects/{id}/cold/checklist` | `ApplyColdChecklistRequest` -> frozen project checklist |
| GET `/projects/{id}/cold/checklist` | `ColdChecklistDto`, filtered items/latest versions/capabilities/blockers |
| POST `/cold-checklist-items/{id}/draft` | `SaveColdDraftRequest` -> create next version or edit current draft |
| POST `/cold-submissions/{id}/files` | Multipart `file`, `concurrencyToken` -> updated submission |
| DELETE `/cold-submissions/{id}/files/{fileId}?concurrencyToken=` | Remove a draft's file |
| POST `/cold-submissions/{id}/submit` | `ColdTokenRequest` -> immutable SUBMITTED version |
| POST `/projects/{id}/cold/lock` | Latest package `ColdTokenRequest` -> LOCKED package/selected versions |
| GET `/cold-checklist-items/{id}/history?page=&pageSize=` | Paged immutable history, newest first |
| GET `/cold-submissions/{id}/files/{fileId}/download` | Authorized private file stream, no public URL |

Template and history pageSize is 1..100. A project checklist has at most 100
items; a template has 1..30 distinct report codes and at least one required item.
Checklist reads issue bounded set queries for the latest versions and files,
not a query for each item. Files are at most ten per version, 10 MiB each.

```json
{
  "majorId": 12,
  "projectPeriodId": 34,
  "name": "Marketing research reports",
  "reports": [
    { "code": "MARKET_RESEARCH", "name": "Market research", "required": true },
    { "code": "CAMPAIGN_PLAN", "name": "Campaign plan", "required": true }
  ]
}
```

These are illustrative IDs/reports, not seeded academic approval. IT's seven
examples are Project Introduction, Project Management, Software Requirement,
User Guide, Software Design, Testing and Implementation. Marketing and Design
staff configure their own templates for their major/period. Creating a new
template version never changes an already-applied checklist.

```json
{
  "projectPeriodId": 34,
  "deadline": "2026-12-10T16:59:59Z",
  "targets": [
    { "templateId": 56, "scope": "MAJOR_SPECIFIC", "studentId": null },
    { "templateId": 57, "scope": "INDIVIDUAL", "studentId": 78 }
  ]
}
```

Apply once while project ACTIVE/FINAL_SUBMISSION. Period must belong to the
project semester and be an active FINAL_SUBMISSION/EVALUATION period. Deadline
must be future, after its start and no later than its end. The applied package
freezes registration, template version, report definitions, target and deadline.
Targets are explicit; uploader identity is never used to invent a target.

New draft: `{ "expectedLatestVersion": 0, "note": "Initial report" }`.
Edit a draft: send its version and `concurrencyToken`. Resubmit after SUBMITTED:
send the latest version number with no old draft token; this creates a new empty
draft version. It does not replace the prior submitted files. New versions
require explicit upload, avoiding silent carry-forward of old evidence.

Upload accepts PDF/DOCX/ZIP with matching extension, MIME and server-validated
content using existing upload validation. ZIP expansion bounds and macro checks
remain enforced. Rejected uploads create no metadata. Draft file removal revokes
its URL; submitted/locked files cannot be removed through this workflow.

Every draft/file/submit mutation changes both submission and package tokens.
Before lock, GET the checklist again and pass its current package token. Lock
requires all required latest versions submitted with files; any latest draft,
including an optional report in progress, blocks lock. Storage size/checksum is
revalidated at submit and lock. Storage failure can still reject a capability
preview. Lock may occur after the student deadline; students cannot submit late.
The package becomes immutable and only its selected latest versions are LOCKED;
older SUBMITTED versions remain historical. Meeting/project lifecycle is unchanged.

## Authority and errors

| Actor | Read | Mutation |
|---|---|---|
| Frozen lead staff | Entire project checklist/history | Apply checklist and lock package |
| Participating staff | Own frozen department majors | Create immutable templates for own department; no project lock |
| Current Primary | Entire project checklist/history | No student upload or academic package lock |
| Active Mentor | Assigned major only | No student upload or academic package lock |
| Active verified team student | Own frozen major and own individual targets | Draft/upload/submit for those targets within server window |
| Unassigned lecturer / Admin alone | Denied | Denied |
| Evaluator | No generic COLD access in phase 5 | Phase 6 adds assignment-bound locked evidence; no edits |

Ended Mentor or departed student loses access. Generic project-file endpoints
cannot read COLD files, which have their own link/authorization path. File streams
use the snapshotted opaque key and metadata, never the live files row's latest
path. DTOs do not expose storage key/path or a permanent public URL.

400: malformed reports/targets, mismatched format or bounds. 401: no login.
403: foreign department/major/student or missing active assignment.
404: missing package/item/version/file. 409: stale version/token, duplicate apply,
closed deadline, immutable state, missing report/file or changed file checksum.
Multipart requests exceeding the 11 MiB transport limit may receive 413.
Reload after 409; do not retry submit/lock with the old token.

## Database, rollback and test evidence

Migration `20261011_add_cold_submissions.sql` adds five tables:
`cold_report_templates`, `cold_packages`, `cold_checklist_items`,
`cold_submissions`, `cold_submission_files`. It is additive/rerunnable with
QUOTED_IDENTIFIER ON and no shared business fixture or inferred backfill.
It reuses `files` and `IFileStorage`; there is no `cold_scores` table.

Package/project and item/version uniqueness, a filtered unique current-draft
index, target/state/size checks and FKs enforce consistency. Project locks
serialize concurrent draft/submit/lock. Mutation and audit commit together.
Upload uses the existing file transaction boundary: confirmed rollback cleans
the owned new object; uncertain commit retains content for reconciliation.
Removal deletes content only after successful commit. Storage is never made public.

`ColdSubmissionEndpointTests` and `ColdSubmissionScopeTests` use actual API
mutations and isolated SQL, with a deterministic in-memory storage adapter.
They verify version/lock/history/download, exact file metadata, concurrent writes,
stale tokens, deadline, audit rollback, invalid format/size/count, same-major
individual denial, interdisciplinary custom templates, department/major scopes,
ended mentor and departed student denial, and migration rerun with existing data.
These are not a claim of real external storage/provider or browser acceptance.

Local validation on 2026-10-11: Release build with warnings-as-errors passed;
1,053 unit and 1,185 integration tests passed with zero skips. Isolated fresh,
upgrade and rerun schema parity plus bootstrap/readiness/ownership guards passed.

Migration manifest, bootstrap, EF configuration, selected schema readiness and
committed OpenAPI are synchronized in this PR. Full-suite/CI/shared rollout status
is recorded in `v5-backend-implementation.md`. Tin should integrate this contract
behind the COLD UI flag; phase 6 is still required for grading.
