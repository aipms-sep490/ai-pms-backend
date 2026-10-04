# LiveKit webhook verification - 2026-10-04

## Deployed change

Endpoint: `https://api-staging.khaidz.com/api/v1/integrations/video/livekit/webhook`.
Container: `aipms-api`; release image tag: `aipms-api:livekit-webhook-20261004`.
Image ID: `sha256:85e6bb41f43bb29679ef7e26e7438ad5ffe34bc791f31f19e057789bef8b57fb`.

- Accept LiveKit's compact Authorization JWT, retaining Bearer compatibility.
- Validate HS256 signature, configured issuer, required expiration and not-before.
- Compare the signed Base64 SHA-256 checksum of the original body in constant time.
- Explicitly align System.IdentityModel.Tokens.Jwt to 7.7.3 with the existing
  IdentityModel packages. Validation tests exposed a transitive 6.35.0/7.7.3
  runtime mismatch before this alignment.
- Use the server-only `roomCreate` grant for DeleteRoom, matching LiveKit's SDK.
  The first provider smoke exposed HTTP 401 with the previous roomAdmin grant.
  Participant tokens were not given management grants.
- No database migration is required.

## Automated validation

- Release Linux Docker publish succeeded with repository warnings-as-errors.
- All 988 unit tests passed (20 added for webhook and DeleteRoom behavior).
- All 9 trusted-proxy tests passed.
- Coverage includes raw/Bearer authorization, wrong secret/issuer, expired/future
  tokens, missing expiration/hash, wrong algorithm, legacy hex hash, case mutation,
  changed body bytes, malformed authorization, disabled video, rejection before
  persistence and duplicate-event suppression using the in-memory test provider.
- The public endpoint rejected an unsigned POST with HTTP 401.

## Authentic provider smoke

The user authorized testing through the deployed backend using the shared AI_PMS
database. The script created isolated provider rooms only; no academic Meeting,
participant binding, attendance record or project was created or edited.
The configured LiveKit Cloud webhook delivered the events; these were not
locally fabricated signed webhook requests.

Final successful run:

- Room: `vm-webhook-smoke-805df828ecdf4be49fe1435b7504e95c`.
- Provider room SID: `RM_hMfN9FH9YHpN`.
- Created, explicitly deleted, then ListRooms confirmed it no longer existed.
- API logs show HTTP 204 for both incoming callbacks.

| Provider event ID | Event | Received UTC | Received Asia/Saigon | DB status |
| --- | --- | --- | --- | --- |
| EV_vX39VgamsaWP | room_started | 2026-10-04 07:25:35 | 2026-10-04 14:25:35 | PROCESSED |
| EV_ptYUfYwbGjtw | room_finished | 2026-10-04 07:25:42 | 2026-10-04 14:25:42 | PROCESSED |

The preliminary run also recorded events EV_oEs3Y3QmghYm and EV_vGETzeKKCbGj.
Its first explicit deletion was rejected by the old grant; cleanup was retried
with the corrected grant and ListRooms verified absence. Neither smoke room
remains. The four webhook metadata rows were retained as evidence in AI_PMS.
They have no meeting_video_session_id because the rooms intentionally did not
belong to a business meeting.

After deployment: container healthy, zero restarts, public system endpoint HTTP
200 and public Swagger HTTP 200. Previous image remains tagged
`aipms-api:before-webhook-fix-20261004` for operational rollback.

## Acceptance boundary

PASS: public routing, actual LiveKit webhook signatures, room start/end callbacks,
event metadata persistence and test-room cleanup.

NOT VERIFIED by this run: user join/leave, camera/microphone/screen sharing,
reconnect, mapping participant events to presence segments, out-of-order events,
concurrent SQL idempotency or the complete AI-PMS meeting lifecycle. The unit
duplicate test is not evidence of SQL concurrency safety. Continue joint FE/video
acceptance separately; do not label the entire Video Meeting MVP complete based
on this provider-only smoke.

To repeat intentionally from WSL (creates real provider events):

```bash
python3 scripts/smoke-livekit-webhook.py \
  --config /home/khaine161/.config/aipms/appsettings.Staging.json --execute
```

Do not publish the private configuration, Authorization headers or join tokens.
