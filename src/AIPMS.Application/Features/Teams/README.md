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

This endpoint uses the current ITeamFormationPolicyProvider. It does not implement
the separate BE-12 database-policy adapter: deployments still need explicit
`TeamFormation:Periods:{registrationPeriodId}:MinMembers`, MaxMembers,
InvitationHours and Version settings until that adapter is integrated. No schema
change is required by candidate search.
