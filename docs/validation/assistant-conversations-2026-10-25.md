# Assistant conversations: backend delivery

Branch: `feat/assistant-conversations`, baseline `origin/develop` at `3c44de3`.
Explicit dependency: fast-forwarded local `fix/assistant-general-conversation` through `6746e89` (general chat, uploads and export). Neither dependency nor this task is merged by this work.

## Delivered

- Persistent conversations owned by active actor, department and optional authorized project. Each ask rechecks access; history, export and citation endpoints also recheck access. General conversation history is separate from project history.
- History uses keyset pagination (20 conversations or turns). Last eight completed turns, maximum 16,000 characters, are supplied as untrusted dialogue. Earlier official/project answers are omitted from model memory and require current retrieval. Uploaded binary content is not replayed.
- Expected revision + transactional compare-and-swap prevents parallel turns from overwriting each other. Run, turn, revision and audit commit together. Cancellation before commit leaves no partial answer. A response lost after commit must be reconciled by reloading history.
- Real provider streaming, including images, uses NDJSON to the browser. Preview text is provisional; final validated response replaces it. OpenAI-compatible `finish_reason=stop` supports 9Router streams without `[DONE]`. Truncated streams without a completion marker fall back.
- Correlation ID is allocated before streaming. Audit can read it after response headers are immutable. Logs contain error type/stack only, no prompts, keys or provider response bodies.
- Citation opening returns the exact excerpt/version recorded with the answer; it does not expose arbitrary document IDs or whole source files.
- Explicit TASK/MEETING draft modes return bounded title/description only. No mutation tool, IDs, dates, assignees or workflow decisions are executed by the assistant. FE hands drafts to existing forms for user selection and confirmation.
- Deletion hides the conversation and removes query turns. Existing answer/evidence audit records remain; deleted conversation sources/exports are inaccessible through the assistant.

## API

| Method | Route under `/api/v1/academic/assistant` | Behavior |
|---|---|---|
| POST | `/conversations` | Create `{projectId?, scope}` |
| GET | `/conversations?projectId=&beforeId=` | Owner/scope history, 20 rows |
| GET | `/conversations/{id}?beforeTurnId=` | Conversation, revision, 20 turns and `hasMore` |
| DELETE | `/conversations/{id}` | Hide conversation, remove query turns, audit |
| POST | `/stream` | Multipart query/scope/projectId/conversationId/expectedRevision/pageContext/draftKind/files |
| GET | `/runs/{id}/citations/{index}` | Exact cited snapshot, zero-based index |

Existing JSON ask, multipart ask-with-files and TXT/MD export remain compatible. Ask DTO now accepts optional conversation ID, expected revision, page context and draft kind. Stale revision returns 409 before streaming; an error after headers start is an `error` event. Clients must accept only `completed` as a saved final answer and must not auto-retry a partially received ask.

NDJSON events: `preview` with text; `completed` with response; `error` with a generic recovery message. Response adds nullable conversationId, revision and draft. Complete OpenAPI: `docs/contracts/cib-v4/openapi.json`.

## SQL and deployment

- Additive migration `db/changes/20261025_add_assistant_conversations.sql`, mirrored in bootstrap and migration manifest. Adds `assistant_conversations` and `assistant_conversation_turns` with named trusted constraints and unique conversation/revision.
- Shared AI_PMS backup verified with CHECKSUM: `/var/opt/mssql/backup/AI_PMS_before_conversations_20261025_160835.bak`.
- Migration applied to AI_PMS: both tables present, zero disabled/untrusted new foreign keys. No project/student/grade lifecycle data changed by this migration.
- Staging image `aipms-api:assistant-conversations-20261025`, final image prefix `877b6859ef1c`. Previous image `assistant-files-20261024` retained. Runtime-only image built from that base with `dotnet publish -r linux-x64 --self-contained false`; no private appsettings copied into image.
- Initial Docker SDK build was interrupted when WSL stopped services. Published on Windows and packaged Linux output to avoid another SDK build inside WSL. WSL background service lifetime also caused intermittent login failures during verification. FE now runs through an active WSL-launched Windows Node process; keep that development process alive while testing.
- Rollback: select previous image in ignored compose env and recreate only api. Keep new tables/data. Do not run destructive lifecycle tests on AI_PMS.

## Evidence

- Release warnings-as-errors passed.
- 29 integration/SQL/OpenAPI tests passed: ownership, revision, parallel replies, cancellation, audit rollback, exact source history, draft non-mutation, fresh/upgrade/rerun schema parity, actual HTTP streaming/correlation.
- 7 provider tests passed after adding 9Router finish marker; attachment suite previously passed (8).
- Real Edge browser, 9Router model `cx/gpt-6-luna`: runs 110005-110008, conversation 90002, revisions 1-4. Follow-up recalled synthetic marker; reload restored history; TASK/MEETING drafts returned; stop and mobile passed; zero browser page errors.
- DB readback: all four runs COMPLETED, each has exactly one `ACADEMIC_ASSISTANT_ASKED` audit.
- Source run 110012: GROUNDED/COMPLETED, exact `[DEMO] Hướng dẫn rubric và công bố kết quả`, version DEMO-2026.1; protected source viewer opened, desktop/mobile passed.
- Local evidence: `../outputs/assistant-conversations-20261025/` including checks.json and screenshots. Test account credentials remain in private DPAPI storage.

## Limits

No live web/weather, image generation, original upload library, or autonomous business mutations are added. History is bounded model context, not unlimited memory. Source preview is an excerpt, not full PDF navigation. Project action submission keeps existing permissions and requires explicit save in the existing form. Real browser draft generation verified; real creation of academic tasks/meetings was not performed on shared data in this task.
