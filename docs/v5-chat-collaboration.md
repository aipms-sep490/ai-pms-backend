# v5 phase 11: chat files, reactions and mute

Status: DELIVERED. PR #120 merged as b9da6751e9f24bc47be1f946f9b5cbef77f4ee14.
CI passed 1055 unit/1239 integration tests and isolated bootstrap/rerun. Shared
migration ran twice: three tables, two nullable columns, three trusted checks,
seven trusted FKs and six indexes verified; business counts unchanged. Report:
`docs/validation/v5-chat-collaboration-rollout-2026-10-12.json`.
BE-N09; backend only. Tin integrates the
additive DTOs and events separately. Existing Chat.Enabled/RealtimeEnabled flags
still apply; no global role gains chat membership.

## Contract and frontend mapping

All IDs remain decimal strings. Base: `/api/v1/chat/conversations/{id}`.

| Endpoint | Payload/result | UI |
| --- | --- | --- |
| POST `.../uploads` | multipart `file` -> ChatAttachmentDto | Upload each selected file; retain returned IDs in composer memory |
| POST `.../messages` | ChatSendRequest adds attachmentIds[]; body optional with files | Send up to 10 uploads, with/without text |
| GET `.../messages/{messageId}/attachments/{attachmentId}/download` | Private stream, attachment filename, MIME, no-store/nosniff | Download via authenticated request; no public/storage URL |
| PUT `.../messages/{messageId}/reactions` | `{ "emoji": "..." }` -> ChatMessageDto | Add own reaction idempotently |
| DELETE `.../messages/{messageId}/reactions?emoji=...` | ChatMessageDto | Remove own reaction idempotently |
| PUT `.../mute` | `{ "mutedUntil": "2026-11-01T09:00:00Z" }` or null | Mute until a future instant within 365 days; null unmutes |

Example attachment-only send (body null and empty normalize identically when files
are present; order of attachment IDs is not meaningful):

```json
{"clientMessageId":"283eacb6-eaa5-4ffa-8649-d9f2d122625f","body":null,"attachmentIds":["101","102"]}
```

ChatMessageDto adds arrays `attachments` (id, fileName, contentType, sizeBytes,
sha256) and `reactions` (emoji, count, reactedByMe). Recalled messages return empty
arrays and do not permit attachment downloads. Existing reply summaries remain
text only. ChatConversationDto adds the caller's nullable UTC `mutedUntil`.
Allowed reaction values (JSON escapes): `\uD83D\uDC4D`, `\u2764\uFE0F`,
`\uD83D\uDE02`, `\uD83D\uDE2E`, `\uD83D\uDE22`, `\uD83D\uDE4F`.
No list of reactor identities is exposed.

Each file is 1 byte..10 MiB, up to 10 per message. Shared UploadValidator verifies
filename, extension/MIME, byte count/signature and bounded archives for PDF, TXT,
PNG, JPEG, ZIP, DOCX, XLSX, PPTX. These are format checks, not malware scanning.
Multipart envelope max 11 MiB; uploads are separate requests. Max 20 unlinked
uploads per sender/conversation; reservations expire after 24 hours. Attached
content does not expire with the upload reservation.

## Authorization, retry and errors

Upload/send/reaction use existing active account, conversation source and membership
checks, rechecked inside the same conversation transaction as the mutation.
Upload must belong to the sender AND conversation, be READY, unexpired and unused.
Each upload links to only one message. Send supports stable clientMessageId retry;
same content returns the existing message and creates no extra link/audit/outbox.
Reusing the key with different content returns 409. Pre-extension text-only hashes
remain compatible. Message edits/recall retain current sender/time/token checks.

Download requires the current membership interval to include the message sequence,
the same conversation, and an unrecalled message. Removal, revoked academic
assignment, leave/rejoin intervals and inactive accounts retain existing guards.
An Admin role alone provides no access. Attachments have their own conversation
metadata and reuse IFileStorage opaque keys; no project file API can read them.

