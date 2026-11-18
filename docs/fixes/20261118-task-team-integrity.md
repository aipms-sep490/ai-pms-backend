# Task integrity fixes — 18 November 2026

Source: the user's `Looxi18_11.docx`, existing project execution authority,
task state machine and contribution rules. The frontend companion is on
`fix/nov18-task-team-integrity`.

## Resulting behavior

| Issue | Resolution | Regression evidence |
| --- | --- | --- |
| Responsibilities cannot be saved after changing team academic scope | The frontend refreshes the shared token, preserves unsaved text on conflict, and reads back after saving | `StructuredResponsibilitiesPanel.test.tsx` and the frontend browser fixture |
| Task uploads have no deletion control | File reads expose actor-specific `canDelete`; the existing DELETE endpoint remains authoritative | `DeliverableHandlerTests`, `TaskEvidenceCommentEndpointTests` and frontend deletion tests |
| Reopening a completed predecessor leaves accepted dependent results unchecked | Persist pending impact reviews and notifications for direct Finish-to-Start successors currently DONE; keep successor state and completion date until an explicit review | `TaskDependencyImpactTests`, `TaskDependencyImpactEndpointTests` |
| Assignees can change during/after acceptance | Reject assignee writes for IN_REVIEW, DONE and CANCELLED; snapshot assignees in status and assignment audit records | Unit tests, SQL endpoint tests and frontend detail/modal tests |

## Authority and lifecycle

- A current active student leader or active PRIMARY lecturer may reopen a
  completed task, with a reason. Restoring a CANCELLED task that previously
  reached DONE has the same requirement and creates a fresh impact review.
- Existing reviews remain history. Completing the predecessor again never
  silently clears a pending review. No automatic status cascade occurs.
- The review is available while the project is ACTIVE and the predecessor is
  DONE or CANCELLED. A normal unfinished predecessor blocks the decision.
- `CONFIRMED_VALID` records the comparison and preserves the successor's state
  and original completion date. It does not complete an unfinished task.
- `REWORK_REQUIRED` moves a DONE successor to IN_PROGRESS and creates impact
  reviews for its own DONE successors. For TODO/IN_PROGRESS/BLOCKED it records
  the required work without another transition. IN_REVIEW/CANCELLED reject this
  decision until the relevant workflow is handled.
- A task with a pending impact cannot be accepted as DONE. Task edits and audit
  writes use the existing execution transaction and concurrency-token pipeline.
- Reads preserve project and major scope, including the predecessor's title
  and reason. Scoped mentors cannot decide reviews or see other majors' tasks.
- Contribution formulas are unchanged. Locked assignees and unchanged timestamps
  prevent late assignment from rewriting an earlier completion. Historical data
  predating this change is not retroactively attributed or reconstructed.
- Task attachments are editable only in TODO/IN_PROGRESS/BLOCKED. Files referenced
  by protected records can still be rejected by the existing deletion policy.

## Contract

- `GET /api/v1/tasks/{id}/dependency-impacts`: current reviews, history and
  actor/state-specific `canReview`, `canResolve` and `blockedReason`.
- `POST /api/v1/tasks/{id}/dependency-impacts/{impactId}/resolve`: decision,
  required reason and current task `concurrencyToken`; returns the current task.
- `ProjectFileDto.canDelete`: per-file permission computed by the server.
- The generated OpenAPI artifact contains both new routes and DTO fields.

## Release order

1. Back up and verify the explicitly selected deployment database.
2. Apply `db/changes/20261118_task_dependency_impacts.sql` through
   `scripts/apply-academic-schema-release.ps1`. This script also verifies target
   identity, takes a checked backup and tracks migration checksums.
3. Run `scripts/verify-academic-schema-readiness.ps1` and
   `scripts/test-schema-readiness.ps1` against that same target.
4. Release this backend before the companion frontend, then verify with the
   intended project roles. A frontend calling these routes requires this backend.

The migration is included in the isolated test manifest and fresh schema.
Upgrade, repeat execution, missing-object detection and readiness were exercised
on an owned temporary SQL Server database. No shared deployment data was migrated
during development. Automated HTTP/SQL tests and browser contract fixtures do
not constitute authenticated production acceptance testing.
