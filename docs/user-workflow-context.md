# Current user and workflow actions

This read-only API supports the registration demo: sign in, choose a semester,
form a single-major/interdisciplinary team, submit a proposal, review it by
department, and start supervisor selection. It does not enumerate actions for
every workspace, evaluation, result, or archive screen.

SRS references: UC-006 (View Profile), section 3.3.8 (Enforce Backend
Authorization Policy), BR-02 (frontend visibility is UX only), and BR-06
(academic roles do not replace resource membership/assignment checks).

## Routes

All routes require a bearer token and return `Cache-Control: no-store`.

| GET route | Response schema | Purpose |
| --- | --- | --- |
| `/api/v1/auth/me/context?academicSemesterId=123` | `UserWorkflowContextDto` | Persisted identity, academic context, semester selection, own team, navigation actions |
| `/api/v1/teams/{teamId}/actions` | `TeamWorkflowActionsDto` | Current student's team actions and registration eligibility |
| `/api/v1/projects/{projectId}/actions` | `ProjectWorkflowActionsDto` | Scoped proposal/review/supervisor-selection actions |

Existing login, `/auth/me`, and profile contracts are unchanged. No schema
migration or shared SQL script is needed. The new routes require deployment of
this API build before FE can call them on a hosted environment.

## Identity and academic context

`user` contains the database identity, status, student/employee codes, persisted
`roles`, distinct `grantedPermissions`, `effectiveRoles`, and
`requiresTokenRefresh`. Password hashes, security stamps and tokens are omitted.

`effectiveRoles` is the intersection of persisted roles and token roles. A newly
granted role cannot unlock actions with an old token; a revoked role in an old
token cannot unlock actions either. When `requiresTokenRefresh` is true, refresh
the session through the existing authentication flow and fetch context again.
This GET does not issue or revoke tokens.

`grantedPermissions` describes the role-permission catalogue. Existing write
endpoints do not universally enforce that catalogue: many use role policies and
resource-specific checks. FE must use `actions` for supported buttons and must
not treat a permission code as blanket project access. This change does not
claim to implement granular permission enforcement across all modules.

`academic` contains organization, department and major references (ID/code/name/
active flag), plus eligibility flags and `issues`. Missing/inactive academic
data does not prevent an active user from reading their own identity.

`currentSemesters` contains active, in-date semesters in the user's organization
(all organizations for an effective admin). Exactly one is auto-selected;
otherwise `selectedSemester` is null and `semesterSelectionIssues` explains why.
Pass `academicSemesterId` to choose explicitly. A foreign semester is hidden
with 404, except for effective admins. Historical selection is readable but is
marked `SEMESTER_NOT_CURRENT` and cannot open registration actions.

`periods` belongs to the selected semester. `isOpen` requires an active current
semester, active period and `startAtUtc <= asOfUtc < endAtUtc`. Multiple open
registration periods still block team creation because the write API requires
an unambiguous window. `currentTeam` is the selected semester's own current
student team; it is null for actors without an effective STUDENT role.

## Action contract

Every action is `{ code, allowed, reasons }`. Codes and reason codes are stable
machine values. Reasons are distinct; an allowed action has an empty list.
For example, this is a fragment of a lead department review response before all
participating departments have approved:

```json
{
  "status": "UNDER_REVIEW",
  "actions": [
    { "code": "view_project", "allowed": true, "reasons": [] },
    {
      "code": "approve_project",
      "allowed": false,
      "reasons": ["DEPARTMENT_APPROVALS_INCOMPLETE"]
    }
  ]
}
```

`allowed` means the actor can begin the operation under the currently observed
resource conditions. An invitation still needs an eligible recipient; a
supervisor request still needs a specific available lecturer, no duplicate
request, and capacity under both limits. Editing and leadership transfer still
validate submitted fields/target. The context does not reserve capacity or
guarantee success against concurrent changes.

The reads use the same request time (`asOfUtc`) but do not create a transactional
snapshot across all queries. After writes or a 403/409 response, fetch context
again. Keep ordinary API error handling. On logout/account change, clear any FE
context. An unknown/missing action should remain hidden/disabled; unknown reason
codes should get a generic unavailable message.

## Navigation actions

| Codes | Gate / meaning |
| --- | --- |
| `view_dashboard`, `view_projects`, `view_notifications`, `edit_profile`, `change_password` | Active authenticated user's navigation; project lists still filter by scope |
| `manage_accounts`, `manage_semesters` | Effective ADMIN |
| `manage_academic_structure` | Effective ADMIN or active department staff; staff remain restricted to their own scope |
| `view_review_queue` | Effective ADMIN or active department staff; a queue entry does not imply permission to approve it |
| `view_invitations` | Effective STUDENT |
| `view_team` | Effective STUDENT with a current team in the selected semester |
| `view_supervisor_inbox`, `manage_own_supervisor_profile` | Effective LECTURER with active department/organization |
| `create_team` | Effective eligible STUDENT, matching selected organization, no current team, open registration and valid BE-12 team policy |

For interdisciplinary formation, FE supplies `academicScope` with mode, lead
department and major quotas to the existing create-team API. A legacy payload
without scope is rejected when `MinDistinctMajors > 1`. An allowed create action
does not infer the desired mode or build these payload fields.

## Team actions

Only current members with an effective STUDENT role can read team actions.
Staff/admin roles alone do not bypass this membership requirement.
The response includes `canRegister`, `eligibilityIssues`, and
`academicScopeConcurrencyToken` for the scope update form.

