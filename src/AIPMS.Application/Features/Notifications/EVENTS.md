# Teams, Supervisors and final-submission notification events

WorkflowNotificationEvent is an internal synchronous application event published
through MediatR after a successful persisted transition, inside its existing SQL
transaction. Its handler resolves recipients from current persisted relationships
and writes in-app notifications through IWorkflowNotificationWriter. Controllers
do not construct notifications. Existing Deliverables producers are unchanged.

| Notification type | Recipient | Related entity |
| --- | --- | --- |
| TEAM_INVITATION_SENT | Invited student | TEAM_INVITATION / invitation ID |
| TEAM_INVITATION_ACCEPTED | Current team leader | TEAM_INVITATION / invitation ID |
| TEAM_INVITATION_REJECTED | Current team leader | TEAM_INVITATION / invitation ID |
| TEAM_INVITATION_CANCELLED | Invited student | TEAM_INVITATION / invitation ID |
| SUPERVISOR_REQUEST_SENT | Requested lecturer | SUPERVISOR_REQUEST / request ID |
| SUPERVISOR_REQUEST_ACCEPTED | Current project team leader | SUPERVISOR_REQUEST / request ID |
| SUPERVISOR_REQUEST_REJECTED | Current project team leader | SUPERVISOR_REQUEST / request ID |
| SUPERVISOR_REQUEST_CANCELLED | Requested lecturer | SUPERVISOR_REQUEST / request ID |
| FINAL_SUBMISSION_LOCKED | Active staff in project major departments and semester organization | PROJECT / project ID (navigate to final-submission route) |
| EVALUATION_FINALIZED | Active staff in assignment department and project/organization scope | EVALUATION / evaluation ID |

Automatic cancellation of competing supervision requests after acceptance uses
the same cancellation event. Expiration reminders and project approval events
are outside this slice.

Recipients must be active accounts with the relevant persisted Student/Lecturer
role and active department/organization in the team's academic organization.
Lecturer recipients must still belong to the project's current major-department
scope. Leader recipients must have current active leader membership; original
senders who have lost leadership are not notified of later decisions. Actor
self-notifications are excluded. If no eligible recipient remains, no notification
is created and the valid workflow transition can finish.

The writer requires an active transaction and locks the source invitation/request
row. The pair (related entity type/ID, notification type) identifies a single
transition notification. The source lock serializes repeated event handling, and
the existing notification check prevents duplicate recipient rows or resetting
read state. This works with existing tables and their recipient uniqueness index;
it is not an asynchronous outbox or a guarantee for other notification producers.
Source status must still match the event, so stale events cannot describe a
different current transition. A new invitation/request ID is a distinct event.

Notification/recipient writes and business/audit writes commit or roll back as one
transaction. Notification persistence failures propagate; the workflow cannot
commit without its attempted notification write. Existing workflow replay behavior
is preserved: Teams rejects processed invitations with 409, while supervisor
same-decision replays return the prior decision without publishing another event.

In-app delivery sets DeliveredAt, initializes unread state and uses the workflow
timestamp. Titles/content are fixed plain text and omit request/response messages
and private project details. Frontend should use the related source ID to navigate
to the authorized team invitation or supervisor request view/list. Reauthorize
every subsequent resource read/action; a notification does not grant resource
access and an old link may no longer be accessible.

No external email/push is sent. Notification tables need no new migration;
the final-submission producer requires the BE-16 locked-package migration.
The evaluation-finalized producer requires the evaluation_finalizations migration;
score status/total, immutable snapshot, audit and notification share one transaction.
Repeated finalization events are deduplicated by their source evaluation lock.
Final package/source, project state, audit and staff inbox share one transaction.
The final-submission source row lock deduplicates replay of its event; one package
per project makes PROJECT/project ID plus notification type a stable inbox key.
Project approval/revision, report feedback, reminders and external
delivery remain separate work; issue #8 is not fully Done.
