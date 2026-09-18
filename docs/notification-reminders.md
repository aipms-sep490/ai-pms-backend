# Scheduled in-app notifications (BE-03B)

The API can run a background sweep for active projects in an active semester and
organization. It uses the server UTC clock and the existing notification inbox.
There is no public endpoint to trigger a sweep and no external email/push delivery.

The optional SMTP notification worker is configured separately with
`NotificationEmail__Enabled=true`. It queues every in-app notification after its
transaction commits, claims each recipient exactly once at a time, and retries
temporary SMTP failures with bounded exponential backoff. After the configured
maximum attempts it marks the delivery `FAILED`; the in-app notification remains
available and unchanged. SMTP credentials use the existing `Email__Host`,
`Email__SenderAddress`, `Email__Username`, `Email__Password`, `Email__Port` and
`Email__EnableSsl` settings. Keep this worker disabled until SMTP is verified.

## Deployment

1. Apply existing final-submission migrations, then
   `db/changes/20260918_add_scheduled_notification_occurrences.sql` to the target
   database using the normal migration process. This additive, rerunnable migration
   leaves the base schema and generated models untouched.
2. Set `ScheduledNotifications:Enabled` to `true` and restart the API. The default
   is `false`, so existing installations can apply the migration before enabling
   the worker. Environment variable: `ScheduledNotifications__Enabled=true`.
3. Optional settings: `IntervalSeconds` (default 300, range 30-86400),
   `ReminderHours` (24, range 1-720), `BatchSize` (50, range 1-500).
   Invalid settings fail startup. An enabled worker sweeps immediately, then waits
   the configured interval after each sweep. At least one API process must be running.

## Rules

| Work | Reminder / warning |
| --- | --- |
| Task | TODO, IN_PROGRESS, BLOCKED, IN_REVIEW; due within the horizon. DONE/CANCELLED tasks and tasks in COMPLETED/CANCELLED milestones are excluded. |
| Deliverable | DRAFT, OPEN, REJECTED with a deadline; excludes completed/cancelled milestones and any current SUBMITTED/ACCEPTED version. |
| Final submission | ACTIVE project without a locked final package; only a started ACTIVE FINAL_SUBMISSION period. One open window takes precedence; overlapping open windows suppress reminders. If no window is open, use the most recently started window for overdue detection. |
| Progress risk | The same BE-06 analysis as the project API; MEDIUM, HIGH, CRITICAL produce warnings. LOW and INSUFFICIENT_DATA risk levels do not. BE-06 can return HIGH/CRITICAL from strong blocker evidence even with incomplete data. |

Task/deliverable overdue means `deadline < now`, matching BE-06. Final submission
closes at `now >= EndAt`, matching its exclusive submission window. No future-window
notification is sent before StartAt. Reports have no authoritative submission deadline;
their period end is not treated as one.

Reminders go to current active student team members. Overdue/risk warnings also go
to the current primary supervisor with lecturer role and an active project-major
department. Every recipient must have an active account, department and organization
in the project's organization. No blanket admin/staff notification is emitted.
Completed, archived, and final-submitted projects are excluded; academic scope is
rechecked inside the publishing transaction.

## Delivery and deduplication

- Deadline occurrence identity includes project, notification type, source ID and
  persisted deadline ticks. A reminder and an overdue warning are separate events.
  Changing the deadline permits another occurrence. Reverting to an already notified
  deadline does not resend it. Reopening work with an unchanged deadline does not resend it.
- Risk occurrence identity includes project, UTC date and risk level. At most one
  notification per level per UTC day; escalation can notify on the same day.
  Recovery and return to the same level in that day does not resend it.
- Each occurrence has one notification and one inbox entry per recipient. Newly
  eligible members may receive the existing occurrence on a later sweep while it
  remains actionable. Previously delivered inbox history is retained after access changes.
- A SERIALIZABLE transaction with a project update lock keeps source state and
  eligibility stable during publication, serializes concurrent workers, and commits
  the occurrence, notification and recipients together. The occurrence primary key
  provides a database uniqueness constraint. Failures roll everything back.
- Projects are scanned by keyset in bounded batches. Each project gets a fresh DI
  scope and transaction. Failures are logged with the project ID; the next project
  proceeds and the failed project is retried on the next sweep. Shutdown cancels
  queries and delays. Very large individual projects still load their BE-06 facts
  in memory, as the existing analysis endpoint does.

Types: `TASK_DEADLINE_REMINDER`, `TASK_OVERDUE`, `DELIVERABLE_DEADLINE_REMINDER`,
`DELIVERABLE_OVERDUE`, `FINAL_SUBMISSION_DEADLINE_REMINDER`, `FINAL_SUBMISSION_OVERDUE`,
`PROJECT_RISK_MEDIUM`, `PROJECT_RISK_HIGH`, `PROJECT_RISK_CRITICAL`.
Task/deliverable links use their entity ID; final/risk links use PROJECT + project ID.
Resource endpoints must continue to reauthorize each request. Content is generic and
does not copy private task/project titles. System notifications have null CreatedBy.

## Verification

`ScheduledNotificationTests` uses owned, isolated SQL Server databases and covers
deadline boundaries, status suppression, overlapping final windows, locked packages,
recipient changes, primary supervision, repeated/concurrent sweeps, rescheduling,
daily BE-06 warnings, cancellation, rollback/retry, and enabled-worker inbox delivery.
`AIPMS_TEST_SQL_CONNECTION` selects a local SQL instance; the fixture creates and
drops only its own `AI_PMS_TEST_<guid>` databases. No shared application database is migrated.
