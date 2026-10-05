# Video presence ordering and recovery

## Event processing

LiveKit webhooks can arrive late or out of order. Presence uses provider occurrence
times, never the time an HTTP request arrived at AI-PMS:

- `createdAt` / `created_at`: Unix seconds for the event.
- `participant.joinedAtMs` / `joined_at_ms`: preferred connection start in milliseconds.
- `participant.joinedAt` / `joined_at`: connection start in seconds when milliseconds
  are unavailable. A join event's occurrence time is the last fallback.
- Protobuf int64 values may be JSON strings or numbers.

The signature is checked before database access. SQL locks serialize updates per
video session, then per provider event ID. Inbox metadata and presence/state updates
commit in one transaction. A failed commit leaves the event retryable. No raw webhook
body, provider token, or API secret is persisted by this handler.

Presence is matched by session, bound user, and provider participant SID. A leave
never closes another SID's reconnect. A leave received first retains an end marker;
a later join refines that connection's start without reopening it. Repeated joins
for the same SID do not create another segment. A connection-aborted event alone
does not prove a successful connection.

Events with unusable timestamps or participant identity/SID are acknowledged with
a safe diagnostic in `video_provider_events.error_code`, without fabricating a
presence segment. Unrecognized event types remain acknowledged. A known event ID
cannot be reused for a different payload.

## Ending and cleanup

End Room and Meeting complete/cancel close presence inside their database transaction.
Provider cleanup remains outside that transaction and can retry. Its eventual execution
time must not extend the logical connection duration.

A delayed join for an ended session is stored closed at the known end boundary if
it describes a connection from before that boundary. A connection starting after the
boundary is ignored. A delayed room-started event cannot revive the session. A known
AI-PMS session end timestamp is retained when the later room-finished event arrives.
Subsecond starts and whole-second disconnect timestamps are clamped to prevent negative
intervals; subsecond connections may contribute zero whole seconds.

Official attendance and Meeting lifecycle are not derived from presence.

## Historical data

No migration or speculative timestamp backfill is needed. Existing invalid rows are
not evidence of an exact historical connection time: the old handler did not store
the provider occurrence timestamp. The read model excludes negative intervals and
connections starting after the session boundary, and caps open/overlong intervals
at that boundary. It does not rewrite old rows or claim that historical totals have
been reconstructed. Normal lifecycle/cleanup operations can still close old open rows.

## Verification on 2026-10-05

- Release warnings-as-errors build and 988 unit tests pass.
- 18 new SQL integration cases cover event ordering, reconnect SID correlation,
  replay/concurrency, transaction rollback/retry, invalid evidence, timestamp
  precision, lifecycle/cleanup boundaries, and legacy read filtering.
- All 52 focused Meeting and CORS integration tests pass on owned test databases.
- The first full local integration run failed with SQL pre-login connection resets
  and a password-reset timestamp constraint failure. It is not counted as a pass;
  the PR's CI run records validation on a clean SQL service.
- Real LiveKit test: Meeting 40003, video session 3, in the previously authorized
  isolated test project 30003. Two Chrome contexts used synthetic media, received
  audio/video/screen sharing, left/rejoined, and ended the room.
- Presence API returned one leader connection and two member connections. SQL
  reported zero negative/open/over-boundary segments. Cleanup was SUCCEEDED on
  attempt 1. Member start/end returned 403; join after end returned 409.
- Provider event diagnostics for those joins/leaves and room events were empty.
- Staging image tested: `sha256:513a2a81d0edb76b751c2c788ca3bba52223e96e301013b4c9dee61b1285734a`.

This verifies the presence fix and core video smoke flow, not physical device quality,
the full academic lifecycle, or every mobile/network-failure acceptance scenario.
