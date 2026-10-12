# Tin: remaining adapter deltas after FE #57

Inspected FE origin/develop 49bae0a; backend closure PR #123. This is an integration
checklist, not a frontend implementation or a deployment claim. Recheck current FE
HEAD before changing code. Capabilities and 409 reconciliation already delivered
in #57 should be preserved, not rebuilt.

## AI

File: src/features/ai/ai-api.ts.

| Current assumption | Backend contract / change |
| --- | --- |
| Runs return an array | PagedResult<AiRunDto>; consume items/page/pageSize/totalCount as defined in OpenAPI |
| Detail /ai/runs/{id} | /projects/{projectId}/ai/runs/{id}; keep project context in navigation/query key |
| RISK/SUMMARY/CONTRIBUTION and outputJson | PROJECT_ASK, REPORT_SUMMARY, PROGRESS_ANALYSIS with typed answer/reportSummary/progressAnalysis; unknown kinds fail closed |
| createdAt/engine/sufficiency | startedAt/completedAt, modelVersion/configVersion, status/errorCode |
| GET creates risk history | GET progress-analysis is transient; use explicit POST /projects/{id}/ai/progress-analysis to record |
| Project-wide risk for every actor | MAJOR scope exposes classified task counts; null project metrics and unavailableMetrics must stay unknown |

History is actor scoped. Clear cached history on identity/project changes. Handle
AI_HISTORY_SCOPE_CHANGED by hiding the old result. A local deterministic engine
must not be labelled an external LLM. Do not generate a new run on page mount.

## Sprint and task board

Files: src/services/api/tasks.api.ts and SprintBacklogPanel.tsx.

- GET /projects/{id}/sprints is paged, not SprintDto[].
- POST /projects/{id}/sprints/{sprintId}/start with sprint concurrencyToken.
- POST /projects/{id}/sprints/{sprintId}/complete with sprint token and each
  unfinished task exactly once in carryover: taskId, targetSprintId (null backlog),
  taskConcurrencyToken. Do not use generic /sprints/{id}/status.
- PUT /tasks/{id}/board replaces sprintId/storyPoints/labels with task token;
  old proposed /sprint and /planning routes are not the release contract.
- GET /projects/{id}/task-board filters backlog/sprint/label/major/status before
  paging/count. Never compute project totals from the first 100 task rows.
- Use server burndown. A carried-over task remains unfinished, never auto-DONE.

## Chat

Files: src/features/chat/chat-api.ts and ChatAttachments.tsx.

Base: /api/v1/chat/conversations/{conversationId}.

| Current assumption | Backend contract |
| --- | --- |
| POST /attachments | POST /uploads, multipart file |
| attachmentFileIds | attachmentIds; body may be null for attachment-only message |
| GET /chat/attachments/{id}/download | GET .../messages/{messageId}/attachments/{attachmentId}/download |
| DELETE .../reactions/{emoji} | DELETE .../reactions?emoji=... |
| Reaction mine | reactedByMe |
| No mute adapter | PUT .../mute with mutedUntil (null to unmute); conversation returns caller preference |

Pass conversation/message IDs into attachment UI; no public URL or file-ID-only
fallback. Preserve stable clientMessageId on send retries, including attachment
IDs. Reactions return updated ChatMessageDto. Respect shouldNotify in realtime
events, deduplicate EventId and refresh state on reconnect. Mute preserves unread;
recall removes download access. Maximum 10 files/message, 10 MiB each.

## Remaining academic and browser acceptance

Audit scheme approval/stage/evaluator-slot editors, Admin period-policy entry,
COLD target/version downloads, weekly submitted history, meeting notes locks,
calendar and own published results against the current OpenAPI. No Admin academic
publication bypass or role-derived authority. UI for legacy/demo/unknown remains
distinct from official publication.

Per feature, record FE/BE SHA, enabled flag, real API errors/tokens, browser evidence,
DB/audit readback, keyboard/focus and 375/768/1024/1440 layouts. Use the isolated
environment runbook; API interception evidence from #57 is not real BE acceptance.
Only enable each flag after its adapter and real lifecycle pass. P2 flags stay off.
