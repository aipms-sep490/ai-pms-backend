# Contribution tracking (BE-13)

The contribution API reports explainable activity indicators for a project. The indicator is not a grade and does not use AI to score students.

## Endpoints

- `GET /api/v1/projects/{projectId}/contributions` returns the team summary. Use `page`, `pageSize` (1-100), and `snapshot=true` to read the latest stored snapshot.
- `GET /api/v1/projects/{projectId}/contributions/{userId}/evidence` returns paged evidence for one member. Use `sourceType` to filter `TASK`, `PROGRESS_REPORT`, `MEETING`, `DELIVERABLE_VERSION`, or `FILE`.
- `POST /api/v1/projects/{projectId}/contributions/snapshot` creates a timestamped snapshot. Repeating the request for unchanged evidence is idempotent.

Summary statistics are calculated over the complete team before member pagination is applied. A team needs at least two members and three credited activity events to produce `SUFFICIENT` data; otherwise the response is `INSUFFICIENT_DATA` and variance is `null`. File evidence has zero credit so uploads cannot inflate the indicator.

Snapshot rebuilds require an active administrator or department staff member whose department covers the project. Rebuilds are serialized per project and write an audit entry in the same transaction. Archived projects are read-only and return the stored snapshot and frozen evidence; an archive without a snapshot returns `404`.

## Local SQL integration test

Set `AIPMS_TEST_SQL_CONNECTION` to an isolated SQL Server connection before running the integration project. The tests create and remove an owned `AI_PMS_TEST_<guid>` database, so the source catalog should be `master` and no production database is modified.

```powershell
$env:AIPMS_TEST_SQL_CONNECTION = 'Server=localhost,1433;Database=master;User Id=sa;Password=<local-secret>;TrustServerCertificate=True;Encrypt=False;'
dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj --filter 'FullyQualifiedName~ContributionEndpointTests'
```
