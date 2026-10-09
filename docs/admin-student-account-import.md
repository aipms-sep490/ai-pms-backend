# Student account import with first Google sign-in

## Contract

Admin uploads UTF-8 CSV or single-sheet XLSX and selects an active major for the
whole batch. Required headers: `MSSV`/`studentCode`, `Ho ten`/`fullName`, `Email`.
Optional: `SDT`/`phone`, `Khung`/`curriculumCode`.
The reader also accepts the Vietnamese headers `Họ tên`, `Họ và tên`, `SĐT`.
Maximum 500 rows, 64 columns, 5 MiB. Formula cells, macros, external workbook links
and excessive decompression are rejected. Keep student codes and phones as text
to preserve leading zeros. Other workbook columns do not assign roles or teams.

- `POST /api/v1/users/student-import/preview`: multipart `file`, `majorId`.
  Returns major/department names, `rows: [{account, errors: []}]`, `canCommit`.
  Account fields: rowNumber, studentCode, fullName, email, phone, curriculumCode.
  No database mutations occur in preview.
- `POST /api/v1/users/student-import/commit`: JSON `{majorId, rows: [account]}`.
  Returns `201 {created}`. Rechecks the complete input, persisted ACTIVE ADMIN,
  active major/department/organization and email/student-code uniqueness inside
  a serializable transaction. Role is always STUDENT; no role IDs are accepted.
- `400`: invalid rows/file/scope. `401/403`: authentication/authorization.
  `409`: existing identity/concurrent write. `413`: multipart limit (6 MiB).
  Audit failure rolls back all inserted users and roles. Repeat requests do not
  overwrite existing accounts. On uncertain responses, preview again; never replay
  automatically. Previews are advisory; commit validates supplied rows independently.

New accounts are ACTIVE with academic profile PENDING. They have no memberships,
qualification approval or academic assignments. Department is derived from the
selected major. Password hashes use independent cryptographically random values
that are never returned, recorded in files, or logged. No common default password.

## First Google login

Only accounts created through this endpoint have `google_enrollment_pending=1`.
Existing accounts retain their explicit password-confirmed Google linking flow.
On first Google login, the existing verifier validates signature, issuer, audience,
nonce, verified email and expiry. Automatic enrollment additionally requires a
Gmail address or a Workspace `hd` claim matching the email domain; a third-party
email attached to a Google account is insufficient.

Under the existing user lock, enrollment rechecks ACTIVE/unlocked status, matching
email, STUDENT-only roles, pending enrollment and absence of a Google link. It
inserts the opaque Google subject link, clears enrollment and records audit in the
same login transaction as refresh-session creation. Unique subject/user indexes
prevent competing links. Unlinking/manual linking clears enrollment, preventing
an automatic relink after intentional unlinking. Login never creates new users.

Use the exact Gmail or school Workspace mailbox listed in the file. Google login
must be configured and enabled for the target frontend origin. Imported students
may subsequently use the existing password recovery workflow to establish a
password; this feature does not send passwords or enrollment emails.

## Database-first rollout

Before running the new binary, apply
`db/changes/20261009_add_google_enrollment_pending.sql`. This adds a NOT NULL BIT
with default false, runs with QUOTED_IDENTIFIER ON, and is rerunnable. Bootstrap,
generated EF model/configuration and E2E migration manifest include the column.
No legacy user is automatically opted in. Rollback can keep the column; pending
new accounts cannot use first sign-in with an older binary.

## Validation

StudentAccountFileReaderTests: CSV/XLSX, aliases, formulas, limits, text IDs.
StudentAccountImportTests: preview/commit, uniqueness/replay, roles, active scope,
concurrent commits and audit rollback on isolated databases.
GoogleAuthEndpointTests and GoogleIdentityVerifierTests: enrollment, verified
authoritative identity, existing linking behavior, unlink and audit rollback.
GoogleEnrollmentMigrationTests: upgrade/rerun preserves existing and pending users.
Swagger routes are exercised by the existing Google/roster integration tests.

Real Google sign-in requires an actual imported mailbox and interactive consent;
fake-verifier integration tests are not evidence of live Google acceptance.

Local validation on 2026-10-09: Release build with warnings-as-errors passed;
1,014 unit tests and 49 targeted SQL integration tests passed (zero skipped).
The SQL tests include file import through the API followed by first Google login
and refresh using a fake verified identity. The FE has 69 passing related tests.
Local AI_PMS migration was applied twice and checked: 21 users before/after,
zero pending enrollments. Local Swagger exposes both routes. Browser preview
through the real local API passed with synthetic input; no account was committed.
