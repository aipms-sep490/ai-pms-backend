# Video Meeting MVP

Video Meeting attaches a technical LiveKit room to the existing Meeting aggregate. Meeting status remains `SCHEDULED`, `COMPLETED`, or `CANCELLED`; video sessions use `CREATED`, `LIVE`, `ENDED`, and `FAILED`.

## Configuration

Keep these values in User Secrets or a secret manager. Do not commit the LiveKit secret.

```ini
VideoMeeting__Enabled=false
VideoMeeting__Provider=LIVEKIT
VideoMeeting__ServerUrl=wss://your-livekit-host
VideoMeeting__ApiKey=...
VideoMeeting__ApiSecret=...
VideoMeeting__JoinTokenTtlSeconds=300
VideoMeeting__JoinOpenBeforeMinutes=0
VideoMeeting__JoinCloseAfterMinutes=0
```

When both join offsets are zero, the join window is `StartAt` through `EndAt`. Provider credentials are required before enabling the feature.

## API

```text
POST /api/v1/meetings/{id}/video/start
POST /api/v1/meetings/{id}/video/join
POST /api/v1/meetings/{id}/video/end
GET  /api/v1/meetings/{id}/video/session
GET  /api/v1/meetings/{id}/video/presence
POST /api/v1/integrations/video/livekit/webhook
```

Only active project participants can join. Meeting creator, current leader, and active primary supervisor can manage the room when they are in project scope. Admin role alone does not grant media access. Join credentials are short-lived and are never persisted or logged.

Presence is evidence. Existing Meeting attendance remains the official attendance workflow. Ending a room never completes a Meeting.

## LiveKit webhook authentication

The anonymous webhook endpoint authenticates each request using LiveKit's signed
JWT in `Authorization`. The raw compact JWT is supported; `Bearer` remains
accepted for compatibility. Validation requires HS256, the configured API key as
issuer, a valid signature, expiration and not-before (10 seconds of clock
tolerance). The `sha256` claim must match standard Base64 SHA-256 of the exact
UTF-8 body, not a reserialized JSON document or a hex digest. Invalid signatures
return 401 before any event persistence. The database's audit payload hash remains
hex; this is separate from the provider's signed claim.

Room creation and deletion use LiveKit's server-side `roomCreate` grant. This
grant is never added to participant join tokens. Room-specific `roomAdmin` alone
does not authorize LiveKit `DeleteRoom`.

Provider-only smoke testing can use `scripts/smoke-livekit-webhook.py` with an
explicit private config path and `--execute`. It creates and deletes one uniquely
named room, verifies cleanup, and does not create an academic Meeting. A failed
cleanup can be retried with `--cleanup-room` and that exact smoke room name.
Configured webhooks still write event metadata in the target backend database;
use an isolated backend by default, or explicitly authorize shared-database smoke.

Protocol references: [LiveKit WebhookReceiver](https://github.com/livekit/node-sdks/blob/main/packages/livekit-server-sdk/src/WebhookReceiver.ts)
and [RoomServiceClient](https://github.com/livekit/node-sdks/blob/main/packages/livekit-server-sdk/src/RoomServiceClient.ts).

Frontend integration details, capability handling, token handling, and the acceptance checklist are in [video-meeting-frontend-integration.md](video-meeting-frontend-integration.md).

Apply `db/changes/20261002_add_video_meetings.sql` before enabling `VideoMeeting__Enabled`. Run provider smoke tests only against an isolated database and LiveKit staging project. Recording, transcription, and AI minutes are not part of this MVP.
