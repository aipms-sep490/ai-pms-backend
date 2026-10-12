# Peer evaluation rounds (expansion PR3)

`PeerEvaluation:Enabled=false` by default. Peer ratings are private contribution
evidence only: no evaluation assignment, weekly score, result or AI input is written.
Tin owns the frontend adapter and browser acceptance in a separate PR.

## Routes and DTOs

Base `/api/v1/projects/{projectId}/peer-evaluations`:

| Method / suffix | Request | Response |
| --- | --- | --- |
| GET /rounds | page, pageSize | PagedResult<PeerRoundDto> |
| POST /rounds | title, opensAt, closesAt | PeerRoundDto |
| POST /rounds/{roundId}/close | concurrencyToken | PeerRoundDto |
| GET /mine | optional roundId | MyPeerEvaluationDto |
| PUT /rounds/{roundId}/mine | concurrencyToken, items | PeerEvaluationDto |
| POST /rounds/{roundId}/mine/submit | concurrencyToken | PeerEvaluationDto |
| GET | required roundId, page, pageSize | Submitted reviewer page |

Dates accept offsets and are normalized to UTC seconds. Windows are at most 90
days and closing time must be in the future. One stored OPEN round/project,
including a scheduled or elapsed window. Primary explicitly closes that round
before creating the next. Display status is SCHEDULED, OPEN, EXPIRED or CLOSED;
there is no silent choice between multiple open rounds. GET /mine without an
active time window returns `{ "roundId": null, "status": "NO_OPEN_ROUND",
"submission": null }`; use an explicit roundId to read own historical submission.

Draft example (a subset is allowed until submit):

```json
{"concurrencyToken":null,"items":[{"targetUserId":42,"rating":4,"comment":"Agreed contribution completed"}]}
```

For a new draft token is null. Subsequent saves require the latest token. Each
target appears once, is another student in the frozen round roster, and has an
integer rating 1..5 and optional comment <=2000. Empty draft is valid. Submit
requires every other roster member and commits the whole submission atomically.
Submitted content is immutable; corrections use a new round. Repeating submit
with the submitted token returns the same record, with one audit only. A stale
save token returns 409. Close is idempotent with its original round token.

DTOs explicitly return `affectsOfficialResult=false`. Round DTO includes
concurrencyToken, capability booleans, reasons, authorized roster and memberCount.
A submitted author has canSubmit=false even while the round is open.

## Authority and provenance

Current Primary alone creates/closes rounds; neither staff nor students sign or
submit for somebody else. Only active student members submit their own work.
Round creation copies user/major/department/name from frozen registration evidence
and exact current membership ID, join time and generation. Missing target
provenance fails closed. There is no inference from today's student profile.

Roster comparison is shared by readiness and draft/submit mutations inside a
project transaction, with membership locks. Leave/rejoin or any member change
invalidates further submission to the old round. Close it and open a new round;
old submissions remain unchanged. Account disable/revoked membership blocks
further operations. Project COMPLETED/ARCHIVED is read-only for this workflow.

| Actor | Reading |
| --- | --- |
| Active roster student | Own outgoing draft/submission; no reviewer endpoint |
| Current Primary | All submitted peer evidence in project |
| Frozen lead staff | All submitted peer evidence in project |
| Participating staff | Only items whose target belongs to own frozen department |
| Admin, Mentor, evaluator by role alone | Denied |

Reviewer routes never return unfinished drafts. Department filtering applies
before count/paging and to nested items/round roster. Account and assignment
checks use persisted state on each request, not token role claims. No peer text
or rating is included in audit payloads or sent to notification/AI pipelines.

## Errors and persistence

400 invalid dates/title/paging/rating/duplicate targets; 401 anonymous; 403 actor,
self/foreign target or revoked membership; 404 disabled feature/missing round or
draft; 409 PEER_STALE, PEER_INCOMPLETE, PEER_ROSTER_CHANGED, PEER_ROUND_NOT_OPEN,
PEER_ROUND_EXISTS, PEER_SUBMITTED, PEER_PROJECT_READ_ONLY or unknown provenance.
Errors use the common ProblemDetails envelope with code where a domain code is
provided. Draft rows and submit/audit changes roll back together on failure.

Migration `20261012_add_peer_evaluations.sql`: rounds, roster, submissions and
items; unique open round, author/round and target/submission, roster FK for author,
rating/date/status checks and history indexes. Additive, rerunnable,
QUOTED_IDENTIFIER ON. Bootstrap, manifest and model/config are synchronized.
Page size <=100, roster 2..100. Reviewer queries fetch only a bounded page. Round
history performs at most one current-roster check, because only one round is open.

Tests `Peer_*` in SupervisorRequestEndpointTests use isolated SQL and actual HTTP
handlers/JWT. They cover lifecycle/readback/audit, private drafts, cross-department
redaction, no score writes, duplicate submit, stale draft, partial/self/foreign
rating, same-row roster generation change, revoked member, deadlines, concurrent
round creation, audit rollback, disabled module and archived writes. The global
schema suite checks fresh/upgrade/rerun and mapped columns. This evidence does not
claim FE browser acceptance.
