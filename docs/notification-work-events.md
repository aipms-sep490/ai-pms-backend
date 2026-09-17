# Project review and feedback notifications

BE-03B supplements the existing inbox, team/supervisor events, project approval,
evaluation finalization, final package, and result publication notifications.

| Trigger | Notification type | Recipients | Inbox destination |
| --- | --- | --- | --- |
| Project rejected | PROJECT_REJECTED | Current eligible student members | PROJECT |
| Revision requested | PROJECT_REVISION_REQUESTED | Current eligible student members | PROJECT |
| Report, meeting, or deliverable feedback saved | SUPERVISOR_FEEDBACK_ADDED | Current eligible student members | PROGRESS_REPORT, MEETING, or DELIVERABLE_VERSION |

Recipients must have an active account, student role, active department and
organization in the team's organization. The actor and departed members are
excluded. Notification text is generic: review reasons, feedback, grades, and
private project content are not copied into the inbox.

The Application publishes MediatR events inside the source transaction after
persisting the decision/feedback and its audit record. Notification failure rolls
back the workflow, history, and audit; retry can then complete normally. Report
and meeting handlers publish inside their repository transaction callback.

Project rejection/revision events carry the updated project concurrency token.
The writer locks the source, rejects stale events, and identifies each occurrence
by its project status history row. Feedback events use the feedback row ID, so
multiple feedback entries on one report each notify the team. Concurrent replays
of one occurrence create one notification and preserve recipient read state.
No schema migration is required.

The stored related entity uses PROJECT_STATUS_HISTORY or SUPERVISOR_FEEDBACK to
deduplicate occurrences. The inbox resolves these to their navigable parent IDs
in at most two batched queries for the current page. Existing pagination, filters,
and mark-read routes are unchanged. Frontends continue routing using
relatedEntityType/relatedEntityId; the destination API checks current resource
access, including when the user has left the team since receiving a notification.

## Verification

`ProjectWorkNotificationTests.cs` extends the real SQL supervisor fixture and
checks endpoint publication, atomic rollback/retry, recipient eligibility,
repeated revisions with a fixed clock, concurrent replay, repeated report/meeting
feedback, and access revocation for notification destinations.

With a local SQL connection supplied through `AIPMS_TEST_SQL_CONNECTION`:

```powershell
dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj --filter 'FullyQualifiedName~Work_notification'
```

For limited local SQL resources, run the complete integration suite with
`-- xUnit.MaxParallelThreads=1 xUnit.ParallelizeTestCollections=false`.

Deadline reminders, delay/risk warnings, and external email/push delivery remain
separate unfinished work under issue #8. This change completes the missing
project-decision and feedback events; it does not close the whole issue.
