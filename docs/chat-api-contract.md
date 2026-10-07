# Chat REST contract v1

Chat is text-only, feature-flagged and independent of meetings, academic evidence
and email notifications. `Chat:Enabled` defaults to false. Apply
`20261007_add_realtime_chat.sql` before enabling. SQL and bootstrap use the same
rerunnable definition; EF models reflect that SQL, not EF migrations.

## Identity and authorization

All endpoints require the existing access JWT. All chat IDs/sequences in JSON are
decimal strings, timestamps UTC ISO-8601, tokens GUIDs. Use BigInt/string comparisons
on FE; do not convert message sequences to JavaScript Number.

- TEAM: current student members only.
- PROJECT: current student members plus active PRIMARY / DISCIPLINE_MENTOR assignments.
- DIRECT: active pair sharing an open team/project chat relation. One pair = one room.
- Same department, Admin, staff, evaluator and lecturer roles alone grant no access.
- Direct recipient search returns eligible IDs/names, never private email addresses.
- New admission starts at the next message sequence. Re-entry does not reveal earlier
  admission or absence history. Source changes conservatively restart admission.
- Database triggers close intervals on member/assignment removal, account/role changes
  and department/organization disabling, even when no chat request observes the gap.
- Closure of a group source captures current admitted membership; captured readers
  retain read-only access. No new group is created from a closed source. Disabled
  accounts cannot read retained conversations. Closed conversations never reopen implicitly.
- Recheck account credentials and authority inside mutation transactions. Read receipts
  and reply targets must refer to visible messages in the same conversation.

Source triggers use `SET NOCOUNT ON`; EF SQL OUTPUT is disabled for the affected
source tables. This affects SQL execution strategy, not existing business contracts.
No user/role/team/project data is backfilled or rewritten by the migration.

## Routes

Prefix `/api/v1/chat`:

| Method | Route | Payload / query |
| --- | --- | --- |
| GET | /recipients | search (max100), cursor, pageSize |
| GET | /conversations | cursor, pageSize |
| POST | /conversations/direct | `{ recipientUserId: "..." }` |
| POST | /teams/{teamId}/conversation | none; get/create official room |
| POST | /projects/{projectId}/conversation | none; get/create official room |
| GET | /conversations/{id} | detail/capabilities |
| GET | /conversations/{id}/members | cursor, pageSize |
| GET | /conversations/{id}/messages | before OR after, pageSize |
| POST | /conversations/{id}/messages | `{ clientMessageId, body, replyToMessageId? }` |
| PATCH | /conversations/{id}/messages/{messageId} | `{ body, concurrencyToken }` |
| POST | /conversations/{id}/messages/{messageId}/recall | `{ concurrencyToken }` |
| PUT | /conversations/{id}/read | `{ messageId }`, success204 |

Other successful endpoints return200. Get/create and send retries return the existing
canonical row. History is oldest-to-newest within each page; initial/before selects
the latest matching page. Use `nextCursor` for subsequent pages. `after` selects the
earliest matching newer page. Cursors are opaque and bound to a conversation.

`ChatPage<T>` = `{items,nextCursor,hasMore}`. Default inbox/contacts page30; default
history/members50, maximum100. An inbox can reorder during activity: deduplicate by
ID and refresh its first page on updates. Unknown resources and denied chat scope
both return404; this deliberately avoids exposing existence.

Conversation: id/kind/title/status, optional teamId/projectId, sequence/version,
updatedAt, unreadCount, lastMessage, canSend. Last preview and unread count obey
admission bounds. Unread excludes own/recalled messages. Do not treat the conversation
sequence as evidence that the actor may read all earlier messages.

Message: id/conversationId/sequence/senderId/senderName, clientMessageId, body,
createdAt/editedAt/recalledAt, concurrencyToken, reply `{id,body,unavailable}`,
canEdit/canRecall. Recalled body is null; reply previews become unavailable. A read-only
conversation must disable all send/edit/recall controls regardless of cached capability.

## Writes and recovery

Maximum body4,000 UTF-16 code units (configured lower limits allowed), nonblank,
Unicode permitted. Render text safely, never HTML. No attachment/remote preview API.
Edit/recall own message within15 minutes, enforced by server time and token.

Every send uses a client-generated nonzero UUID, persisted on the optimistic item.
Retry with the SAME UUID/body/reply. Same key with changed content returns409
`CHAT_IDEMPOTENCY_CONFLICT`. Do not create a new key after a lost response.
Message, conversation sequence, body-free audit and outbox commit atomically.

Read markers advance monotonically. FE marks only the last visible rendered message
while the page is focused. Receiving an event must not itself mark messages read.

REST limits: reads180/minute/user, writes30/minute/user by default. 429 includes
Retry-After. Error status classes:400 invalid input,401 invalid credentials,403 inactive
account or non-sender mutation,404 hidden resource,409 stale/state/idempotency conflict,
429 rate limit,503 disabled feature. Stable domain codes use CHAT_* in problem details.

## Validation

`ChatSqlTests` uses an owned SQL database: concurrent creation/send, retry payload
conflicts, cursor history, monotonic reads, denial of implicit Admin access, late
supervisor admission, ended assignments, membership/role/department revocation,
closure retention, edit/recall, audit rollback and migration rerun.
`ChatApiTests` checks real controllers/JWT plumbing, string ID responses, disabled
feature, hidden foreign resources and Swagger route exposure.

The REST/outbox layer alone is not a complete realtime release. SignalR dispatch,
FE integration and joint staging acceptance follow in dependent changes.
