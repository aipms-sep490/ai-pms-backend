# Academic schema readiness before FE–BE release

The local authenticated audit on 15/11/2026 reproduced 500 responses on department documents, rubrics/Gate templates and evaluator assignment detail. The BE branch already contains the additive migrations below, and `db/e2e/migrations.json` already orders them. The configured UAT database lacked their required objects. This is a deployment/schema parity failure, not a reason to return mock data or change scoring rules.

Run `scripts/verify-academic-schema-readiness.ps1` with `AIPMS_SCHEMA_CHECK_CONNECTION` set **locally in the process environment** to the intended target. The script uses `ApplicationIntent=ReadOnly`, prints no connection details, writes no data, and exits `0` when ready, `2` when objects are missing, `3` when it cannot check. Do not paste credentials into chat, command history, a document, or Git.

The controlled release order is:

1. Confirm exact DB target, current backup and deployment owner. Review existing migration dependencies and rollback.
2. Apply `20261030_add_rubric_criterion_levels.sql`, `20261030_add_rubric_scope_fields.sql`, and `20261115_department_document_templates.sql` through the established DBA release process, in manifest order with any earlier unapplied dependencies. No migration is applied by the readiness script.
3. Repeat the read-only readiness check. A `READY` result proves schema presence only.
4. Re-run scoped GETs for documents, rubrics/Gate templates and evaluator assignment detail; then run permitted and denied actor tests, document upload/publish/download, evaluator reads and readback on isolated fixtures.

The three migration files and their registration were already present; this change adds an explicit, repeatable release gate so a partial UAT schema cannot be mistaken for complete feature readiness.
