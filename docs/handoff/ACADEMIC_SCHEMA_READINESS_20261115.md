# Academic schema readiness before FE–BE release

The local authenticated audit on 15/11/2026 reproduced 500 responses on department documents, rubrics/Gate templates and evaluator assignment detail. The BE branch already contains the additive migrations below, and `db/e2e/migrations.json` already orders them. The configured UAT database lacked their required objects. This is a deployment/schema parity failure, not a reason to return mock data or change scoring rules.

Run `scripts/verify-academic-schema-readiness.ps1` with `AIPMS_SCHEMA_CHECK_CONNECTION` set **locally in the process environment** to the intended target. The script uses `ApplicationIntent=ReadOnly`, prints no connection details, writes no data, and exits `0` when ready, `2` when objects are missing, `3` when it cannot check. Do not paste credentials into chat, command history, a document, or Git.

The check now covers 34 named tables, columns, enabled indexes, trusted foreign keys and trusted check constraints from the three migration groups. It is a deployment preflight, not a data-quality or full schema-parity proof. On an isolated local database created from the ordered migration manifest it returned `READY 34/34`; the configured UAT target returned `NOT_READY 34/34` on 15/11/2026. The local migration runner replayed the manifest successfully. Disabling one check constraint in that owned database made the preflight return `NOT_READY 1/34`; restoring it returned `READY 34/34`.

The isolated baseline-upgrade test exposed a separate SQL Server batch-compilation defect in `20261114_confirm_defense_committees.sql`: a newly added `status` column was referenced by a check constraint and filtered index in the same batch. A new additive prerequisite, `20261113_prepare_defense_committee_columns.sql`, now creates `status` and `version` in an earlier batch. The original migration remains byte-for-byte unchanged so already-applied checksums stay valid. `SchemaParityTests.Fresh_upgrade_and_rerun_have_identical_schema_and_preserve_snapshots` passes on local SQL Server after the fix, and the existing owned E2E database replays the new manifest. This changes no committee role or scoring rule.

The controlled release order is:

1. Confirm exact DB target, current backup and deployment owner. Review existing migration dependencies and rollback.
2. Apply `20261030_add_rubric_criterion_levels.sql`, `20261030_add_rubric_scope_fields.sql`, and `20261115_department_document_templates.sql` through the established DBA release process, in manifest order with any earlier unapplied dependencies, including `20261113_prepare_defense_committee_columns.sql` before the unchanged `20261114_confirm_defense_committees.sql` where needed. No migration is applied by the readiness script.
3. Repeat the read-only readiness check. A `READY` result proves these 34 metadata items are present and enabled/trusted; it does not prove full data compatibility.
4. Re-run scoped GETs for documents, rubrics/Gate templates and evaluator assignment detail; then run permitted and denied actor tests, document upload/publish/download, evaluator reads and readback on isolated fixtures.

The three migration files and their registration were already present; this change adds an explicit, repeatable release gate so a partial UAT schema cannot be mistaken for complete feature readiness.
