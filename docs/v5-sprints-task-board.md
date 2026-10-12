# v5 phase 10: sprints and task board

Status: DELIVERED. PR #119 merged as 8d4982adba9fd36a6b7655d364465d676371d982.
CI passed 1055 unit and 1228 integration tests, zero skips, plus isolated bootstrap
and rerun. Shared migration ran twice and verified three tables, three task columns,
six checks, seven FKs and seven indexes; business counts unchanged. Evidence:
`docs/validation/v5-sprints-rollout-2026-10-12.json`. BE-N08; no frontend changes.

## Contract

| Endpoint | Request/response |
| --- | --- |
| GET `/api/v1/projects/{projectId}/sprints?page=&pageSize=` | PagedResult<SprintDto> |
| POST `/api/v1/projects/{projectId}/sprints` | SaveSprintRequest -> SprintDto |
| PUT `/api/v1/projects/{projectId}/sprints/{id}` | SaveSprintRequest with concurrencyToken -> SprintDto |
| POST `/api/v1/projects/{projectId}/sprints/{id}/start` | SprintTokenRequest -> SprintDto |
| POST `/api/v1/projects/{projectId}/sprints/{id}/complete` | CompleteSprintRequest -> SprintDto |
| GET `/api/v1/projects/{projectId}/sprints/{id}/burndown` | SprintBurndownDto |
| PUT `/api/v1/tasks/{taskId}/board` | SaveTaskBoardRequest -> existing TaskDto with additive board fields |
| GET `/api/v1/projects/{projectId}/task-board` | PagedResult<TaskDto>; sprintId, backlog, label, majorId, status, page, pageSize |

Existing task APIs remain. TaskDto adds sprintId, storyPoints, taskCode and labels.
Use the new board endpoint for combined board filters; existing task list filters
retain their contract. Filter by source on the server before paging/count; use
backlog=true for unassigned tasks and omit it when supplying sprintId. Major
classification uses EXISTS so multiple disciplines do not duplicate a task.
Page sizes are 1..100; labels are up to ten unique lowercase ASCII slugs of 1..32
characters. Points: null (unestimated) or 1/2/3/5/8/13. Null never means estimated 0.

Create example:
```json
{"name":"Sprint 1","startAt":"2026-11-01T02:00:00Z","endAt":"2026-11-15T02:00:00Z"}
```

Board update replaces the task's board metadata, leaving milestone and task
business state intact:
```json
{"sprintId":123,"storyPoints":5,"labels":["api","review"],"concurrencyToken":"current-task-token"}
```

Completion requires every unfinished task exactly once (excluding DONE/CANCELLED):
```json
{"concurrencyToken":"current-sprint-token","carryover":[
  {"taskId":456,"targetSprintId":null,"taskConcurrencyToken":"current-task-token"},
  {"taskId":457,"targetSprintId":124,"taskConcurrencyToken":"current-task-token"}
]}
```
Null target moves to backlog; a target must be a PLANNED sprint in the same project.
Missing/extra/duplicate tasks, stale tokens or foreign targets reject the whole
transaction. No task is automatically marked DONE. Completed task membership stays
in the completed sprint; task lifecycle can continue through its existing API,
but the completed sprint history remains unchanged. Sprint scope/estimates cannot
be edited after completion; history-bearing tasks cannot be deleted.

## Authority and lifecycle

Use frozen registration scope and active accounts. Current student leader or
PRIMARY manages; Mentor is never promoted to Primary authority. Member reads
project board; Mentor/participating staff reads only classified task majors in
scope. Lead staff reads project board but is not a sprint manager. Admin/evaluator
role alone grants no sprint access. Missing frozen scope fails closed.
Burndown requires whole-project read authority, avoiding totals outside an actor's
major. Scoped mentors still read their filtered board and sprint schedule metadata.

All writes lock team then project, recheck authority/ACTIVE state and require the
current resource token. Capability flags use the same manager predicate. One
ACTIVE sprint per project is enforced in code and a filtered unique index.
PLANNED can be edited; start rejects an elapsed end date. Planned ranges are at
most 90 days; starting early is allowed explicitly. Complete only accepts ACTIVE.
Max 500 tasks per sprint; history query max 10000 events (400 rather than truncation).
Mutation, history and audit share a transaction. No new notification is required.

400 invalid request/filter/points/history bound; 401 missing login; 403 wrong scope
or manager authority; 404 unknown project/task/sprint; 409 stale token, archived
project, active sprint conflict or invalid carryover. Refresh affected tokens on
409; do not automatically retry a mutation based on stale FE role/cached state.

## Historical chart and database

Start snapshots the current task membership/status/estimate as BASELINE at actual
start. It does not fabricate a history before that instant. SCOPE, ESTIMATE and
STATUS events are appended on real changes, including the existing task status
route and explicit carryover. Burndown folds events, never current task state;
scopePoints and unestimatedTasks explain scope removal and incomplete estimation.
A zero remaining total after moving work out is not a claim that tasks were done.
Same timestamp events are folded together; no ideal line/past daily points are
invented. A planned/empty sprint may have no chart points.

Migration `20261012_add_sprints.sql` adds sprints, task_board_labels,
sprint_task_events, nullable task sprint/points and persisted computed taskCode.
TASK-{identity} is globally unique (therefore project-unique), immutable, and IDs
are not reseeded/reused. Existing tasks receive a deterministic display identifier;
no historical business scope or estimates are inferred. Existing milestone remains
the task's project owner. Normal API cross-project milestone moves remain denied.
Bootstrap/manifest/model/readiness are updated together; no EF migration.

Validation: SprintEndpointTests exercises API create/start/estimate/status/carryover,
real history, immutable completed chart, major scope/revocation, concurrent start
and estimate, token conflict, archived write denial, audit rollback and pagination.
SchemaParityTests covers fresh/upgrade/rerun including indexes/FKs. SQL and lifecycle
tests use owned isolated DBs. Shared rollout follows merge/green CI only.

Local validation 2026-10-12: Release warnings-as-errors passed, 1055 unit tests
passed, full integration suite passed 1227 tests before the final constraint test;
the final focused sprint/schema/OpenAPI run passed 16 tests, including all eight
sprint tests. Isolated bootstrap/readiness/rerun passed. CI runs the complete final
source again before merge.

Tin maps SprintDto capabilities/token to controls, board fields to cards and
explicit carryover to the completion dialog. Do not conflate sprint with milestone,
unestimated with zero, or scope removal with completed work. This release does not
implement Gantt, automatic task completion or a general project scheduling engine.
