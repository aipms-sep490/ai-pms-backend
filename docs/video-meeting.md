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

Frontend integration details, capability handling, token handling, and the acceptance checklist are in [video-meeting-frontend-integration.md](video-meeting-frontend-integration.md).

Apply `db/changes/20261002_add_video_meetings.sql` before enabling `VideoMeeting__Enabled`. Run provider smoke tests only against an isolated database and LiveKit staging project. Recording, transcription, and AI minutes are not part of this MVP.
