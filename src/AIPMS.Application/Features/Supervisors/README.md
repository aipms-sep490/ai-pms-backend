# Supervisors — BE-07, profiles, candidates, requests and assignments

Profile/expertise APIs run against the existing schema. Project candidates use
the persisted BE-12 supervisor-selection quota. Assignment read/end APIs complete
the request lifecycle. Workspace uses the existing project/team and milestone/task
APIs; template-based milestone initialization is deferred until a template module
and schema exist (BE-12 currently rejects template configuration).

## API contract

All routes require authentication. IDs in the directory/detail/expertise routes
are **supervisor profile IDs**; provisioning explicitly uses a **user ID**.

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/v1/supervisors` | Directory, filters: `departmentId`, `search` (name), `expertise`, `isAvailable`; `page=1`, `pageSize=20` (max 100). Stable name/ID ordering. |
| GET | `/api/v1/supervisors/{profileId}` | Public academic profile and expertise; no private account/email/phone fields. |
| GET | `/api/v1/projects/{projectId}/supervisor-candidates` | Eligible lecturers with expertise, workload and remaining capacity; `search`, `expertise`, `page=1`, `pageSize=20` (max 100). |
| PUT | `/api/v1/supervisors/users/{userId}/profile` | Upsert the lecturer's unique profile. Body: `{ "bio": "...", "isAvailable": true }`; returns 200 with profile ID. Null/blank bio clears it. |
| PUT | `/api/v1/supervisors/{profileId}/expertise` | Replace the complete list. Body: `{ "expertise": [{ "name": "Software Engineering", "proficiencyLevel": "Advanced" }] }`. Empty list clears expertise; null list is invalid. |
| POST | `/api/v1/projects/{projectId}/supervisor-requests` | Current student team leader sends `{ "supervisorProfileId": 1, "message": "..." }`. Returns request DTO (200). |
| GET | `/api/v1/projects/{projectId}/supervisor-requests` | Project readers list requests; optional `status`, `page`, `pageSize`. |
| GET | `/api/v1/supervisors/requests` | Lecturer's own inbox; optional `status`, `page`, `pageSize`. |
| POST | `/api/v1/supervisor-requests/{requestId}/cancel` | Current student team leader cancels a pending request; no body. |
| POST | `/api/v1/supervisor-requests/{requestId}/accept` | Requested lecturer accepts with `{ "message": "..." }`; returns request DTO including assignment ID. |
| POST | `/api/v1/supervisor-requests/{requestId}/reject` | Requested lecturer rejects with `{ "message": "..." }`. |
| GET | `/api/v1/projects/{projectId}/supervisor-assignments` | Project readers list assignments; optional `status=ACTIVE\|ENDED`, `page`, `pageSize`. Omit status for all records. |
| GET | `/api/v1/supervisors/assignments` | Lecturer's own current and ended assignments, with the same filters and pagination. |
| GET | `/api/v1/supervisor-assignments/{assignmentId}` | Assignment detail for project readers or its lecturer, including after ending. |
| POST | `/api/v1/supervisor-assignments/{assignmentId}/end` | End an assignment on a completed/archived project with `{ "reason": "Guidance completed" }`. Returns assignment DTO (200). |

`proficiencyLevel` is optional descriptive text, not a scored/ranked qualification.
Names are trimmed and duplicate names ignoring case/outer whitespace are rejected.
The database unique index remains the final guard for collation-equivalent names.

## Access and persistence

- Authenticated active accounts can read the directory. Only active `LECTURER`
  accounts in an active department/organization are displayed. Availability is
  a directory filter, **not** a guarantee of capacity or assignment eligibility.
- A lecturer can edit their own profile/expertise. Admin can edit any eligible
  lecturer. Department Staff can edit only lecturers in their own active department.
  Role/scope checks use current database account data, not request-supplied scope.
- Mutations run in a serializable transaction covering access checks, data and
  before/after audit. Concurrent conflicting edits can return 409 and require a
  reload/retry. Profile and expertise are separate updates; successful later full
  replacements of the same fields take precedence.
- Existing `max_active_projects` is preserved. Profile/expertise APIs do not configure quota,
  create assignments or change project status. No schema/generated model edits.
- ProblemDetails: 400 invalid input, 401 anonymous, 403 unauthorized/inactive actor,
  404 absent/non-directory profile, 409 invalid lecturer or conflicting write.

## Verification

`SupervisorProfileTests` covers access rules, validation and handler audit context.
`Supervisors/SupervisorEndpointTests` uses an owned, isolated SQL database for HTTP,
persistence, scope, concurrency and audit rollback tests. It does not use shared
project databases or legacy unmerged implementations.

## Candidate and capacity contract

- Only active accounts with project read access can inspect candidates. Non-admin
  readers also need an active department/organization. This grants no permission
  to send, cancel or accept requests. Roles and scope come from persisted data.
- The project must be `APPROVED`, have no unended assignment, and belong to an
  active semester whose dates include today (UTC). Candidate departments come
  from active project majors in that semester's organization; no matching active
  major is a 409 configuration conflict.
- Exactly one `ACTIVE` `SUPERVISOR_SELECTION` period must cover now in that semester
  (`start_at <= now < end_at`), with a positive `max_projects_per_supervisor`.
  Missing, overlapping or unconfigured periods return 409, with no guessed default.
- Candidates are active lecturers in those departments, with active academic scope,
  available profiles and no pending request for the same project/profile pair.
  Expertise is free text: the optional filter matches it; it is not automatically
  inferred from a major and is not an AI score.
- Both capacity constraints apply: `max_active_projects` limits unended assignments
  across all semesters; the selection period's quota limits unended assignments in
  the target semester. Count distinct projects, regardless of primary/secondary role.
  Assignments on completed/archived projects still occupy capacity until `ended_at`
  is recorded. Pending requests do not reserve slots.
- A null profile limit adds no global cap; zero permits no new assignments.
  `remainingSlots` is the smaller remaining allowance, clamped to zero. Profiles
  with zero remaining slots are filtered before pagination and total count.
- Results return `activeProjects`, `semesterActiveProjects`, `profileLimit`,
  `semesterLimit`, `remainingSlots` and `selectionPeriodId`, plus public profile
  fields and expertise. Ordering is name then profile ID. There is no AI dependency.
- This is a read-only snapshot, not a reservation. Counts/pages can change between
  reads. Accept rechecks eligibility and both caps inside its transaction.

`SupervisorCandidateEndpointTests` verifies permissions, project/period eligibility,
cross-semester capacity, null/zero limits, pending requests, paging and no writes.
`SupervisorCapacityTests`, candidate validator tests and handler tests cover the
rules and response mapping. No migration or generated-model edits are required.

## Request decisions and concurrency

- Sending requires current active student membership with `is_leader = true`.
  Project read access and admin/staff roles alone do not grant send/cancel rights.
  The current leader may cancel even when they were not the original sender.
  Only the profile's current active lecturer can accept/reject; role claims do not
  override persisted roles or ownership.
- Send checks the same project, academic scope, selection period and dual capacity
  rules as candidates, plus the database-backed duplicate-pending guard. A pending
  request does not reserve capacity or change project status, so an approved project
  can approach more than one eligible supervisor.
- Accept rechecks current eligibility, both capacity caps and the selection window
  after acquiring capacity/project locks. It creates one primary assignment, records
  `APPROVED -> SUPERVISOR_PENDING -> ACTIVE` history, accepts the request and cancels
  the project's other pending requests in one serializable transaction. An existing
  `SUPERVISOR_PENDING` project may also finish this flow. The project/profile/request
  IDs are taken from the stored request, never supplied separately by the responder.
- Existing project/team data forms the execution workspace; activating the project
  enables the existing milestone/task APIs. This slice does not invent default
  milestones or instantiate a template. Template-based initialization is deferred
  by the agreed scope; it requires its own module/schema review.
- All decisions record actor/time and before/after audit data in the same transaction.
  Assignment and project audit events reference their respective entities. Automatic
  cancellation records the winning request ID. Any failure, including the final audit
  write, rolls back request, assignment, project, history and previous audit writes.
- Request row update locks serialize decisions. Supervisor row update locks protect
  global capacity across projects/semesters, and project locks protect the single
  assignment across different supervisors. Serializable reads also protect policy,
  account and workload checks. Deadlocks, lock conflicts and unique/concurrency
  conflicts return 409 ProblemDetails with trace ID; the caller can reload/retry.
- Repeating the same accept/reject/cancel returns the original decision without
  another write or audit. Accepted replays require a matching assignment and never
  reopen completed/archived projects or ended assignments. A different final decision
  is 409; cancellation/rejection can clear pending requests after the period closes.
- Request/response messages allow null or up to 2000 characters and are trimmed.
  List statuses are `PENDING`, `ACCEPTED`, `REJECTED`, `CANCELLED`; page is 1..1000000,
  pageSize is 1..100. Lists sort by requested time descending then ID descending.

`SupervisorRequestEndpointTests` covers the request lifecycle, persisted permissions,
stale eligibility, concurrent duplicate sends/accepts/cancel, idempotent replay and
rollback. Handler and validator unit tests cover decision replays and input contracts.

## Assignment lifecycle and workspace

- Assignment IDs, profile IDs, user IDs and request IDs are distinct fields in the
  response. It includes the supervisor's public name, primary flag, assigned time
  and ended time, without account contact/security data.
- Project assignment lists require existing project read access. The lecturer's
  own list and detail retain their historical assignment records after ending,
  without restoring access to the project's workspace. Lists sort by assigned time
  descending then ID descending, with page 1..1000000 and pageSize 1..100.
  `ACTIVE` means `ended_at IS NULL`, independent of the project's lifecycle status.
- Ending requires an active persisted admin, the assigned active lecturer in an
  active academic scope, or active Department Staff whose department has an active
  project major. Student leaders and other project readers cannot end assignments.
  Token role claims and profile availability do not grant this permission.
- Only `COMPLETED` or `ARCHIVED` projects can end an unended assignment. There is
  no supervisor replacement transition in the current project state machine;
  ending assignments on unfinished projects returns 409. Ending does not change
  the project status, accepted request, primary flag or existing workspace data.
- The nonblank reason (max 2000 characters, trimmed) is stored in audit with actor,
  time and before/after assignment data. The server writes `ended_at`/`updated_at`
  atomically with `SUPERVISOR_ASSIGNMENT_ENDED`; audit failure rolls back the end.
  Both capacity limits then stop counting this assignment.
- End locks the assignment and shares the request workflow's supervisor/project
  locks. Concurrent end/accept conflicts return 409 and may be retried. Repeating
  an end first rechecks permission, then returns the original end time without
  changing its reason or producing another audit. End never deletes/reopens data.
- Acceptance already atomically grants supervisor project access through the
  assignment and activates the existing team/project workspace. The workspace
  starts with no generated milestones; its existing milestone/task APIs are usable
  immediately after acceptance. An accept replay preserves all user-created work.

`SupervisorAssignmentEndpointTests` verifies workspace usability, request-to-assignment
identity, end permissions, historical access, both capacity releases, concurrent
end/accept, idempotency and audit rollback against an isolated SQL database.
Handler/validator unit tests cover rules and input bounds. No schema or generated
model changes, SQL rollout, or template defaults are required by this slice.
