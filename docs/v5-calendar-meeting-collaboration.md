# v5 phase 9: calendar and meeting collaboration

Status: implemented; CI/merge/shared rollout pending. FE is unchanged. This phase implements
BE-06 and BE-N05/06/07; it does not certify the remaining sprint/chat/AI phases.

## Calendar contract

`GET /api/v1/calendar?from=&to=&cursor=&pageSize=&sourceType=` retains its response
shape. A range of at most 366 days and pageSize 1..100 are required. Sources:
TASK, MILESTONE, MEETING, DELIVERABLE, FINAL_SUBMISSION, EVALUATION_ASSIGNMENT,
CHECKPOINT, DEFENSE_SESSION. Times are UTC. Range is inclusive from, exclusive to.
Dates are real task/deliverable due dates, milestone dates, meeting/defense start,
locked final package deadline, evaluation period end, and checkpoint planned date.
Missing final package deadlines are not fabricated from the academic period.

The v2 cursor orders by date, source ID, then source kind. It is bound to actor,
range and source filter. Reset paging after changing filters; old/mismatched or
malformed cursors return 400. Page size may change without resetting the cursor.
Each source applies authorization, range, keyset filter and Take(pageSize+1) in
SQL. Discovery is capped at 1000 projects (overflow returns 400), and snapshot
authority is loaded in batches. No per-event/project query loop. The read uses
a serializable transaction; each page rechecks current access.

| Actor | Calendar visibility |
| --- | --- |
| Active project student in frozen membership | Team project events; checkpoints common/own major |
| Current PRIMARY | Assigned project events |
| Frozen lead department staff | Project events |
| Participating department staff | Task disciplines and artifact targets in frozen department majors; scoped checkpoints |
| Active discipline mentor | Assigned major tasks/artifacts/checkpoints; meetings only when invited |
| Active scoped evaluator | Own authoritative evaluation window; invited meetings only with current relationship |
| Committee lecturer | Exact assigned defense session |
| Admin role alone | No academic calendar events |

Missing/invalid frozen snapshots fail closed. Current major department changes do
not rewrite the frozen authority. Revoked/ended assignments stop granting events.
Deep links are navigation metadata and do not bypass the destination's checks.

## Meeting API and FE mapping

| Endpoint | DTO / usage |
| --- | --- |
| POST `/api/v1/projects/{projectId}/meeting-series` | CreateMeetingSeriesRequest -> MeetingSeriesDto; schedule dialog recurrence |
| GET `/api/v1/meetings/{meetingId}/collaboration` | MeetingCollaborationDto; detail capability, self RSVP, delivery status |
| PUT `/api/v1/meetings/{meetingId}/rsvp` | RespondMeetingRequest -> MeetingRsvpDto; self response |
| POST `/api/v1/meetings/{meetingId}/notes/lock` | LockMeetingNotesRequest -> MeetingCollaborationDto; completed notes lock |
| GET `/api/v1/meetings/{meetingId}/notes/history?page=1&pageSize=20` | PagedResult<MeetingNotesVersionDto>; typed immutable history |

Existing MeetingParticipantDto adds rsvpStatus; attendanceStatus keeps its current
meaning. The self RSVP concurrency token is returned in collaboration.myRsvp.
The meeting concurrency token is required for notes lock. All existing meeting,
decisions, attendance and action routes remain available.

Example series (replace IDs and future dates with fixture values):

```json
{
  "meeting": {
    "title": "Weekly project discussion",
    "agenda": "Review progress",
    "startAt": "2026-11-02T02:00:00Z",
    "endAt": "2026-11-02T03:00:00Z",
    "location": "Demo room",
    "onlineUrl": null,
    "participantUserIds": [123],
    "meetingDeliveryMode": "ONSITE",
    "videoChannel": "NONE"
  },
  "recurrence": "WEEKLY",
  "occurrenceCount": 4
}
```

