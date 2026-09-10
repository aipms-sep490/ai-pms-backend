# Supervisors — BE-07, profile/expertise slice

This slice runs against the existing `develop` schema and does not depend on BE-12.
It does **not** complete issue #12: candidates/capacity, requests, assignment,
activation and workspace initialization remain follow-up work.

## API contract

All routes require authentication. IDs in the directory/detail/expertise routes
are **supervisor profile IDs**; provisioning explicitly uses a **user ID**.

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/v1/supervisors` | Directory, filters: `departmentId`, `search` (name), `expertise`, `isAvailable`; `page=1`, `pageSize=20` (max 100). Stable name/ID ordering. |
| GET | `/api/v1/supervisors/{profileId}` | Public academic profile and expertise; no private account/email/phone fields. |
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

Before the assignment slice, agree the BE-12 policy contract and capacity source,
then implement send/cancel/accept/reject/end with transaction, concurrency and
project lifecycle tests. Do not mark the whole issue Done based on this slice.
