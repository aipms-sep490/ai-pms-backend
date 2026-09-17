# Contribution tracking (BE-13)

The contribution API reports explainable activity indicators for a project. The indicator is not a grade and does not use AI to score students.

## Endpoints

- `GET /api/v1/projects/{projectId}/contributions` returns the team summary. Use `page`, `pageSize` (1-100), and `snapshot=true` to read the latest stored snapshot.
- `GET /api/v1/projects/{projectId}/contributions/{userId}/evidence` returns paged evidence for one member. Use `sourceType` to filter `TASK`, `PROGRESS_REPORT`, `MEETING`, `DELIVERABLE_VERSION`, or `FILE`.
- `POST /api/v1/projects/{projectId}/contributions/snapshot` creates a timestamped snapshot. Repeating the request for unchanged evidence is idempotent.

Summary statistics are calculated over the complete team before member pagination is applied. A team needs at least two members and three credited activity events to produce `SUFFICIENT` data; otherwise the response is `INSUFFICIENT_DATA` and variance is `null`. File evidence has zero credit so uploads cannot inflate the indicator.

Snapshot rebuilds require an active administrator or department staff member whose department covers the project. Rebuilds are serialized per project and write an audit entry in the same transaction. Archived projects are read-only and return the stored snapshot and frozen evidence; an archive without a snapshot returns `404`.

## Scoring and compatibility

Rule `activity-v2` gives one credit per completed task, divided equally among its eligible assignees; one per submitted/reviewed report; one per attended completed meeting; and one per submitted deliverable version. Rejected/superseded deliverable versions still record activity, not quality. Assignments alone give no credit. Events must fall within the contributor's team membership interval, and departed members remain in the statistics. Variance is the population variance across all recorded members, including members with zero activity.

Evidence timestamps use UTC. Evidence is ordered by timestamp descending, then source type and source ID; members are ordered by user ID. A snapshot stores these evidence descriptors and all member statistics. Its hash incorporates the previous generation and the full captured payload, so changing an evidence label without changing counts creates a new generation. An unchanged replay preserves the original timestamp/hash and does not create a second audit event.

The evidence response changes from an array to `{ items, page, pageSize, totalCount, totalPages }`; frontend callers must read `items`. Summary adds pagination and snapshot metadata. Apply the existing `db/changes/20260916_add_contribution_snapshots.sql` migration before use. No generated EF model or base schema change is required.

Create the snapshot before archiving. Legacy `activity-v1` member-array snapshots remain readable; they have no frozen evidence, so archived evidence returns `409` instead of reconstructing history from live tables.

Current limitations: task credit relies on the currently retained assignment rows, because historical assignment changes are not reconstructed. Files have project evidence links through deliverables, reports, and meetings; the current schema has no direct task-file link. Pagination bounds the response, but aggregation still reads the project's evidence into memory and serializes requests on the project row. This favors consistency for capstone teams; large project histories would need a separate indexed aggregation design.

## Local SQL integration test

Set `AIPMS_TEST_SQL_CONNECTION` to an isolated SQL Server connection before running the integration project. The tests create and remove an owned `AI_PMS_TEST_<guid>` database, so the source catalog should be `master` and no production database is modified.

```powershell
$env:AIPMS_TEST_SQL_CONNECTION = 'Server=localhost,1433;Database=master;User Id=sa;Password=<local-secret>;TrustServerCertificate=True;Encrypt=False;'
dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj --filter 'FullyQualifiedName~ContributionEndpointTests'
```

On Windows with Docker inside WSL, run the installed Windows .NET SDK against the reachable SQL TCP port. Setting this environment variable avoids requiring Windows Testcontainers to access the WSL Docker socket. Do not assume a newly published WSL port is forwarded to Windows: verify the SQL login before running the suite. Connection strings belong only in the current shell or a secret store.