NONE requires exactly one occurrence; WEEKLY/BIWEEKLY support 1..12, with at most
100 explicitly invited participants. All occurrences commit atomically using
the existing meeting command and validation. Start/end require UTC Z timestamps.
Recurrence is fixed 7/14-day intervals (same Vietnam local time, no DST timezone
conversion). Each occurrence is a real Meeting, independently editable/cancellable,
with its own existing 15-minute reminder. Series has no separate business aggregate.
Series creation is not an idempotent upload API; after an ambiguous response, read
the project meeting list before retrying the entire series.

RSVP allows ACCEPTED/TENTATIVE/DECLINED for self participants only, while project
ACTIVE and meeting SCHEDULED. It never writes official attendance. Declined RSVP
suppresses reminders both when enqueuing and immediately before delivery. Existing
attendance-based suppression is retained. Concurrency is per participant.

Notes can be locked only after meeting COMPLETED, while project ACTIVE, by the
same server predicate used for governance mutations: current leader, assigned
supervisor, or organizer who remains an active member. Admin alone does not gain
this new lock action. Scope/account/state are checked under team/project/meeting
locks. `canLockNotes` is advisory; mutation checks again.

Every subsequent content/decision/action-definition change records an append-only
version. Lock records a final version; no unlock endpoint. Historical versions are
not fabricated for pre-migration content. Snapshot contains notes, minutes,
decisions/blockers and typed decision/action definitions from BOTH meeting and
project action-item APIs. No private storage reference is exposed. At most 1000
definitions per collection can be snapshotted; overflow rolls back with 409.
History uses pageSize 1..100. Notes lock blocks both action APIs' definition writes;
existing action progress and official attendance updates remain authorized and
audited separately. Progress changes do not rewrite the locked definition snapshot.

Email deliveries report actual queue status, attempt count and sentAt. Empty means
no recorded delivery, not SENT. Managers see the newest 100 delivery records;
other authorized readers see only their own. `hasMoreEmailDeliveries` explicitly
marks truncation. SENT means transport acceptance, not proof of inbox receipt.
The meeting-created notification now resolves its Meeting source before queueing.
No live SMTP is used in automated tests.

## Errors and authority

400 invalid enum/count/UTC/cursor/page; 401 unauthenticated; 403 wrong resource
scope, inactive account, removed assignment or nonparticipant RSVP; 404 missing
resource; 409 stale token, closed lifecycle, notes lock or bounded history limit.
Use existing ProblemDetails error envelope; refresh after 409. Example stale
request: `{ "status": "TENTATIVE", "concurrencyToken": "old-token" }` -> 409.
Never infer permission from FE role or RSVP. Reuse server canLockNotes and tokens.

Leader change reuses the current request/approval workflow: active eligible target,
one pending request, current primary approval, atomic leader/history/audit. Existing
tests cover ended/replaced mentor, target left, concurrent approval and rollback;
no new leader table or alternate transfer endpoint is introduced.

## Database and acceptance

Migration: `db/changes/20261012_add_meeting_collaboration.sql`, included in bootstrap
and ordered manifest; additive/rerunnable, QUOTED_IDENTIFIER ON. Adds series and
notes metadata, separate RSVP/token, and meeting_notes_versions. Explicit legacy
INVITED/ACCEPTED/DECLINED values are copied once; ATTENDED/ABSENT do not invent RSVP.
No business lifecycle tests or historical note backfill on shared AI_PMS.

Tests: CalendarV5EndpointTests; MeetingCollaborationEndpointTests;
MeetingReminderTests/MeetingReminderWorkerTests; TeamLeaderChangeEndpointTests;
ProgressAndMeetingConcurrencyTests; SchemaParityTests and isolated bootstrap script.
Focused acceptance includes real API writes, DB/audit readback, scope revocation,
cross-source cursor ties, RSVP race, independent cancellation, deduplicated reminder,
both action-route lock bypass attempts, audit rollback and actual Swagger schemas.
OpenAPI artifact: `docs/contracts/cib-v4/openapi.json`.

Tin: adapt calendar source kinds/cursor reset, recurrence selector, RSVP buttons,
notes lock/history and actual delivery labels. Keep attendance and RSVP separate.
Browser/keyboard/responsive FE acceptance remains Tin's work after contract release.
