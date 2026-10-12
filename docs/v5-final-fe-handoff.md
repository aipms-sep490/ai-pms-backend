# AI-PMS v5 P0/P1 backend handoff for Tin

Updated 2026-10-12. Backend implementation/isolated acceptance is delivered through
PR #121, merge `4eb34d6f29b62ec1f74db758068314120ef8470f`; shared migrations verified.
Release evidence and exact merge SHAs are maintained in
[the delivery register](v5-backend-implementation.md). This document does not mark
the frontend integrated or enable its feature flags. No frontend file was changed.

## Source of truth and compatibility

Closure follow-up: [analysis scope and full DB audit](v5-p0-p1-closure.md).
Explicit POST project `/ai/progress-analysis` returns runId; history adds typed
progressAnalysis with kind PROGRESS_ANALYSIS. GET remains read-only but enforces
AI scope. Mentor/participating staff receive MAJOR task counts, not project risk.
Milestone totals may be null; preserve unavailableMetrics/contextScope and do not
convert hidden metrics to zero. This follow-up requires matching deployment.

Use `/swagger/v1/swagger.json` on the matching backend build or the checked-in
[OpenAPI](contracts/cib-v4/openapi.json). All business routes are under `/api/v1`.
Keep existing project, meeting, tasks, notes, actions, result and chat aggregates.
The old DEPART Admin publication policy is superseded by v5: frozen lead department
publishes academic results; Admin alone cannot grade/publish. Semester, project
period and qualification-policy mutation remain Admin-only.

Do not infer authority from role badges or `200` on a read. Use server capabilities,
then let mutation revalidate. Unknown action/reason/provenance fails closed.
Refresh resource and capabilities on 409; never replay a publish with a fresh
token without showing the changed preview. Null/missing score is not zero.

## Endpoint to screen and rollout mapping

The rollout keys below describe independent FE gates to map to Tin's existing
feature configuration. They are not new environment-variable names and this PR
does not create/enable them. Keep each gate off until its adapter and browser
acceptance pass. Backend `Chat.Enabled`/`Chat.RealtimeEnabled` and existing video
configuration still apply; the other new APIs enforce resource authorization.

| Tickets / PR | Endpoint group | Screen / gate | DTO and acceptance evidence |
|---|---|---|---|
| BE-03/04/05/10/11, N12 / #112 | `projects/{id}/my-capabilities`, existing actions, scheme/components, evaluator assignments | Project governance / academic authority | ProjectCapabilitiesDto; [authority](v5-academic-authority.md); V5AcademicCapabilityTests, assignment race tests |
| BE-07/08 / #110 | task list `majorId`/`disciplineRole`; `evaluation-assignments/{id}/evidence` and protected files | Task filters, evaluator evidence / locked evidence | Keep `items[]`, map to `files[]` only in view adapter; [contract](v5-locked-evidence.md); LockedEvidenceSecurityTests and real upload acceptance |
| BE-09/17, N11 / #113 | `evaluation-schemes`, project/student result preview, publish and GET | Scheme editor, score sheets, own result / versioned scoring | SchemePolicyInput, ResultBreakdownDto; [contract](v5-scoring-results.md); E-demo-golden, PolicyEvaluationEndpointTests |
| N01 / #114 | `cold-report-templates`, project cold checklist/lock, cold submission files/submit/history | Major report checklist / COLD submission | ColdTemplateDto, ColdChecklistDto; [contract](v5-cold-submissions.md); ColdSubmissionEndpointTests, ColdAcceptanceJourneyTests |
| N02/03 / #115 | Existing evaluator draft/save/finalize, project cold progress | COLD scoring and progress / COLD evaluation | Stage separate from scope; [contract](v5-cold-evaluation.md); ColdEvaluationEndpointTests |
| N04 / #116 | Project `weekly-assessments`, `weeks`, `rubrics`, draft/submit/history | Primary weekly feedback, student's own history / weekly demo | WeeklyAssessmentDto; [contract](v5-weekly-assessments.md); WeeklyAssessmentEndpointTests |
| BE-12/13/14 / #117 | Evidence review, project checkpoints, committees/defense sessions | Evidence review, checkpoint and defense calendar / tracking | [contract](v5-academic-tracking.md); AcademicTrackingEndpointTests |
| BE-06, N05/06/07 / #118 | `calendar`, project `meeting-series`, meeting `collaboration`, `rsvp`, notes lock/history | Calendar and canonical Meeting Detail / meeting collaboration | MeetingCollaborationDto; [contract](v5-calendar-meeting-collaboration.md); CalendarV5EndpointTests and meeting collaboration tests |
| N08 / #119 | Project sprints/start/complete/burndown/task-board; task `board` | Task board/sprint detail / sprints | SprintDto, TaskDto board additions; [contract](v5-sprints-task-board.md); SprintEndpointTests |
| N09 / #120 | Chat uploads, messages with attachmentIds, private download, reactions, mute; SignalR | Existing chat composer/conversation / chat collaboration | ChatMessageDto, ChatConversationDto, ChatEvent; [contract](v5-chat-collaboration.md); ChatCollaborationTests and SignalR tests |
| BE-15, N10 / #121 | Project AI ask/report summary/runs; `dashboards/me?semesterId=` | AI panel/history and actor home / AI history, actor dashboard | AiRunDto, ActorDashboardDto; [contract](v5-ai-history-dashboard.md); AiHistoryTests |

