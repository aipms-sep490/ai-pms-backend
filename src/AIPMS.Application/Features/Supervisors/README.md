# Supervisors — BE-07, profiles and candidates

Profile/expertise APIs run against the existing schema. Project candidates use
the persisted BE-12 supervisor-selection quota. This does **not** complete issue
#12: requests, assignment, activation and workspace initialization remain follow-up work.

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
- Existing `max_active_projects` is preserved. This API does not configure quota,
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
  reads. The future accept flow must recheck eligibility and both caps within its
  concurrency-safe transaction, then create assignment/activate/init atomically.

`SupervisorCandidateEndpointTests` verifies permissions, project/period eligibility,
cross-semester capacity, null/zero limits, pending requests, paging and no writes.
`SupervisorCapacityTests`, candidate validator tests and handler tests cover the
rules and response mapping. No migration or generated-model edits are required.
