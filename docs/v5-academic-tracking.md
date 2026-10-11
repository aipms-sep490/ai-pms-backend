# v5 phase 8: evidence review, checkpoints and defense scheduling

BE-12/13/14. Backend contract for Tin. These are tracking/scheduling records, not
academic approval, gate enforcement or a new grading pipeline. No frontend edits.
Swagger now explicitly declares success response schemas for these routes and
the earlier COLD/weekly routes; controller-wide error declarations had suppressed
their inferred success schemas. Runtime payloads of those earlier APIs are unchanged.

## Endpoints

All routes below have `/api/v1` prefix, require authentication, and disable caching.

| Route | Purpose |
|---|---|
| GET project-evidence/{id}/review-target?sourceVersionId= | Current source hash, latest review ID, canReview/reason |
| POST project-evidence/{id}/reviews | Append decision for exact source version |
| GET project-evidence/{id}/reviews?page=&pageSize= | Scoped append-only decision history |
| POST projects/{id}/checkpoints | Plan scoped checkpoint |
| PUT projects/{id}/checkpoints/{checkpointId} | Edit undecided plan using token |
| GET projects/{id}/checkpoints?page=&pageSize= | Scoped plans, risk signal and decision history |
| POST checkpoints/{id}/decisions | Append conclusion and references to reviewed evidence |
| POST projects/{id}/defense-committees | Create immutable committee and major memberships |
| GET projects/{id}/defense-committees?page=&pageSize= | Lead/Primary committee list |
| POST projects/{id}/defense-sessions | Schedule one defense with explicit committee and room |
| GET projects/{id}/defense-sessions?page=&pageSize= | Project-scoped schedule history |
| GET defense-sessions/{id} | Scoped schedule; assigned committee lecturers may read just this record |
| POST defense-sessions/{id}/cancel | Cancel a future scheduled defense with token and reason |

Page is 1..100000; pageSize is 1..100. Count and paging follow authorization filters.
History is stable by ID (checkpoint/session lists use planned/start time then ID).

## Authority and lifecycle

All access requires an active account and a valid, active frozen registration
scope. Missing/invalid provenance fails closed. Writes require ACTIVE or
FINAL_SUBMISSION; COMPLETED/ARCHIVED remain read-only for authorized readers.

| Actor | Evidence review | Checkpoint | Defense |
|---|---|---|---|
| Current Primary | Read and review whole project | Manage all project/major plans | Read committees/schedule |
| Current discipline Mentor | Read/review explicitly targeted assigned major | Manage/read assigned major only | Read project schedule metadata |
| Frozen lead department staff | Read reviews, cannot act as reviewer | Manage project and major plans | Create committee, schedule/cancel |
| Participating department staff | Read reviews for own frozen major mapping | Read own major | Read project schedule metadata |
| Active student in frozen roster | No review-history API access | Read common/own-major plans | Read schedule metadata |
| Committee lecturer without other assignment | No project evidence authority | None | Read exact assigned session only |
| Admin alone / unrelated lecturer / ended Mentor | None | None | None |

Committee membership never creates an EvaluationAssignment or permits scoring.
The existing D17 primary/evaluator exclusion remains. A Primary may participate
in scheduling as a committee member; that does not make them an evaluator.

## Evidence version and decision

Reuse project_evidence; no alternate file ledger or upload model. Reviews support
TASK, DELIVERABLE, FILE, MEETING and PROGRESS_REPORT. For DELIVERABLE, select an
explicit deliverable version using sourceVersionId (mandatory for this source).
Other source types reject that parameter. A source hash captures source content,
target, concurrency marker and up to 100 exact file metadata/storage references.
Storage references and source snapshot contents are never exposed in these DTOs.
Existing authorized download workflows remain the only way to download a file;
a review record does not grant a new download capability.

1. Read review-target and obtain sourceVersion/latestReviewId.
2. Send both values with the decision; the server checks them under a project
   transaction and inserts reviewer, assignment, decision, reason, timestamp and
   the private source snapshot atomically with audit.
3. A changed source/file or newer review returns 409. Reload before deciding.
   Retry with the same old values cannot append a duplicate review.

```json
{
  "sourceVersionId": 123,
  "expectedSourceVersion": "hash-returned-by-review-target",
  "expectedLatestReviewId": 0,
  "decision": "CHANGES_REQUESTED",
  "reason": "Add the missing validation evidence."
}
```

Decision is ACCEPTED, CHANGES_REQUESTED or REJECTED; reason is required, <=2000
characters. IDs/hash in examples are illustrative. Historical reviews never
change when a source is edited or a newer deliverable version is submitted.
The legacy ledger verificationStatus remains PENDING/UNKNOWN; it is not silently
converted into a new official approval state.

Mentor scope requires an explicit matching task discipline or deliverable target.
Meeting/report sources lack authoritative major targets, so major-labelled
meeting/report evidence fails closed; review those sources as project-wide
evidence. Do not infer major/student ownership from uploader or current profile.

