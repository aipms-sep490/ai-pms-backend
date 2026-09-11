# Teams

Team formation, invitations and single-major eligibility. Interdisciplinary teams
are not enabled by this module.

## Invitation candidate search

`GET /api/v1/teams/{teamId}/invitation-candidates?search=SE001&page=1&pageSize=20`

Only the current student leader of this team may use the endpoint. Authorization
checks active persisted student identity and academic profile as well as current
leadership. Admin/staff/lecturer privileges alone do not grant directory access.
The team must be FORMING/ELIGIBLE, have an unlocked roster, have capacity and have
an unambiguous active registration window and valid team formation policy. Missing
or incompatible academic data (including a legacy mixed-major roster) fails closed.

The server derives semester, major and organization from the authorized team and
leader; clients cannot choose a broader search scope. Candidates must be active
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

## BE-12 database policy (Foundation)

Teams uses DatabaseTeamFormationPolicyProvider to read MinTeamSize, MaxTeamSize
and MinDistinctMajors from the applicable REGISTRATION row in project_periods.
Create, invite, accept, eligibility, project registration and invitation candidate
search consume this policy. Candidate search rejects a team at the DB member limit;
transactional invite/accept operations recheck the current limits.

Missing or invalid policy blocks operations. When both size limits are null, the
policy is unconfigured; otherwise a null minimum defaults to 3 and a null maximum
to 5. A null MinDistinctMajors defaults to 1. Foundation supports single-major
teams only: MinDistinctMajors > 1 returns 409 for operations requiring a usable
policy, while team reads expose UNSUPPORTED_HYBRID_POLICY and CanRegister=false.

Invitation expiry remains a separate Teams setting: a valid 1..720-hour value in
`TeamFormation:Periods:{registrationPeriodId}:InvitationHours` takes precedence
over `TeamFormation:DefaultInvitationHours`; otherwise the fallback is 24 hours.
The former configuration MinMembers, MaxMembers and Version values no longer
control the DB provider. PolicyVersion is a deterministic fingerprint of period
ID and effective DB team limits; unrelated period edits and invitation expiry
settings do not change it.

Full Hybrid support (ProjectMode, PrimaryMajor, per-major quotas and participating
department decisions; SRS BR-45/47/49/55/56) remains deferred. The fingerprint is
not a persisted submission snapshot: historical policy and academic-scope
snapshots required by BR-35 also remain deferred. Milestone template management
and immutable rubric versions retain their separately agreed scopes. This
Foundation integration introduces no schema migration.
