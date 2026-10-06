# Meeting email reminders

The backend sends one reminder to each eligible invited participant approximately
15 minutes before a scheduled meeting. No frontend changes or new API are required.
This applies to onsite, external-link and in-app video meetings.

## Enable

Configure in private deployment settings, User Secrets or environment variables:

```ini
MeetingReminders__Enabled=true
MeetingReminders__MinutesBefore=15
MeetingReminders__IntervalSeconds=30
MeetingReminders__BatchSize=50
MeetingReminders__MaxAttempts=5
```

Default is disabled. SMTP uses existing `Email` host, port, TLS, sender name/address,
username, password and timeout settings. An enabled reminder worker fails startup
if SMTP configuration is missing. Store credentials outside Git. The default SMTP
timeout is 30 seconds (existing allowed maximum: 300 seconds).

`NotificationEmail:Enabled` and `ScheduledNotifications:Enabled` can remain false:
the dedicated reminder worker creates and claims only `MEETING_START_REMINDER`.
Enabling this feature does not release unrelated historical emails. If the general
notification email worker is enabled too, both workers safely share the queue;
the general worker uses its own retry settings and also revalidates reminders.

No new migration is required. Deployment must already include
`20260918_add_scheduled_notification_occurrences.sql` and the existing
`notification_email_deliveries` migration/schema.

## Eligibility and time

- Meeting must be `SCHEDULED`, project `ACTIVE`, with `now < startAt <= now + 15min`.
- Recipient must be an actual `MeetingParticipant`, with null/`INVITED`/`ACCEPTED`
  attendance status, an active account, and current project access through the same
  backend predicate used by video meeting authorization.
- No blanket email to team members, staff or admins. An organizer receives a
  reminder only if included as a participant. Removed members, unassigned/ended
  supervisors and declined invitations are excluded.
- A meeting created less than 15 minutes before its start is picked up by the next
  sweep. A participant added during the window can receive the existing occurrence.
- Polling starts immediately, then waits 30 seconds after each sweep. Database/SMTP
  load can delay dispatch; this is not an exact-to-the-second delivery guarantee.
  Keep at least one API instance running. After an outage, only still-future meetings
  within the configured window are caught up; past meetings are not mailed.
- Subject/body include title, project code, start in Vietnam time (UTC+7), location
  and external link or instructions to open the in-app meeting. No media token,
  SMTP credential or guessed frontend URL is included.

## Durability, rescheduling and retries

Occurrence identity includes the meeting ID and persisted UTC start ticks. Under
team/project/meeting locks, occurrence, notification inbox entries and email queue
rows commit together. Concurrent/repeated sweeps do not create duplicate reminders.
A different start time gets a new occurrence. Returning to an already notified time
does not resend it; terminal suppressed occurrences are not reopened automatically.

At every claim, recheck meeting/project state, current start and recipient access.
Refresh content from current meeting details. Cancelled/completed, rescheduled,
started or unauthorized deliveries become `FAILED` with
`MEETING_REMINDER_OBSOLETE` and never reach SMTP. Existing inbox history remains.
Checks occur before sending, without holding database locks during network I/O;
an email already in flight cannot be recalled after a concurrent schedule change.

SMTP failure/timeout schedules retries after 30, 60, 120 and 240 seconds, with five
send attempts by default. A crashed claim can be reclaimed after six minutes,
longer than the maximum SMTP timeout. An attempt counter fences completion so an
older worker cannot overwrite a newer claim. Other notification types retain their
existing 15-minute reclaim interval. An expired meeting is suppressed even on retry.

SMTP is at-least-once: if delivery succeeded remotely but acknowledgment or database
completion was lost, a retry may deliver another email. `SENT` means SMTP accepted
the message, not proof of receipt in the recipient inbox. No public send-test endpoint
is added. To stop new reminders, disable `MeetingReminders:Enabled`; if the general
email worker is enabled, it may still process already queued valid reminders.

## Verification

- `MeetingReminderTests`: SQL boundary conditions, concurrent/repeated sweeps,
  eligibility, newly invited recipients, cancellation/rescheduling/scope loss,
  current content/timezone, crash reclaim, attempt fencing, queue isolation,
  transactional rollback. Uses owned `AI_PMS_TEST_<guid>` databases only.
- `MeetingReminderWorkerTests`: real hosted worker with fake SMTP/queue, paging,
  per-meeting failure isolation, obsolete suppression, retry and terminal failure,
  startup rejection when SMTP is absent.
- `NotificationEmailQueueTests` and `ExternalIntegrationTests`: existing shared
  queue and SMTP success/failure/timeout/cancellation regression coverage.

CI never sends real mail. Real acceptance uses a dedicated test meeting and explicitly
designated test mailbox; SMTP acceptance and inbox receipt are separate checks.