## Tracking-only checkpoints

Create with title (<=200), nullable majorId, and plannedAt including timezone.
DTO returns UTC plannedAt, token, canManage, isOverdue, decisions[], trackingOnly
true and blocksAcademicWorkflow false. Overdue means planned time passed and the
latest conclusion is neither MET nor CANCELLED. NOT_MET/AT_RISK is a risk signal;
it never fails, completes or blocks the project.

An undecided plan can be edited using concurrencyToken; its target is immutable.
After a decision, plan fields are immutable. Add a conclusion with a reason or
create a successor checkpoint if the plan itself must change. Conclusions are
append-only, at most 100 per checkpoint; an evidence list has at most 50 unique
ledger references, each paired with an exact review ID in the same project and
permitted major. A conclusion can cite historical reviewed evidence explicitly;
the server does not represent that evidence as the latest source version.

```json
{
  "concurrencyToken": "current-token",
  "decision": "AT_RISK",
  "reason": "Validation evidence remains incomplete.",
  "evidence": [{ "evidenceId": 12, "reviewId": 34 }]
}
```

Conclusion is MET, AT_RISK, NOT_MET or CANCELLED. Audit and rollback cover both the
new conclusion and token update. Lists batch relations rather than querying each
displayed record separately; decisions have a fixed per-checkpoint limit.

## Minimal defense schedule

Committee input: name (<=200), members[{userId,majorId}] (1..20 unique pairs).
Every frozen major must be covered. Each member must be an active lecturer with
an available supervisor profile in the frozen responsible department and exact
major code/name expertise (case-insensitive; no similar-major fallback). One
qualified lecturer can cover multiple majors. The minimum committee size is not
an academic policy decision; configure a broader approved committee when needed.
These immutable memberships preserve the planned roster. Qualification is
revalidated at scheduling time, so a stale preview does not confer authority.

Schedule input: committeeId, startAt, endAt (timezone supplied, whole seconds),
room. Time must be future, positive and at most 8 hours. Room is a textual identity
(trimmed and uppercase, <=100), not a new campus room catalog. Use a consistent
room identifier. Server serializes defense bookings with a transaction-owned
SQL application lock and checks half-open overlaps [start,end) for room, lecturer
and project. Same project also has a filtered unique one-SCHEDULED constraint.
An adjacent session is allowed; concurrent conflicting requests cannot both win.

Conflict checking in this phase covers defense-session bookings. Meetings remain
their separate scheduling workflow; this does not claim a global reservation
system for every calendar item. No appeals, defense attempts or automatic
evaluator assignment are implemented.

Cancellation requires current token and reason. A started session cannot be
cancelled to create another attempt. For a future schedule correction, explicitly
cancel then schedule again; previous session and audit are preserved. Students
and committee lecturers receive a generic inbox notification and an entry in the
existing email queue. The queue commits with the schedule and audit; delivery
workers run after commit. Creating the schedule does not mean email was sent.
Students and participating departments get schedule metadata, not committee contact data
or other academic artifacts. An assigned lecturer can open the exact session even
without project authority; the project list and evidence remain denied.

## Errors, schema and validation

401 unauthenticated; 403 account/assignment/frozen-scope denial; 404 missing or
wrong-project nested record/version; 400 invalid payload/bounds; 409 stale token,
source or review, immutable/closed state, unknown provenance, missing qualification
or schedule conflict. Qualification/conflict errors include:
DEFENSE_MAJOR_COVERAGE_REQUIRED, DEFENSE_MEMBER_NOT_QUALIFIED,
DEFENSE_ALREADY_SCHEDULED, DEFENSE_SCHEDULE_CONFLICT, DEFENSE_SCHEDULE_BUSY.
Examples: old review hash -> 409; Mentor reviews a common target -> 403; unknown
version under a deliverable -> 404; another project's review in a checkpoint ->403.

`20261012_add_academic_tracking.sql` adds six tables: project_evidence_reviews,
project_checkpoints, project_checkpoint_decisions, defense_committees,
defense_committee_members, defense_sessions. Additive/rerunnable, QUOTED_IDENTIFIER
ON, no backfill. Bootstrap/manifest/EF/readiness match. All lifecycle testing runs
on owned isolated databases; shared AI_PMS receives only verified additive schema
after green CI and merge.

`AcademicTrackingEndpointTests` exercises real endpoints: stale source/file,
exact deliverable version, role/ended/archived/foreign scope denial, audit rollback,
concurrent review/decision/booking, immutable checkpoint plan, same-room and shared
lecturer conflicts, interdisciplinary coverage/expertise, schedule-only committee
access and migration rerun with data. Existing schema parity compares a fresh
database with an upgraded baseline. Final test counts, PR and shared rollout
evidence are recorded in `v5-backend-implementation.md`. FE/browser acceptance is
separate, and P0/P1 is still IN_PROGRESS.
