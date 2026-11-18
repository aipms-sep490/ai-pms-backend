# PR #150 — business validation and merge review

## Source and issue

User requested review and merge of backend PR #150 into `develop`.
Reviewed head `0df68a4ecae58fe1859f6aeb70cda05cd17d000d` already includes
`develop` at `54fdec3ab89fdcf72d1037b31feaecd19a7c6ff1`.
The original CI failed because runtime OpenAPI contains the new milestone dependency
routes but the committed artifact did not. Review also found a missing upgrade script,
SQL lock exceptions being ignored, incomplete one-sided milestone date validation,
UTC/calendar date mismatch, and predecessor deletion resulting in a foreign-key error.

## Changes

- Regenerate committed OpenAPI from the offline API host; retain the zero-write drift assertion.
- Add the idempotent `20261118_milestone_dependencies.sql` migration to the isolated bootstrap manifest.
  Schema and migration enforce the currently supported `FINISH_TO_START` dependency type.
- Require a real SQL transaction for application locks and propagate SQL failures.
- Rotate milestone tokens on dependency changes; return a business conflict when deleting a referenced predecessor.
- Validate either milestone date against both execution boundaries. Compare calendar dates
  using Vietnam time, matching the execution UI, while task/period instants remain UTC.
- On execution-period edits, check projects resolved to that period, rather than every
  project belonging to the semester.
- Preserve reporting history while allowing full-form clients to resend unchanged values.

## Verification

- Build in Release with warnings treated as errors.
- Full unit suite, including one-sided dates, Vietnam midnight boundaries, and unrelated periods.
- SQL endpoint tests cover token rotation/stale writes, cycle rejection, unauthorized writes,
  concurrent opposite edges, protected deletion and removal.
- SQL upgrade test starts with a legacy milestones table, runs migration twice, preserves
  an existing dependency and rejects self, duplicate, unsupported type and missing FK.
- Existing reporting concurrency test verifies submitted report dates/deadline/policy cannot
  change and an identical full-form payload remains valid.
- Full integration/SQL suite and GitHub PR checks must pass before merge. Counts and run IDs
  are recorded in the release evidence outside the repository after completion.

## Deployment boundary

This PR adds a database table. Existing deployments must apply the reviewed migration
before starting the new API: milestone reads now include `milestone_dependencies`.
The automatic deployment guard is expected to stop on a new migration. Merging this PR
does not by itself prove runtime deployment or browser UAT. No live DB changes are part
of this merge review. Existing registration snapshots, roster and academic policies are unchanged.
