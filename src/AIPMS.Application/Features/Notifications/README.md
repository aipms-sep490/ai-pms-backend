# Personal notification inbox (BE-03B, first slice)

All endpoints require a valid token for an active account. User identity comes
from the authenticated principal; no endpoint accepts a recipient/user ID. Role
or notification-creator status does not grant access to other recipients' inboxes.

| Method | Route | Result |
| --- | --- | --- |
| GET | /api/v1/notifications | PagedResult of NotificationDto |
| GET | /api/v1/notifications/unread-count | `{ "count": 2 }` |
| PATCH | /api/v1/notifications/{notificationId}/read | 204, no request body |
| POST | /api/v1/notifications/read-all | 204, no request body |

List filters: optional `isRead=true/false`, exact `notificationType` (trimmed,
maximum 50 characters; empty means no filter), `page` (1..1,000,000, default 1)
and `pageSize` (1..100, default 20). Type comparison follows SQL collation; unknown
types produce an empty list. Results sort by notification CreatedAt descending,
then notification ID descending. Filtering and paging run in SQL. Unread count
covers the entire caller's inbox, independent of list filters. Count/list are
live reads; newly arriving notifications or read updates may change pagination
between requests.

```json
{
  "items": [{
    "id": 42,
    "notificationType": "TEAM_INVITATION",
    "title": "Team invitation",
    "content": "You have a team invitation.",
    "relatedEntityType": "TEAM_INVITATION",
    "relatedEntityId": 123,
    "createdAt": "2026-09-12T10:00:00Z",
    "isRead": false,
    "readAt": null
  }],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

`id` is the notification ID, not the recipient-row ID. Read state is per recipient;
reading a shared notification never marks it read for anyone else. Mark-one uses
an atomic owner-scoped conditional update; repeated/concurrent calls preserve
the first ReadAt and UpdatedAt. Mark-all updates only the caller's unread rows in
one SQL statement and preserves already-read timestamps. Notifications arriving
after that statement are not covered; a later call can mark new rows. Empty
inboxes and repeated calls return 204. Missing and unowned IDs both return 404.

Responses use UTC timestamps and no-store caching. Invalid filters/IDs return
400 ProblemDetails, invalid/missing authentication 401, and inaccessible IDs 404.
Frontend should render title/content as text, handle empty/loading/error states,
and refresh the list and unread count after read operations. Resolve navigation
from supported relatedEntityType/relatedEntityId pairs; tolerate missing or unknown
targets. Opening the target must use its normal resource authorization. An old
notification does not grant current project access. No recipient lists or private
storage paths are exposed.

This slice reads existing notifications/notification_recipients and adds no
creation API, schema change or external delivery. Existing producers remain in
place. Domain event integration, recipient resolution, deduplication, reminders,
email/push and additional workflow events belong to the next slice; issue #8 is
not fully complete after this inbox implementation.
