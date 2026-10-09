# Admin student curriculum import and team roster export

## Curriculum import (PR1)

`curriculumCode` stores the source system's "Khung" verbatim after trimming.
It is nullable, up to 100 characters. Existing students remain null; do not
derive it from student code, major, semester, or current team.

Admin create-user and JSON account import accept optional `curriculumCode`.
Account detail/list and `GET /api/v1/users/me/profile` return it. Self-service
profile updates cannot change it.

### Preview

`POST /api/v1/users/curriculum-import/preview`, multipart field `file`.
Only an authenticated, persisted ACTIVE ADMIN may preview or commit.

- UTF-8 comma-separated CSV or XLSX with exactly one worksheet.
- Row 1: `MSSV` / `studentCode`, `Khung` / `curriculumCode` (case-insensitive).
- Other columns are ignored. Maximum 64 columns, 500 data rows, 5 MiB.
- XLSX formulas, macros, external workbook links and oversized archives are rejected.
- MSSV matches existing STUDENT users, case-insensitively. No account creation.
- Unknown, ambiguous or duplicate MSSV are row errors. Empty Khung is SKIPPED,
  never an instruction to erase existing data.

Response: `rows[]`, `canCommit`. Each row includes `rowNumber`, `studentCode`,
`userId`, `fullName`, `currentCurriculumCode`, `curriculumCode`,
`expectedConcurrencyToken`, `status` (UPDATE/UNCHANGED/SKIPPED/ERROR), `errors[]`.
Preview does not change the database. FE should show errors before offering commit.

### Commit

`POST /api/v1/users/curriculum-import/commit`:

```json
{
  "rows": [
    {
      "userId": 123,
      "studentCode": "DE180001",
      "curriculumCode": "BIT_SE_18D_Java",
      "expectedConcurrencyToken": "<row token returned by preview>"
    }
  ]
}
```

Send only UPDATE/UNCHANGED rows, at most 500 unique students. This is an Admin
update API, not a signed immutable preview: it revalidates each supplied row,
persisted role, student identity and rowversion inside one transaction. Stable
user lock order prevents competing batches from partially overwriting each other.
Audit `STUDENT_CURRICULA_IMPORTED` is in the same transaction. Audit failure or
one stale row rolls back the whole batch. Re-preview identical data returns
UNCHANGED and does not update rowversions. Replaying a stale commit returns 409.

Success: `200 { "updated": 1, "unchanged": 0 }`.
Validation/file errors: 400 with `errors`; unauthenticated: 401; non-admin or
inactive admin: 403; stale identity/version or competing write: 409 with `code`.
HTTP multipart size limit: 6 MiB (413); parsed file limit: 5 MiB (400).

Only curriculum and user update timestamp change. Password, role, academic scope,
team membership and leader assignment remain unchanged.

### Database-first rollout

Run `db/changes/20261009_add_student_curriculum_code.sql` before deploying the
new binary. Additive nullable `users.curriculum_code NVARCHAR(100)`, rerunnable,
no backfill. Bootstrap schema, generated mapping and E2E manifest are updated.
Tests use owned isolated databases; no feature migration is applied to shared
`AI_PMS` by this work. Rollback to the previous binary can leave the column intact.

### Validation and sample

- Unit: CSV aliases/quoting/leading zeros, malformed/oversized data, XLSX formulas.
- SQL/HTTP: preview/commit, reimport, stale/concurrent writes, atomic/audit rollback,
  duplicate/unknown student, persisted role denial, student read-only profile.
- `docs/samples/student-curriculum-import.csv` contains synthetic identifiers.
  Replace these with the supplied real source. No real source file has been imported.

## Team roster XLSX (PR2)

`GET /api/v1/teams/export?semesterId=123&format=xlsx`

Optional positive `departmentId`, `majorId`, `teamId`. `semesterId` is required.
Only ACTIVE persisted ADMIN accounts may download; Department Staff cannot use
this endpoint. Existing dashboard portfolio CSV/XLSX/PDF exports remain separate.

### Data semantics

- Current members (`left_at IS NULL`) of teams in the selected semester.
- Department/major filters apply to **each member's current academic profile**.
  In an interdisciplinary team this may export only some members, and the leader
  may be absent when outside the chosen major. Omit those filters for a full team.
- Team must belong to the chosen semester; major must belong to the chosen
  department when both filters are supplied. Unknown/inconsistent IDs return 404.
- This is a current roster, not a historical reconstruction. Completed/archived
  teams can appear if membership is current. No inferred curriculum values.
- Sorting: team code, team ID tie-break, leader first, MSSV, user/member ID tie-break.
- At most 10,000 student rows. The query reads at most 10,001 to detect overflow;
  there is no silent truncation or client-side page aggregation. Overflow: 422,
  `detail` starts with `ROSTER_EXPORT_ROW_LIMIT_EXCEEDED`. Narrow the filters.
- No matches: a valid workbook containing headers only.

### Workbook

Exactly eight columns: STT, Mã nhóm, MSSV, Họ và tên,
Trưởng nhóm/Thành viên, SĐT, Email, Khung. All values except STT are text.
Leading zeros are preserved; user strings never become executable formulas.
Missing phone/curriculum remains blank. Mã nhóm is merged within each team,
the header is dark blue with white text and frozen, teams have alternating
backgrounds, and leader/member cells have distinct colors. Fixed readable widths,
wrapped cells and landscape printing are included. Excel/Sheets chip controls
from the screenshot are represented as cell colors, not interactive dropdowns.

Response content type:
`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`.
Filename: `team-roster-{semesterId}.xlsx`. `Cache-Control: no-store`.
Validation/unsupported format: 400; no authentication: 401; wrong/revoked role:
403; invalid filter relation: 404; row limit: 422. Audit failure prevents download.
Audit `TEAM_ROSTER_EXPORTED` records actor, filters, semester and row count,
not the downloaded personal data. No database change beyond PR1 is required.

### FE integration

1. Admin upload action sends the source file to preview. Show row statuses/errors.
2. When valid, send UPDATE/UNCHANGED rows to commit; do not send SKIPPED rows.
   On 409 require a new preview. Refresh the account list after success.
3. Admin export action sends the selected semester and optional filters to the
   roster endpoint, using the existing authenticated HTTP client with blob response.
4. Check status/content type before saving. Decode a problem+json error from the
   blob when unsuccessful. Download using Content-Disposition or the documented
   safe filename, and release any temporary browser object URL afterward.
5. Do not fetch individual user profiles or stitch paged account lists on the FE.

### Acceptance

`CurriculumFileReaderTests`, `CurriculumImportTests`, `CurriculumMigrationTests`,
`TeamRosterWorkbookTests`, `TeamRosterExportTests` cover parsing, transactionality,
upgrade/rerun, authorization, current-member filters, leader ordering, workbook
text/formula safety, 10,000/10,001 boundaries, audit and Swagger routes.

`docs/samples/team-roster-sample.xlsx` is generated by the actual HTTP export
with synthetic test students and reserved example.test email addresses. It is
not a copy of real student records. Real source import and FE integration are
still deployment/user-data steps; no shared database data is changed here.
