# V5 isolated test foundation

Use an authorized SQL connection in the **process environment**, never in source
or command history. `AIPMS_TEST_SQL_CONNECTION` identifies a server; the tooling
overrides its database with an owned random test catalog. Tests must never target
shared `AI_PMS` for lifecycle mutations.

```powershell
dotnet build AIPMS.sln -c Release -warnaserror
dotnet test AIPMS.sln -c Release --no-build
./scripts/test-e2e-bootstrap.ps1
```

CI runs the SQL parity tests through the integration test project against its SQL
service. Test classes have bounded parallelism; concurrency tests still issue
simultaneous requests inside each test. Each test DB uses its own connection pool.

For a manual API acceptance run:

```powershell
./scripts/new-e2e-database.ps1
```

The output gives the owned database name, stable aliases and a generated local
test credential. Treat the credential as temporary; do not commit it or include
it in acceptance artifacts. Configure a test API instance to use that database.
Save the exact returned name for rerun and cleanup:

```powershell
./scripts/new-e2e-database.ps1 -DatabaseName AI_PMS_E2E_<returned-guid> -VerifyRerun
./scripts/new-e2e-database.ps1 -DatabaseName AI_PMS_E2E_<returned-guid> -Drop
```

Only the exact owned name and ownership marker permit cleanup. A failed create
may leave its owned DB for diagnosis; do not substitute an unrelated DB name.

## Input actors

All emails are synthetic `@e2e.invalid`; no real student data is included.

| Scenario | Students | Academic actors / departments |
|---|---|---|
| A: IT | v5.a.leader, v5.a.member1, v5.a.member2 | v5.staff.it, v5.primary, v5.evaluator.it; V5-IT |
| B: Marketing demo | v5.b.leader, v5.b.member1, v5.b.member2 | v5.staff.marketing, v5.mentor.marketing, v5.evaluator.marketing; V5-MKT |
| C: IT + Marketing + Design | v5.c.leader, v5.c.marketing, v5.c.design | staff, primary/mentors and separate evaluators for all three majors |

The base seed supplies `admin@e2e.invalid`. V5 adds 18 active verified actors,
three departments/majors, supervisor profiles and aliases. A lecturer profile is
an input, **not** an active project assignment. The v5 seed creates no team,
registration approval, evaluation assignment, score or published result. Existing
base-seed draft projects are labelled legacy sample fixtures, not v5 acceptance.

Use the admin API to configure scenario windows/policies relative to the test
clock, then create teams, applications, frozen scope, decisions and assignments
through actual authenticated APIs. Rubrics must stay explicitly demo unless a
separate academic approval is recorded. The full A-F API driver is still pending
the feature phases; these input fixtures do not certify it.

## Required evidence per scenario

Store redacted requests/responses, correlation IDs, status/error codes, DB
readback and audit IDs. A: IT lifecycle; B: Marketing demo; C: interdisciplinary;
D: scope/assignment/concurrency negatives; E: golden decimal scoring
8.10/7.90/7.40 and project 7.67; F: AI unavailable/insufficient-evidence fallback.
Mock data and successful HTTP status alone cannot establish completion.
