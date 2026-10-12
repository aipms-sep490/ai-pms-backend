# Working Agreement: backend contract

Feature: `WorkingAgreement__Enabled=false` by default. No frontend changes or
automatic feature activation. The migration must precede the application update.

## Routes and examples

All routes require authentication and live resource authorization. Prefix:
`/api/v1/projects/{projectId}/working-agreement`.

| Method / suffix | Contract |
| --- | --- |
| GET / | Latest draft or published version; 204 when none exists |
| GET /history?page=1&pageSize=20 | Versions newest first; pageSize 1-100, page 1-100000 |
| PUT /draft | Leader creates a successor or updates the current draft |
| POST /publish | Leader freezes content and snapshots current membership intervals |
| POST /acceptances | Current student accepts only their own snapshot entry |

Create a first draft:

```json
{"expectedLatestVersion":0,"concurrencyToken":null,"sections":[{"title":"Communication","body":"Meet each Monday"}]}
```

Update a draft using its current version/token. Create a successor with the
latest version and a null token. Sections: 1-20; title 1-200 characters; body
1-4000 characters; whitespace-only content is invalid. Content is stored as
JSON `{ "sections": [...] }`; it is not interpreted as HTML.

Publish/accept:

```json
{"agreementId":123,"concurrencyToken":"opaque-token-from-response"}
```

Response: `WorkingAgreementDto`, including version/status/contentJson, author,
UTC createdAt, concurrencyToken, required, memberCount, acceptedByMe, acceptances,
readiness and action capabilities. `ACTIVE` means the current published document,
not unanimous acceptance. A newer draft does not supersede it until published.
History row actions must be checked as well as project-level capabilities.

Draft edits and publication change the token. Acceptances do not change the
content token, allowing multiple people to consent to the same immutable version.
Repeated acceptance is idempotent and does not add another audit entry.
Publishing a successor supersedes the previous document without copying consent.
Membership ID, joined time and generation are snapshotted: leaving and rejoining
does not reuse old consent, including same-second reactivation of the existing row.

## Authority and activation

- Current student leader writes/publishes; active members accept for themselves.
- Current Primary and staff in validated frozen academic scope may read.
- Admin, ordinary lecturer, Mentor or evaluator do not gain access from role alone.
- Completed/archived projects stay readable in scope and reject mutations.
- Legacy invalid academic scope is read-only. Draft/revision project leaders may
  prepare an agreement before a frozen registration exists.

`GET /projects/{id}/my-capabilities` adds manage/publish/accept agreement actions
and nullable `workingAgreement` readiness. Disabled features return denied actions.
Activation and readiness use the same registration-bound policy predicate.

`PeriodPolicyFields.requireWorkingAgreement` defaults to false for old payloads
and snapshots. Only Admin can change it or publish a draft that toggles it.
Existing scoped staff permissions for other policy fields are preserved. Semester,
period CRUD and qualification policy routes retain their existing authority.
The policy captured by the registration snapshot is authoritative, not the latest
editable period. No legacy provenance is invented and no active project is demoted.

Primary request acceptance and the generic project status repository check the
agreement inside the status/assignment transaction before ACTIVE. If required,
the active document must match the current roster and all entries must be accepted.
This safety guard continues enforcing a captured requirement even if the feature
flag is disabled; disabling UI/API does not bypass a frozen academic requirement.

## Errors and persistence

| Condition | HTTP / reason |
| --- | --- |
| Invalid content or paging | 400 |
| Anonymous | 401 |
| Actor outside scope, sign for another actor | 403 |
| Feature disabled, missing project/version | 404 |
| Stale draft/content token | 409 / WORKING_AGREEMENT_STALE |
| Roster changed | 409 / WORKING_AGREEMENT_ROSTER_CHANGED |
| Archived/completed mutation | 409 / WORKING_AGREEMENT_READ_ONLY |
| Required agreement incomplete at activation | 422, WORKING_AGREEMENT_INCOMPLETE in detail |

Migration `20261012_add_working_agreements.sql` adds project_working_agreements
and agreement_members, plus team_members.membership_generation (new GUID for each
membership activation, not a historical academic backfill). The latter table is both required-member snapshot and nullable
acceptance record. Unique project/version, one active/one draft, and one acceptance
per user/version are enforced by SQL. All mutations take the project lock and
commit audit atomically. No direct email is sent by this module.

Tests: WorkingAgreementEndpointTests, WorkingAgreementActivationTests (partial
SupervisorRequestEndpointTests), existing supervisor/policy regression and schema
parity suites. They use isolated owned databases, not shared AI_PMS.

FE delta: the existing acceptance adapter must send concurrencyToken; add draft,
publish/history UI and handle 204. Use returned capability/reasons, never infer
authority from the user role. A rendered mock screen does not enable rollout.
