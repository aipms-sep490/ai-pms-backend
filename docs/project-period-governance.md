# Project period governance and supervisor assignments

## Period policy

The existing Project Period create/update endpoints accept `allowedProjectModes`
(`SINGLE_MAJOR`, `INTERDISCIPLINARY`) and `allowedProposalSources`
(`PUBLISHED_TOPIC`, `STUDENT_PROPOSAL`) as non-empty comma-separated sets.
Omitting either field preserves its existing value; new and legacy periods default
to both values. Values are normalized and unknown values return 400. ADMIN retains
period configuration authority; that role does not grant academic review authority.

Responses include `policyVersion`. Changes to mode/source sets, registration dates,
period type, team quotas, supervisor capacity or period status advance this revision.
Equivalent sets in a different order do not advance it. Updates are transactional,
closed periods/semesters reject changes, and create/update audits include policy fields.

Policy is checked when creating/configuring a team, creating/editing/publishing a
topic, creating/editing a proposal, selecting a topic, and submitting/resubmitting.
Existing submitted and ACTIVE projects are not automatically reclassified or rejected
when the period policy changes. A new submission uses current policy and stores an
immutable copy of the version, allowed sets, proposal source, academic scope, and
major-to-department mapping in registration evidence.

A period allowing only published topics can create a project through the existing
`POST /api/v1/projects` payload plus optional `topicId`. Draft creation, topic selection,
permission checks and audit share one transaction; an invalid topic leaves no draft.
Omit `topicId` for the existing student-proposal flow.

## Department decisions

`GET /api/v1/projects/{id}/academic-review` retains `latestSubmission` and adds
`submissionHistory` (newest round first). Each round includes the immutable evidence
and department decisions. Existing decision endpoints continue to require both the
current snapshot ID and project concurrency token; a revision starts a new round.

Only active persisted DEPARTMENT_STAFF in the responsible department can perform
academic review. For configured projects the lead department controls final state
transitions and participating departments record only their own decision. ADMIN-only
accounts cannot replace this authority. Scoped project access uses frozen submission
departments for submitted/ACTIVE work; draft/revision access follows editable scope.
New submissions of legacy single-department teams also capture policy evidence;
legacy multi-department teams must configure a lead department before submission.
Existing historical records are not backfilled with invented approvals or actors.

## Candidates and requests

- `GET /api/v1/projects/{projectId}/supervisor-candidates?assignmentType=PRIMARY`
- `GET /api/v1/projects/{projectId}/supervisor-candidates?assignmentType=DISCIPLINE_MENTOR&majorId=123`
- `POST /api/v1/projects/{projectId}/supervisor-requests`

Request payload:

```json
{
  "supervisorProfileId": 42,
  "assignmentType": "DISCIPLINE_MENTOR",
  "majorId": 123,
  "message": "Please advise the software work"
}
```

`assignmentType` defaults to `PRIMARY` for old clients. PRIMARY forbids `majorId`;
DISCIPLINE_MENTOR requires a positive, project-required major. The current student
team leader sends/cancels requests; only the requested lecturer accepts/rejects.
A mentor must belong to the major's department and have an expertise name matching
its current code or name (case-insensitive). This explicit match uses the existing
free-text expertise catalogue; no inferred AI expertise mapping is used.

Primary selection keeps the existing APPROVED/SUPERVISOR_PENDING flow and open
SUPERVISOR_SELECTION window. Mentor selection requires ACTIVE plus an active primary.
During execution, mentor and replacement capacity use the latest started ACTIVE or CLOSED
SUPERVISOR_SELECTION period in the same semester, including a CLOSED
selection period. The semester must still be active and in date.

There is at most one active primary and one active mentor per required major.
Mentors are optional. Capacity counts distinct unended projects, so a lecturer holding
multiple roles on the same project consumes one project slot. Candidates report actual
workload; a lecturer already on this project can take an additional role without a new
project slot. Scope, account status, expertise, capacity and occupancy are rechecked
inside the request transaction. Profile/project locks and SQL unique indexes arbitrate
races; conflicts return 409. Accepting a request cancels only competing requests for
the same assignment type and major. Mentor acceptance never activates the project or
initializes milestones again. Replaying the same final decision returns its result.

## Replacement and history

`POST /api/v1/supervisor-assignments/{assignmentId}/replace`:

```json
{
  "supervisorProfileId": 57,
  "reason": "Current lecturer is no longer available"
}
```

Reason is required (trimmed, at most 2,000 characters). The lead department replaces
a primary; the frozen major's department replaces its discipline mentor. ADMIN alone,
students and lecturers cannot replace assignments. The project must be ACTIVE and
the replacement lecturer must pass scope, availability, expertise and capacity checks.

The transaction ends the old assignment and creates a new assignment of the same
type/major, preserving the project's ACTIVE state and existing milestones. The
associated accepted request explicitly records a department-authorized replacement,
not a lecturer response. An audit and notifications to the team and old/new lecturers
commit with the assignment. An audit/notification failure rolls everything back.
Repeating the same old assignment, replacement profile and trimmed reason returns
the original replacement; a conflicting retry returns 409.

Existing assignment GET/list endpoints return `assignmentType`, `majorId`, `status`,
`assignedBy`, `endedBy`, `endReason`, and `replacesAssignmentId`, alongside timestamps.
They include ended assignments unless filtered by `status=ACTIVE`. Historical actors
remain null when unknown. The former supervisor may read their own assignment record
but loses project workspace access unless another active assignment grants it.
Mentor membership does not grant primary-only grading or leader-change approval.

## Migration and validation

Apply in order before deploying the API:

1. `db/changes/20260925_add_project_period_governance_policy.sql`
2. `db/changes/20260925_add_supervisor_assignment_types.sql`

Both are rerunnable SQL scripts; bootstrap `db/schema.sql` and integration bootstrap
include the new schema. The assignment migration replaces the old lifetime
project/supervisor uniqueness with active-slot constraints and preserves historical
rows/primary flags. It does not fabricate historical major/actor values.

SQL tests create and dispose only unique `AI_PMS_TEST_*` databases from
`AIPMS_TEST_SQL_CONNECTION`; they never migrate the supplied shared database catalog.
Tests cover migration rerun from legacy tables, independent mentor slots, concurrent
acceptance, scoped replacement/history, audit rollback, permissions, capacity, period
policy, submission evidence, department decisions and Swagger contracts.

Issue #74 owns eligibility snapshot checks/stale/lock and the final submit guard.
Its fingerprint should include the persisted `ProjectPeriod.policyVersion`; the
existing team policy provider exposes a governance revision suffix after version 1.
Merge and revalidate #74 before this change (#75); do not claim the new eligibility
snapshot endpoints are part of this PR. No frontend changes are included.