Reactions reject read-only conversations, foreign/hidden messages, revoked scope
and recalled messages. A unique (message,user,emoji) key plus conversation lock
makes concurrent add/remove idempotent. Mutation/history audit and realtime outbox
commit together; audit failure rolls back send/link/reaction/mute.

| Status | Meaning / client handling |
| --- | --- |
| 400 | Invalid content, MIME/size, more than 10/duplicate IDs, unsupported emoji or mute range |
| 401 | Missing/invalid authentication |
| 403 | Account inactive, or existing sender restriction |
| 404 | Hidden conversation/message/download, revoked membership or missing bytes |
| 409 | CHAT_ATTACHMENT_UNAVAILABLE, CHAT_UPLOAD_LIMIT, CHAT_IDEMPOTENCY_CONFLICT, CHAT_READ_ONLY, CHAT_STALE or busy transaction |
| 413 | Multipart envelope exceeds server limit |
| 429 | Existing read/write rate limits |
| 503 | CHAT_DISABLED |

Upload has no retry key: after ambiguous failure, a retry makes a new reservation;
unused objects expire. Do not retry an entire send with a new clientMessageId after
an ambiguous result. No token, storage key or provider URL is returned in metadata.

## Realtime and mute

MessagesChanged remains an invalidation event, including edits/recalls; new sends
carry a persisted message reference internally in the outbox. Its ChatEvent adds
`shouldNotify`, calculated separately for each current recipient at dispatch.
It is false for the sender, already-read/hidden/recalled messages and currently
muted users. Muted users still receive the invalidation and maintain unread state.
Tin must use shouldNotify for sound/toast rather than treating every invalidation
as a new notification; de-duplicate by EventId (delivery is at least once).

ReactionsChanged invalidates message reactions with shouldNotify=false. Mute uses
ReadStateChanged to refresh the actor's conversation state across devices; that
hint contains no private preference or message body. It does not reorder inbox or
advance read sequence. The backend does not send chat email/push in this phase.
Current SignalR connection/credential/scope checks and reconnect readback remain.

## Storage lifecycle and database

Migration `20261012_add_chat_collaboration.sql` is additive/rerunnable with
QUOTED_IDENTIFIER ON: chat_uploads, chat_message_attachments, chat_reactions;
nullable member-state muted_until and outbox message_id. Message body check now
allows empty non-recalled text; the application requires text or a linked file.
Bootstrap, manifest, EF configuration and selected readiness are updated together.

Upload commits an UPLOADING reservation before writing private bytes. It then
rechecks source access under the conversation lock and holds the upload row lock
through create/READY/audit commit. Interrupted/uncertain outcomes retain the
reservation. Failed provider creates own no object and are not deleted.
Cleanup runs every minute, at most 20 rows; it locks and commits DELETING before
calling the same storage adapter. Send can never attach DELETING/expired uploads.
Failures back off five minutes; restart retries DELETING rows. A crash after delete
is safe because DeleteAsync is idempotent. Linked content is never swept as orphan.
No storage paths, credentials or message bodies are logged by the worker. Recalled
attachments become inaccessible immediately; retention deletion is separate.

Tests use isolated DBs, HTTP lifecycle requests and DB/audit/outbox readback, plus
two real SignalR clients for mute delivery. Storage failure tests use an in-memory
adapter explicitly; they do not certify Google Drive availability or browser UX.
Shared migration waits for green CI and merge. Backend technical delivery does not
mean the FE attachment/reaction/mute controls have been integrated.

Local validation 2026-10-12: Release warnings-as-errors and 1055 unit/1236
integration tests passed before final contract assertions and three added rollback/
rerun cases. Final focused chat/schema suite passed 30 tests, including actual
SignalR mute delivery, attachment-only migration rerun, concurrent send/reaction,
audit rollback and cleanup retry. Isolated bootstrap/readiness/rerun passed. CI
repeats the full final source before merge.
