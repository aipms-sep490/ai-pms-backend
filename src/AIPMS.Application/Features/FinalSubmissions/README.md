# BE-16 final preparation, submission and locking

Issue #21; SRS Report 3 sections 3.16.1-3.16.4 and BR-140/141/142/143.
Result publication, completion and archive are still separate work.

## Required artifacts: agreed project-level policy

The user selected a checklist per project on 2026-09-12. Persisted department
staff in a project major's department (or active admin) configures 1-100 distinct
required BE-08 deliverable IDs from that project. Reports/evidence are uploaded
as versioned BE-08 deliverables; mutable ProgressReport/Meeting attachments are
not selected directly. No fixed document list, name/type matching or automatic
requirement for every deliverable is inferred. Academic staff configures the
actual required report/evidence list.

Configuration needs ACTIVE project, active academic scope and no locked package.
Students/supervisors cannot remove requirements. PUT replaces the whole set:
first creation uses null token; subsequent writes require the current GUID token
and rotate it. Missing configuration blocks submission; empty lists are invalid.
Restrictive foreign keys prevent deletion of configured deliverables. Changes
are audited and a stale configuration token returns 409.

## API

All routes require authentication, use persisted roles, return ProblemDetails
and disable caching. Dates are UTC. The prefix is /api/v1/projects/{projectId}.

| Method | Route after prefix | Behavior |
| --- | --- | --- |
| GET | /final-submission-periods | Team discovers semester final windows; page 1-1000000/pageSize 1-100; stable startAt/id descending |
| POST | /final-submission-draft | Current student leader creates one draft; 201, duplicate 409 |
| GET | /final-submission-draft | Team reads live draft selections and edit blockers |
| PUT | /final-submission-draft | Leader replaces notes, period and explicit version IDs using draft token |
| GET | /final-submission/requirements | Authorized package readers see required definitions and configuration token |
| PUT | /final-submission/requirements | Managed staff/admin configures required deliverable IDs |
| GET | /final-submission/checklist | Team reads exact unmet rules, completeness, deadline and both confirmation tokens |
| POST | /final-submission | Leader submits locked package; 201, repeated/conflicting request 409 |
| GET | /final-submission | Authorized reader sees immutable package metadata; absent package 404 |
| GET | /final-submission/files/{fileId}/download | Authorized reader downloads only a snapshotted file |

Draft POST (PUT also requires concurrencyToken):
```json
{"projectPeriodId":12,"notes":"Prepared for final submission","deliverableVersionIds":[101,203]}
```
Empty drafts are valid; null/duplicate/nonpositive IDs, over 100 selections or
notes over 10000 UTF-16 code units return 400. PUT removes omitted IDs. Select at
most one version per deliverable, belonging to this project, SUBMITTED/ACCEPTED,
with valid file metadata and exactly one file parent. Older eligible versions
are intentionally allowed; newer uploads never replace explicit selections.
After locking the draft returns isLocked=true and cannot be edited. Draft
metadata stays live; use official GET for the snapshot. Staff/admin/supervisor/
evaluator roles alone do not grant private draft access.

Requirements PUT:
```json
{"deliverableIds":[10,20],"concurrencyToken":null}
```
Submit using the tokens returned by checklist:
```json
{"draftConcurrencyToken":"<GUID>","requirementsConcurrencyToken":"<GUID>"}
```
canSubmit is true only for current student leader when all rules pass. Members
can read completeness but receive LEADER_REQUIRED. canEdit/canPrepareDraft are
preparation permissions, not submission readiness. Requirements definitions do
not imply completion; checklist resolves selectedVersionId/isComplete per ID.
Blockers include REQUIREMENTS_NOT_CONFIGURED, DRAFT_REQUIRED, EMPTY_PACKAGE,
REQUIRED_DELIVERABLE_MISSING:{id}, VERSION_INELIGIBLE:{id},
FILE_CONTENT_INVALID:{id}, ALREADY_SUBMITTED and period/state/authority codes.

## State, content and concurrency

Preparation/submission requires ACTIVE project and academic scope, ACTIVE
semester within inclusive dates, and exactly one ACTIVE FINAL_SUBMISSION period
in that semester covering startAt <= now < endAt. The draft must select that
period. No grace/late override or automatic rebinding. Explicit draft period
changes use the draft token. Team reads remain available after deadlines.

Checklist and submit stream actual file bytes to verify size and SHA-256. Every
selected artifact, including optional items, must pass. Missing/corrupt content
blocks submission. BE-08 uploads must have finished under its execution/deadline
rules. No upload/copy/provider change occurs here; keys reference BE-08's private,
create-only version content.

Serializable transactions and the BE-08 project XLOCK protect membership, roles,
versions/files, requirements and period configuration. Both draft and checklist
tokens must still match. Deadlock, expected uniqueness/FK and EF concurrency
conflicts return 409; reload before retrying.

One transaction inserts final_submissions/final_submission_items, snapshots
version/title/status/required flag and file metadata/key/hash, transitions
ACTIVE -> FINAL_SUBMISSION, audits actor/time/context and notifies active staff
in the project departments. Time is rechecked after storage reads and after
persistence/audit/notification. Failure rolls everything back; repeat/concurrent
POSTs cannot create a second package or notification.

Snapshot GET reads stored metadata, never live version/title/file records. No
normal update/delete/unlock/resubmit route exists. Draft/configuration writes
also check the locked row even if project status changes externally. BE-08
blocks mutations once the project leaves ACTIVE and never permits replacing
submitted version bytes. This is application immutability; SQL and physical
storage administrators remain privileged.

## Official access and BE-09

Readers: active student team members, managed department staff/admin, current
primary lecturer supervisor, active assigned lecturer in project/assignment
department. SUPERVISOR evaluator also needs current supervision. Each request
checks current account/role/scope/membership/assignment; revoked evaluators lose
access. Read remains possible on completed/archived projects while authorized.

Evaluators use the dedicated final-package download route; general BE-08 access
to all project files is not broadened. Storage keys never enter API responses or
audit snapshots. Unknown/unselected file IDs return 404.

BE-09 new assignments and creation/saving of draft grades now require a nonempty
locked package as well as state/window/rubric/scope rules. Legacy status-only
projects return 409; no fake packages/backfill. Existing grade history remains
readable under existing permissions. BE-09 per-evaluator finalization is described
in ../Evaluations/FINALIZATION.md; result publication and evaluator-assignment
notifications remain separate work.

## Deployment and tests

Apply rubric/evaluation/draft migrations, then
`db/changes/20260912_add_locked_final_submissions.sql` before deploying the API.
The additive rerunnable script creates four requirements/submission tables;
it does not change Generated/schema.sql, project states or historical grades.
No startup auto-migration. This task tests SQL on owned isolated databases only;
shared-server application belongs to the separate deployment step.

SQL-backed tests exercise actual BE-08 upload/download, older-version selection,
immutable metadata, authorization/revocation, missing/checksum-invalid files,
missing requirements, deadline/state changes, concurrent submits/edits, rollback
on audit/notification failure, migration rerun, and submit -> assignment -> draft
grading. Issue #21 remains partial until results/completion/archive are implemented.
