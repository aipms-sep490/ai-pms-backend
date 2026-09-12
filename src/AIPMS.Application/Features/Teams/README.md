# Teams

Team formation, invitations and mode-aware eligibility. New clients configure
SINGLE_MAJOR or INTERDISCIPLINARY through academicScope; see
`../../../../docs/interdisciplinary-demo.md` for the complete FE contract.

## Invitation candidate search

`GET /api/v1/teams/{teamId}/invitation-candidates?search=SE001&page=1&pageSize=20`

Only the current student leader of this team may use the endpoint. Authorization
checks active persisted student identity and academic profile as well as current
leadership. Admin/staff/lecturer privileges alone do not grant directory access.
The team must be FORMING/ELIGIBLE, have an unlocked roster, have capacity and have
an unambiguous active registration window and valid team formation policy. Missing
or incompatible academic data (including a legacy mixed-major roster) fails closed.

The server derives semester, allowed majors and organization from the authorized
team scope (leader's major for legacy teams); clients cannot broaden the candidate
query. Full per-major quotas are excluded before pagination. Candidates must be active
students with active matching major/department/organization, consistent department
and major links, and no current membership in that semester. Memberships in other
semesters and ended memberships do not disqualify a candidate. Current members,
including the leader, are excluded.

`search` is optional, trimmed, at most 255 characters, and matches substrings in
full name, email or student code. Case/accent behavior follows the database
collation. Empty/whitespace search lists eligible candidates. SQL filters precede
paging; results sort by full name then user ID. `page` starts at 1 (maximum
1,000,000); `pageSize` defaults to 20 and is bounded to 1..100.

The response is PagedResult with items/page/pageSize/totalCount/totalPages. Each
candidate exposes only identity needed to invite and the requesting team's live
pending invitation:

```json
{
  "userId": 42,
  "fullName": "Demo Student",
  "email": "student@example.test",
  "studentCode": "SE001",
  "majorId": 10,
  "majorCode": "SE",
  "majorName": "Software Engineering",
  "invitationStatus": "PENDING",
  "pendingInvitationId": 123,
  "pendingInvitationExpiresAt": "2026-09-12T08:00:00Z",
  "canInvite": false
}
```

When there is no live pending invitation from this team, invitationStatus is NONE,
the pending fields are null and canInvite is true. PENDING requires an expiry
strictly after server time. Expired or missing expiry, cancelled and rejected
invitations do not block re-inviting. Search never rewrites invitation history.
An invitation from another team does not reserve membership and is not disclosed.
No passwords, phone numbers, security state or other teams' invitation data are
returned.

Frontend should debounce search, reset paging when search changes and disable the
Invite action for PENDING candidates. Use the candidate userId with the existing
`POST /api/v1/teams/{teamId}/invitations` endpoint; use the existing invitation ID
for cancellation. An expired invitation is processed by the existing send workflow
when a new invitation is created.

Search is advisory and read-only; it does not reserve a seat or membership. Another
request can change membership, profile, leadership, capacity or period after a
search response. Existing transactional invite/accept handlers revalidate these
conditions. On 409, reload the team/candidate list; do not treat canInvite as an
authorization token or a guarantee that a later invitation will succeed.

Responses use the shared ProblemDetails pipeline: 400 invalid query, 401 missing
authentication, 403 non-leader/non-student, 404 missing team, and 409 unavailable
window/policy, locked/full team or incompatible academic context.

## BE-12 database policy

Teams uses DatabaseTeamFormationPolicyProvider to read MinTeamSize, MaxTeamSize
and MinDistinctMajors from the applicable REGISTRATION row in project_periods.
Create, invite, accept, eligibility, project registration and invitation candidate
search consume this policy. Candidate search rejects a team at the DB member limit;
transactional invite/accept operations recheck the current limits.

Missing or invalid policy blocks operations. When both size limits are null, the
policy is unconfigured; otherwise a null minimum defaults to 3 and a null maximum
to 5. A null MinDistinctMajors defaults to 1. Explicit INTERDISCIPLINARY scope
requires at least max(2, MinDistinctMajors) distinct mandatory majors; each quota
must be met. Explicit SINGLE_MAJOR scope requires all members to match PrimaryMajor.
Legacy teams without scope still fail closed when MinDistinctMajors > 1 until
their leader configures academicScope; they never silently become interdisciplinary.

Invitation expiry remains a separate Teams setting: a valid 1..720-hour value in
`TeamFormation:Periods:{registrationPeriodId}:InvitationHours` takes precedence
over `TeamFormation:DefaultInvitationHours`; otherwise the fallback is 24 hours.
The former configuration MinMembers, MaxMembers and Version values no longer
control the DB provider. PolicyVersion is a deterministic fingerprint of period
ID and effective DB team limits; unrelated period edits and invitation expiry
settings do not change it.

Configured teams persist mode, primary major (single-major only), lead department,
per-major quotas and responsibilities. Submission stores policy, roster and scope
evidence per review round. Required department decisions gate lead-department
approval. Apply `db/changes/20260912_add_interdisciplinary_projects.sql` before
deploying this version. Published topic selection and per-period allowed-mode/source
switches remain separate work; this slice enables both modes for explicit scopes.
