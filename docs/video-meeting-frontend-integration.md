# Video Meeting Frontend Integration

This document describes the frontend contract for the backend Video Meeting MVP. The backend owns authorization, provider capabilities, and the LiveKit credential. The frontend must not infer permissions from a role or construct a provider token.

## Meeting scheduling

The existing meeting create and update endpoints accept these optional fields:

```json
{
  "meetingDeliveryMode": "ONSITE | REMOTE | HYBRID",
  "videoChannel": "NONE | EXTERNAL_LINK | IN_APP_VIDEO",
  "location": "Room 204",
  "onlineUrl": "https://example.test/room"
}
```

Valid combinations are:

| Delivery mode | Channel | Required fields |
| --- | --- | --- |
| `ONSITE` | `NONE` | `location` |
| `REMOTE` | `EXTERNAL_LINK` | HTTPS `onlineUrl` |
| `REMOTE` | `IN_APP_VIDEO` | none |
| `HYBRID` | `EXTERNAL_LINK` | `location`, HTTPS `onlineUrl` |
| `HYBRID` | `IN_APP_VIDEO` | `location` |

`NONE` does not create a video session. Existing meetings that omit the new fields remain valid and are returned with the legacy-derived mode/channel.

## Capability preflight

Call this endpoint when opening Meeting Detail and after a mutation:

```text
GET /api/v1/meetings/{meetingId}/video/session
```

The response contains `capabilities.canStart`, `capabilities.canJoin`, `capabilities.canEnd`, `capabilities.canRecord`, `joinWindow`, and `denialReasons`. Treat this response as authoritative and fail closed when a capability is false. `canRecord` is always `false` in this MVP.

Useful denial codes include `VIDEO_NOT_ENABLED`, `PROJECT_NOT_ACTIVE`, `MEETING_NOT_SCHEDULED`, `MEETING_PARTICIPANT_REQUIRED`, `VIDEO_START_FORBIDDEN`, `VIDEO_SESSION_NOT_STARTED`, `VIDEO_SESSION_ENDED`, `VIDEO_JOIN_TOO_EARLY`, and `VIDEO_JOIN_WINDOW_CLOSED`.

## Start, join, leave, and end

Only call start after an explicit host action:

```text
POST /api/v1/meetings/{meetingId}/video/start
```

The backend creates the provider room and returns the current session. A retry is safe; if an active session already exists, the current session is returned.

Join is also an explicit user action:

```text
POST /api/v1/meetings/{meetingId}/video/join
```

The response contains `serverUrl`, `accessToken`, `participantIdentity`, `expiresAt`, and media `capabilities`. Pass the token only to the LiveKit SDK in memory. Never place it in localStorage, sessionStorage, a URL, application logs, analytics events, or API payloads. The token is short lived and must not be refreshed by calling the provider directly; request a new backend credential after the current one expires, subject to the join window.

Leaving a room is a client/provider action and does not call the backend `end` endpoint. End Room is a separate explicit host action:

```text
POST /api/v1/meetings/{meetingId}/video/end
```

After end, the session cannot issue new join credentials. Completing or cancelling the Meeting is still done through the existing Meeting lifecycle endpoint and is separate from ending the video room.

## LiveKit client rules

- Keep LiveKit code in a meeting-video feature/provider module.
- Use the returned `serverUrl` and `accessToken`; do not use the API key or API secret in the browser.
- Request camera/microphone permission only after Join is pressed.
- Support camera-off and audio-only states; screen sharing is enabled only when returned in capabilities.
- Display provider errors separately from backend errors.
- Treat reconnects as normal SDK state transitions and keep the participant list synchronized with the SDK.
- Do not grant moderator controls from frontend role names. Use the returned `capabilities`.

## Presence evidence

Presence is evidence and does not replace official Meeting attendance. Read it with:

```text
GET /api/v1/meetings/{meetingId}/video/presence
```

The response aggregates each participant's `firstJoinedAt`, `lastLeftAt`, `totalConnectedSeconds`, and `connectionCount`. Render it as read-only evidence alongside the existing attendance workflow.

## Error handling

Preserve the backend problem response and use its stable error code. The important cases are:

| Code | Frontend behavior |
| --- | --- |
| `VIDEO_NOT_ENABLED` | Show video unavailable/configuration state. |
| `PROJECT_NOT_ACTIVE` / `MEETING_NOT_SCHEDULED` | Disable video actions and refresh Meeting state. |
| `MEETING_PARTICIPANT_REQUIRED` | Show that the user must be added to the Meeting. |
| `VIDEO_JOIN_TOO_EARLY` / `VIDEO_JOIN_WINDOW_CLOSED` | Show the returned join window. |
| `VIDEO_SESSION_ENDED` / `VIDEO_SESSION_FAILED` | Refresh session; offer restart only when `canStart` is true. |
| `VIDEO_PROVIDER_UNAVAILABLE` / `VIDEO_PROVIDER_TIMEOUT` | Show a retry action without changing Meeting lifecycle state. |
| `401` | Refresh the AI-PMS session through the existing auth flow. |
| `403` / `404` | Show an access/not-found state without exposing cross-project details. |
| `409` | Refresh the session because another host or request changed its state. |

## Suggested UI flow

1. Load Meeting Detail and call the capability endpoint.
2. Render the delivery mode and video channel from the Meeting response.
3. Show Start only when `canStart` is true; show Join only when `canJoin` is true.
4. On Start, call the backend and then join with the returned session state.
5. On Join, call the backend, initialize LiveKit with the returned credential, and keep the credential in memory.
6. On Leave, disconnect from LiveKit only.
7. On End, ask for confirmation, call the backend, disconnect, and refresh capabilities.
8. Keep Meeting complete/cancel actions in their existing UI and refresh the video capability after either action.

## Local and staging configuration

The frontend needs only the normal AI-PMS API base URL. It must not receive LiveKit API credentials. The backend `VideoMeeting` feature must be enabled and configured with the staging provider before testing. Use a project with `IN_APP_VIDEO`, an `ACTIVE` project, a `SCHEDULED` Meeting, and users present in `MeetingParticipant`.

## Acceptance checklist

- Create and edit all five valid delivery/channel combinations.
- Verify legacy Meetings remain editable.
- Verify capability buttons are disabled when a denial code is returned.
- Verify non-participants cannot join.
- Verify host Start, participant Join, Leave, and host End.
- Verify camera/microphone denial and provider reconnect states.
- Verify the token is absent from browser storage, URLs, logs, and analytics.
- Verify completing/cancelling the Meeting blocks a new join while preserving Meeting history and notes.
- Verify presence evidence is read-only and does not alter official attendance.
