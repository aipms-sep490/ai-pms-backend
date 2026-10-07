# Chat realtime contract v1

The SignalR hub is `/hubs/chat`. It is disabled unless both `Chat:Enabled` and
`Chat:RealtimeEnabled` are true. The browser uses the existing JWT session through
`accessTokenFactory`; it never stores a separate hub token. Query-string
`access_token` is accepted only on this exact path and must be redacted by proxy
and application request logging.

## Client methods

- `WatchConversation(conversationId)` validates current SQL access before joining a
  transient transport group.
- `UnwatchConversation(conversationId)` leaves that group.
- `SetTyping(conversationId, isTyping)` is server-checked and should be sent at most
  every three seconds. Server-side clients expire typing UI after six seconds.

Client group membership is not authority. Every REST read/write and every dispatch
re-resolves current membership/assignment/account state. A stale socket receives no
protected payload. Logout, refresh failure and account/password invalidation close
the connection through the normal FE session provider.

## Events

`ConversationChanged`, `MessagesChanged`, `ReadStateChanged` and `AccessRevoked`
carry a minimal envelope only:

```json
{
  "eventId": "guid",
  "conversationId": "123",
  "version": "7",
  "schemaVersion": 1
}
```

The client refetches authorized REST data after an event; message bodies, email,
role and recipient lists are never broadcast in an outbox event. Events are
at-least-once and may arrive late or duplicated. Deduplicate by `eventId` and
discard an envelope whose version is not newer than the local version. REST catch-up
after reconnect is authoritative.

`TypingChanged` includes only conversation ID, opaque decimal user ID and a boolean;
it is sent only to currently authorized peers. Presence is approximate connection
state and is added to the same per-user routing model; it is never a global last-seen
record or official attendance evidence.

## Reliability and operations

The REST transaction commits message, conversation sequence, audit and outbox row
together. A local wake signal reduces latency; SQL polling (default five seconds)
recovers restarts and lost wakes. Claim leases last two minutes and are fenced by
lease token. Dispatch failure retries with bounded backoff; duplicate dispatch does
not create a duplicate message. Only one API instance is supported without a
backplane; a second instance requires Azure SignalR or an equivalent shared routing
service before rollout.

Cloudflare must allow WebSocket upgrades for `/hubs/chat`. Use HTTPS/WSS in staging
and production. Do not log query-string tokens, message content, typing text or
private emails.
