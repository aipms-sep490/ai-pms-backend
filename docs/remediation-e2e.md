# Remediation acceptance database

Backend only. Never point mutation tests at the shared `AI_PMS` database.

## Bootstrap and cleanup

Set `AIPMS_TEST_SQL_CONNECTION` in the process environment to a SQL Server login with database creation permission. The script ignores its initial catalog and creates a random `AI_PMS_E2E_<guid>` database. Do not put credentials in command history, source control, reports or logs.

```powershell
$database = ./scripts/new-e2e-database.ps1
./scripts/new-e2e-database.ps1 -DatabaseName $database -VerifyRerun
./scripts/new-e2e-database.ps1 -DatabaseName $database -Drop
./scripts/test-e2e-bootstrap.ps1
./scripts/test-schema-readiness.ps1
```

Only matching database names with the `AIPMS_E2E_OWNER=remediation-v1` marker may be replayed or dropped. Failed runs are retained for diagnosis. Cleanup affects only the named disposable database.

`test-schema-readiness.ps1` is SELECT-only and reads the explicitly configured catalog. Its JSON report covers required workflow tables, token type/nullability/defaults, indexes and trusted constraints, with a UTC timestamp. It exits with an error for missing capabilities. It prints no credentials, usernames, row data or server names. This is a schema report, not proof that provider configuration or all workflow acceptance scenarios passed.

The ordered manifest is `db/e2e/migrations.json`. Each schema/migration/seed is recorded in `e2e_script_ledger` with SHA-256, UTC application time and SQL login. Hashes normalize CRLF to LF. Changed applied scripts are rejected; create a fresh database or add a migration. This ledger covers disposable fixtures, not historical production deployments. No application startup migration is added.

Passwords use runtime ASP.NET Identity V3 hashes. Optionally supply `AIPMS_E2E_PASSWORD` through the process environment before initial creation; otherwise a random password is discarded. Replay preserves hashes. Test users use `@e2e.invalid`; this is not an email delivery test.

## Fixtures and evidence boundaries

`dbo.e2e_aliases` resolves semantic names to database IDs. Accounts include Admin, staff from both participating departments and an outside department, primary supervisor candidate, discipline mentor candidate, evaluator, inactive/outside lecturers, student leaders/members and an outsider. Supervisor/mentor are lecturer capabilities, not separate application roles. Discipline assignment remains dependent on PR #76.

Both `single-project` and `inter-project` begin in DRAFT with persisted team/major scope and separate rosters of three students. No final package, grade, approval or ACTIVE state is invented by this seed. Registration/review/supervisor-selection/execution/final/evaluation windows are sequential, not simultaneously open. API acceptance fixtures use their own controlled clock and database to prove transitions; changing a fixture clock also requires opening the intended period status explicitly.

Existing SQL suites cover final package upload/locking/download, evaluator assignments/scoring, deterministic result publication, scope denial, stale tokens and transactional rollback. The additional acceptance journey must join these steps through API before claiming end-to-end completion. Registration/eligibility snapshot integration awaits the production implementation from issue #74; schema-only PR #80 does not establish completion.

Before release, preserve the target backup, review each additive migration and run schema/FK/index verification on the actual target. Use a forward corrective migration when data has been written; never drop feature tables as an automatic rollback. Scaffold and review mappings for schema changes. These tooling changes introduce no production schema changes.