All schema changes and shared rollout reports are mapped in the delivery register.
Phase 12 adds only `20261012_add_ai_runs.sql`; there is no dashboard table or
frontend migration. Build matching application/schema versions together.

## Behaviors the adapters must preserve

- Scheme PUBLISHED means immutable, not academically approved. Show `isDemo`,
  `approvalStatus`, `readiness`, `canPublish` and blockers separately. Demo and
  weekly scores must never be presented as official published results.
- Stage ON_GOING/COLD/DEFENSE and scope COMMON/MAJOR_SPECIFIC/INDIVIDUAL are separate
  fields. Do not derive one from evaluator type or reuse a report from another major.
- Show exact locked file version, declared major/student and private download action.
  Never use latest-file fallback, uploader-derived ownership or raw storage paths.
- COLD templates belong to a major/period. IT examples are optional examples;
  Marketing/Design use their configured checklist. Submit and lock use latest tokens.
- Weekly dates come from the server's semester/timezone calculation. A correction
  after submit is a new version with reason; old history stays visible and immutable.
- Gate/checkpoint overdue signals do not fail the project automatically. Committee
  membership does not grant evaluation rights; preserve explicit assignments.
- Meeting RSVP is separate from attendance; Leave, End Room and Complete Meeting
  remain separate. A locked note blocks note/decision/definition edits while action
  progress still uses its authorized endpoint. Show actual email delivery status.
- Sprint completion requires an explicit destination/token for every unfinished
  task. Do not mark carryover DONE. Use server burndown, not recreated past snapshots.
- Chat IDs remain decimal strings. Upload at most ten files, 10 MiB each; send the
  returned IDs with a stable clientMessageId. Retry uses the same payload/ID. Recall
  removes media access. Mute only changes notifications, not membership or unread.
- AI requests are optional. Show FALLBACK/INSUFFICIENT_EVIDENCE/FAILED/RUNNING honestly.
  Current provider is a local deterministic adapter, not a deployed external LLM.
  AI_HISTORY_SCOPE_CHANGED hides the old answer. Do not cache histories across actors.
- Mentor/participating staff use scoped dashboards/me. Whole-project legacy cards
  are restricted to Primary/lead; an empty legacy list is not a loading failure.

## Error and token handling

| Response | FE behavior |
|---|---|
| 400 | Show field/query validation; reset invalid cursor and fetch correct bounded range |
| 401 | Existing login/refresh flow; do not retry mutation indefinitely |
| 403 | Remove unauthorized action/data; refetch capabilities after assignment changes |
| 404 | Missing or inaccessible resource/file; no fallback to another project's endpoint |
| 409 | Stale state/token, locked resource, incompatible assignment or readiness blocker; reload and ask user to review |
| 413 / 422 | Upload/export size or semantic validation where declared in OpenAPI; show supplied safe reason |
| 429 | Respect throttling and retry delay; avoid AI history polling loops |
| 503 | Existing provider outage contract; keep academic workflow usable |

Treat concurrencyToken as opaque, retaining its exact string/UUID representation
from the owning DTO. `expectedLatestVersion`, RSVP token, task token, draft token,
scheme token and result confirmation token are not interchangeable. Each phase
contract documents the matching request; do not send a project token everywhere.

## Acceptance handoff

[A-F backend evidence](v5-acceptance-matrix.md) distinguishes real HTTP/SQL paths
from synthetic inputs, in-memory storage and provider failure injection. Test JWTs
exercise persisted resource rules; they are not Google-login acceptance. Redacted
artifacts contain no credentials, student records or raw query/prompt text.

Tin still owns browser execution for IT, Marketing and three-major journeys;
revoked permissions/stale tokens; COLD file upload/download; weekly history;
meeting lock/RSVP; sprint carryover; chat reconnect/recall/mute; AI fallback.
Verify keyboard/focus and 375/768/1024/1440 layouts per enabled gate. Record the
frontend SHA, backend SHA, environment and browser evidence before declaring
`P0_P1_INTEGRATED_READY`. Backend CI cannot certify those checks.
