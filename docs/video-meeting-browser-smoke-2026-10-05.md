# Video Meeting browser smoke test - 2026-10-05

## Result

The core two-person video flow works against real LiveKit Cloud from the current
frontend. Full video acceptance is still blocked by out-of-order presence handling.

- Frontend: `develop` at `1b365a8`, served at `http://localhost:5173`.
- Backend base: `origin/develop` at `dde3ceb`, plus the local credentialed-CORS fix
  on `fix/frontend-credentialed-cors`.
- Staging API: `https://api-staging.khaidz.com/api/v1`.
- Container image tested: `sha256:748041c7f9aa7a796fc22bb83549f1c790c0e946e2845f1eeaa7ee6b0a11414e`.
- No frontend source changes. Local Vite configuration is ignored by Git.
- No schema migration was needed.

## Authorized isolated fixture

The user authorized separate accounts and a separate test group in the shared
AI_PMS staging database. Only additive fixture records were created.

| Resource | Identifier |
| --- | --- |
| Leader | User 40002, `video.leader.20261005@aipms.test` |
| Member | User 40003, `video.member.20261005@aipms.test` |
| Team | 30002, `VIDEO-TEST-20261005` |
| Project | 30003, `[TEST] Video Meeting Integration` |
| Meeting | 40002, created through the frontend |
| Video sessions | 1 and 2, both ended |

Credentials are not stored in this document or in repository files. The project
was explicitly seeded in ACTIVE state for execution testing. This fixture does
not prove academic eligibility, registration, approval, or supervisor workflows.
An audit record marks it as a seeded execution fixture.

## Verified with two isolated Chrome contexts

- Both accounts log in with password authentication and load the test project.
- Leader creates a REMOTE / IN_APP_VIDEO meeting and invites the member.
- Start, join, and end return HTTP 200 against staging.
- Both contexts display two participants. WebRTC inbound statistics show received
  audio packets and decoded video frames in both directions.
- Microphone and camera toggles work.
- Screen sharing is received by the member as an additional video track.
- Member has no End Room control.
- Direct member start/end requests return 403; join after End Room returns 409.
- Member leaves without ending the host's room, then rejoins successfully.
- Host ends the room and provider cleanup disconnects the member.
- Restart after an ended session succeeds with a new session.
- Meeting remains SCHEDULED after End Room. Attendance is not overwritten:
  leader remains confirmed and member remains invited.
- Actual signed provider events are recorded and processed.
- Both cleanup jobs finish SUCCEEDED; the second succeeds on its first attempt.

Camera, microphone, and screen capture use Chrome test media. These checks prove
transport and UI integration, not real hardware quality. Abrupt network loss,
multi-device/mobile behavior, and the full academic lifecycle were not tested.

## Fix applied for this test

Frontend requests use `credentials: include`. The general backend CORS policy
previously omitted `Access-Control-Allow-Credentials`, which blocked login in the
browser even though the origin was allowed. The fix enables credentials only for
the existing explicit origin allowlist. Seven integration tests cover preflight,
actual responses, and denial of unlisted/lookalike/null origins.

An obsolete Windows API process on port 5080 was still consuming cleanup jobs
from the same database. The first cleanup failed with VIDEO_PROVIDER_REJECTED;
the staging container had not made that DeleteRoom call. The obsolete process
was stopped, only the test session's failed job was requeued, and the current
container completed cleanup. The subsequent test needed no manual retry.

At the time of the smoke test, the CORS fix was deployed to the local staging
container before publication. This branch includes the fix and its regression tests;
the pull request records subsequent CI and merge status.

## Remaining defect: presence ordering

`VideoProviderEventService.ApplyAsync` uses receipt/processing time and closes the
latest open user segment without matching `provider_connection_id`. It also creates
a new open segment for delayed join events after the video session has ended.

Observed SQL evidence (UTC):

- Session 1, member 40003: joined_at `12:20:29.3352201`, left_at
  `12:20:28.6327872`. A segment has a negative duration.
- Session 2 ends at `12:23:58.5899015`, but a delayed member join is recorded at
  `12:24:00.023405` and closed at `12:24:11.2225135`.

All involved events were marked PROCESSED, so signature acceptance alone does not
prove correct presence evidence. Do not use these aggregates as accurate attendance
duration or mark the full video contract complete.

Follow-up should serialize event application with the session, commit inbox and
projection atomically, correlate joins/leaves by provider connection SID, use
validated provider occurrence timestamps, and prevent delayed events from reopening
ended sessions. Add SQL tests for leave-before-join, delayed old-connection leave
after reconnect, duplicate/concurrent events, and join-after-room-finished.

## Automated checks

- Frontend production build succeeds (bundle-size warnings remain).
- 98 focused frontend meeting/video tests pass.
- 7 backend CORS integration tests pass with a Release warnings-as-errors build.
- Backend container builds and starts successfully; no destructive database tests
  were run against AI_PMS.

The local frontend is left running for manual testing. Start a new meeting or edit
the test meeting's time window before joining outside its scheduled period.
