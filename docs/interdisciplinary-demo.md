# Interdisciplinary proposal demo

Implements the student-proposal path of SRS UC-032/033/040/041/044/049 and
BR-35/45/47/48/49/55/56/58. Both project modes are available for explicitly
configured teams. The existing academic registration window and BE-12 size
policy must be valid. Verified majors come from university-managed user profiles.

## Database and compatibility

Apply `db/changes/20260912_add_interdisciplinary_projects.sql` before deployment.
It adds four tables, does not edit existing data, and can be rerun. Existing
teams without scope retain single-major Foundation behavior. Historical proposals
are not assigned fabricated scope, review evidence or approvals. Configure an
existing FORMING/ELIGIBLE team while its proposal is DRAFT/REVISION_REQUIRED (or
it has no roster-locking proposal) to opt in. Do not apply this script implicitly
to a shared server merely to run tests; tests create separate owned databases.

## 1. Choose scope and create the team

Read `GET /api/v1/users/me/profile`, `/academic/hierarchy`, `/academic/semesters`
and `/academic/project-periods` (all latter routes also under `/api/v1`).
The FE may collect proposal text before team formation, but persistence of the
project draft still requires an eligible team and its current leader.

`POST /api/v1/teams`:

```json
{
  "academicSemesterId": 1,
  "code": "HYBRID01",
  "name": "Interdisciplinary capstone",
  "description": "Student proposal",
  "academicScope": {
    "projectMode": "INTERDISCIPLINARY",
    "primaryMajorId": null,
    "leadDepartmentId": 10,
    "requirements": [
      {"majorId": 100, "minMembers": 1, "maxMembers": 3, "responsibility": "Software implementation"},
      {"majorId": 200, "minMembers": 1, "maxMembers": 2, "responsibility": "Business analysis"}
    ]
  }
}
```

Replace example IDs with actual academic data. Requirements must use distinct,
active majors in the semester organization. Each requirement is mandatory with
1 <= minMembers <= maxMembers <= BE-12 MaxTeamSize. At least
max(2, BE-12 MinDistinctMajors) majors are needed for INTERDISCIPLINARY. Sum of
minimums must fit total capacity; sum of maximums must reach minimum team size.
LeadDepartment must be one of the departments owning those majors. All active
members must belong to a listed major. Extra unrelated majors are rejected.

For SINGLE_MAJOR use one requirement, set primaryMajorId to that major and
leadDepartmentId to its department. MinDistinctMajors is the interdisciplinary
minimum for explicit scopes; it does not turn single-major teams into hybrid teams.

The response includes academicScope and its GUID concurrencyToken.
`PUT /api/v1/teams/{teamId}/academic-scope` accepts the academicScope object,
including its current concurrencyToken. First configuration of a legacy team
uses null/omitted token. Only the current student leader may change scope.
Changes may leave minimum quotas incomplete but cannot exclude current members
or place the existing roster above a maximum quota. Remove members first if needed.

## 2. Invite, accept and check eligibility

Use the existing team candidate/invitation/accept APIs. Candidate search includes
all configured majors with capacity and excludes already-full major quotas.
Invite and accept recheck active profiles, total capacity and per-major limits
inside the roster transaction. Pending invitations do not reserve a seat.

`GET /api/v1/teams/{teamId}` and `POST .../{teamId}/eligibility/refresh` return
eligibility.canRegister, rosterLocked and reasons. Reasons include:

| Code | Meaning |
| --- | --- |
| TOO_FEW_MEMBERS / TOO_MANY_MEMBERS | BE-12 overall size limit |
| MAJOR_MIN_MEMBERS:{majorId} | A mandatory major still lacks members |
| MAJOR_MAX_MEMBERS:{majorId} | A major exceeds its maximum |
| MEMBER_MAJOR_NOT_ALLOWED | An active member is outside the configured requirements |
| INELIGIBLE_MEMBER | Inactive/inconsistent profile or wrong organization |
| INTERDISCIPLINARY_MAJORS_REQUIRED | Not enough distinct requirements, or invalid primary major for this mode |
| MAJOR_QUOTAS_INFEASIBLE | Per-major quotas cannot fit the overall size policy |

Render these as localized messages and look up major names in academic hierarchy.
PASS is advisory until submission; changing roster/scope invalidates previous
eligibility. Submission rechecks authoritative data under the same transaction.

## 3. Draft, submit and snapshot

Use existing `POST /api/v1/projects`, `PUT /api/v1/projects/{id}` and
`POST /api/v1/projects/{id}/submit`. RequiredMajorIds must exactly match the team's
scope. Scope and proposal are separate edits: if requirements change, update
RequiredMajorIds too before submission. Scope edits invalidate the editable
project's rowversion; reload the project before editing/submitting it.

Submission requires title, problem statement, objectives, expected output and
domain/technology/keyword tags. It stores the current policy fingerprint and size
limits, UTC window, organization, scope, department IDs and verified roster as an
immutable registration snapshot. For INTERDISCIPLINARY, every participating
department (including the lead) starts PENDING. Same-department different majors
produce one department decision. Submission locks roster/scope through the existing
project-state rules. A failed snapshot/status/history/audit write rolls back the
whole operation.

`GET /api/v1/projects/{id}/academic-review` returns current project concurrencyToken,
academicScope and latestSubmission with snapshot ID, evidence and decisions.
Only authorized team/supervisor/staff/admin readers can access it.

## 4. Department review

The lead department starts review with `POST /api/v1/projects/{id}/start-review`.
Project state must be UNDER_REVIEW for department decisions.

Each department staff account calls `POST /api/v1/projects/{id}/department-decisions`:

```json
{
  "snapshotId": 12,
  "concurrencyToken": "current project rowversion in base64",
  "decision": "APPROVED",
  "reason": "Reviewed feasibility and discipline responsibilities"
}
```

Only APPROVED/REJECTED are accepted; REJECTED requires a reason. The department is
derived from the actor's active persisted staff role and academic scope. Clients
cannot select another department, and an ADMIN role alone cannot provide an
academic decision. A department decides once per submitted version. To change a
decision, the lead requests revision and the team submits a new version.

Use the returned project concurrencyToken for the next action. Concurrent decisions
using the same token yield one success and a 409 requiring a reload/retry. The lead
may call the existing approve endpoint only after every department has approved.
Missing/rejected decisions block approval. The lead may request revision or reject
without waiting for all departments. For explicit SINGLE_MAJOR scope, only the
lead department reviews and no separate department-decisions step is required.

Revision follows REVISION_REQUIRED -> edit -> resubmit -> SUBMITTED -> start-review.
Every resubmission stores new evidence and resets required decisions to PENDING;
older snapshots and decisions remain stored. An old snapshot ID or token returns
409. Final approval yields APPROVED, ready for existing supervisor candidate and
request APIs. A department rejection alone records its decision; the lead chooses
revision or final rejection. It does not silently change the proposal state.

## Access and remaining boundaries

Project listing now filters on the current persisted account's team membership,
unended supervisor assignment, staff department or administrator role before
pagination. Client-supplied team/major filters cannot broaden that scope.

This implements student-proposed single/interdisciplinary registration and review.
Published topic catalogues/ProposalSource selection, per-period allowed-mode/source
switches, discipline mentors and per-student grading are separate work. It does
not claim the entire long-term Hybrid SRS is finished. One primary supervisor and
the existing capacity rules still govern the next demo stage.
