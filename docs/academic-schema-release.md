# Academic schema release

This release adds rubric levels and scope, confirmed defense committee metadata, common-scope Cold checklist support, and department document templates. It does not seed academic decisions or alter historical scores.

1. Run the read-only `scripts/verify-academic-schema-readiness.ps1` with `AIPMS_SCHEMA_CHECK_CONNECTION` set in the process environment. Record the missing objects.
2. Test the exact branch with the isolated E2E database and run the FE production build. Identify the configured SQL Server and database by a read-only query. Do not put connection strings in Git, shell history, logs, or reports.
3. Set `AIPMS_MIGRATION_CONNECTION` in the process environment and run `scripts/apply-academic-schema-release.ps1 -ExpectedServer <server> -ExpectedDatabase <database>`. The runner refuses a different target, creates a unique copy-only backup with checksum, verifies it, then applies six additive scripts in order. Its ledger records each script hash and prevents a changed script from being replayed.
4. Run `scripts/verify-academic-schema-readiness.ps1` and `scripts/test-schema-readiness.ps1` against the target using a read-only connection. Confirm application behavior separately with authorized test accounts before claiming end-to-end acceptance.

Keep the reported backup path and the SQL Server backup retention policy. A failed migration stops for diagnosis; the runner never restores a live database automatically, since that could discard concurrent writes. A database restore, if required, is a separately reviewed recovery operation.