| Codes | Gates |
| --- | --- |
| `view_team` | Current member |
| `edit_team`, `refresh_eligibility` | Leader and mutable roster/window/policy/scope |
| `configure_academic_scope` | Leader, unlocked roster, open window, valid policy; permits repairing invalid existing scope |
| `view_invitation_candidates`, `invite_member` | Eligible leader, mutable context and available total/major capacity; minimum quotas may still be incomplete |
| `view_team_invitations` | Leader |
| `leave_team` | Nonleader and mutable context |
| `remove_member` | Leader, another member exists, mutable context |
| `transfer_leadership` | Leader, another eligible member exists, mutable context; selected replacement is revalidated |
| `create_project_draft` | Leader, full team eligibility, unambiguous registration semester and no active project |

GET actions does not persist eligibility or modify cached team status, audit,
notifications, or membership. Use the existing explicit eligibility refresh
command when a persisted status refresh is intended.

## Project actions

The existing scoped project reader protects this route. It checks current
membership, supervisor assignment, or authorized department/admin access.
Configured submissions use their frozen department scope for review access.
The response supplies `concurrencyToken`, `submissionSnapshotId` and
`actorDepartmentId` for existing write contracts.

| Codes | Gates |
| --- | --- |
| `view_project`, `view_project_history`, `view_academic_review` | Authorized project reader |
| `edit_project_draft`, `set_project_majors` | Effective student leader in DRAFT or REVISION_REQUIRED |
| `submit_project` | DRAFT, leader, registration eligibility, structured fields, required tags and matching academic scope |
| `resubmit_project` | Same checks, in REVISION_REQUIRED |
| `start_review`, `request_revision`, `reject_project`, `approve_project` | Allowed state-machine transition and authorized reviewer |
| `approve_department`, `reject_department` | UNDER_REVIEW, active staff of a required department with a pending decision |
| `view_supervisor_candidates` | Existing supervisor read scope, APPROVED without active assignment, active semester/majors, one configured selection period |
| `send_supervisor_request` | Student leader, active department, same general selection gates; lecturer-specific checks occur on submission |

For configured proposals, the reviewer is active staff of the snapshot lead
department. ADMIN alone does not acquire this role. Interdisciplinary approval
also requires every snapshot department decision to be APPROVED. Revision and
resubmission create fresh decisions through the existing write workflow.

The context offers one canonical submit button per state even though existing
submit/resubmit write handlers both accept the state-machine transition to
SUBMITTED. Those write contracts are unchanged.

## Reason codes and errors

| Reason codes | FE interpretation |
| --- | --- |
| `ADMIN_REQUIRED`, `ACADEMIC_MANAGER_REQUIRED`, `REVIEWER_SCOPE_REQUIRED`, `STUDENT_ROLE_REQUIRED`, `LECTURER_ROLE_REQUIRED`, `DEPARTMENT_STAFF_REQUIRED` | Actor lacks required role/scope |
| `DEPARTMENT_SCOPE_MISSING`, `ACADEMIC_SCOPE_INACTIVE`, `STUDENT_PROFILE_INELIGIBLE`, `ORGANIZATION_MISMATCH` | Academic profile needs correction or lies outside selected scope |
| `NO_CURRENT_SEMESTER`, `SEMESTER_SELECTION_REQUIRED`, `SEMESTER_NOT_CURRENT`, `AMBIGUOUS_REGISTRATION_SEMESTER` | Select a valid semester or resolve overlapping configuration |
| `REGISTRATION_WINDOW_UNAVAILABLE`, `TEAM_POLICY_UNCONFIGURED`, `UNSUPPORTED_HYBRID_POLICY`, `ACADEMIC_SCOPE_INVALID` | Registration/policy/scope configuration blocks the operation |
| `NO_CURRENT_TEAM`, `TEAM_ALREADY_EXISTS`, `TEAM_LEADER_REQUIRED`, `ROSTER_LOCKED`, `TRANSFER_LEADERSHIP_FIRST`, `NO_OTHER_MEMBER`, `NO_ELIGIBLE_REPLACEMENT`, `PROJECT_ALREADY_EXISTS` | Membership, leadership or workflow state blocks the operation |
| `TEAM_FULL`, `ALL_MAJOR_QUOTAS_FULL` | No invitation capacity |
| Existing TeamRules/HybridTeamRules codes, including `TOO_FEW_MEMBERS`, `INELIGIBLE_MEMBER`, `MAJOR_MIN_MEMBERS:<id>` | Render existing team eligibility feedback; major codes identify the affected quota |
| `PROPOSAL_FIELDS_REQUIRED`, `PROJECT_MAJORS_REQUIRED`, `PROPOSAL_TAGS_REQUIRED`, `PROJECT_MAJOR_SCOPE_MISMATCH` | Proposal needs completion/correction |
| `PROJECT_NOT_EDITABLE`, `INVALID_PROJECT_STATE`, `SUBMISSION_SNAPSHOT_REQUIRED` | Wrong state or missing submission evidence |
| `LEAD_DEPARTMENT_REVIEWER_REQUIRED`, `DEPARTMENT_APPROVALS_INCOMPLETE`, `NOT_REQUIRED_DEPARTMENT`, `DEPARTMENT_ALREADY_DECIDED` | Department review restrictions |
| `SUPERVISOR_SELECTION_STATE_INVALID`, `PROJECT_ACADEMIC_CONTEXT_INACTIVE`, `SUPERVISOR_SELECTION_POLICY_UNAVAILABLE`, `SUPERVISOR_CONTEXT_ACCESS_DENIED` | Supervisor selection is not currently available |

Nonpositive IDs return 400. Anonymous/inactive accounts return 401. Unauthorized
resource readers return 403; nonexistent resources or foreign semester selection
return 404. Responses use the application's existing ProblemDetails middleware.
