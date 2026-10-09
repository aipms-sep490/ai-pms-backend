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

Implemented in the dependent export PR. Existing portfolio exports remain separate.
