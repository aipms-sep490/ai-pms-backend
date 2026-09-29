# Baseline v3 PR2: project requirements and review history

Backend only. Built on develop `3a883d9`: eligibility (#74/#80), mentor/governance
(#75/#76), execution concurrency (#79), isolated SQL fixtures (#81). PR3 (#84)
and PR4 (#85) depend on this contract; their implementation is not included here.

## Requirements contract

`GET /api/v1/projects/{projectId}/major-requirements` returns:

```json
{
  "concurrencyToken": "project-rowversion-base64",
  "requirements": [
    {
      "id": 1,
      "majorId": 10,
      "minMembers": 1,
      "maxMembers": 3,
      "responsibility": "Software design and implementation",
      "concurrencyToken": "requirement-guid"
    }
  ]
}
```

`PUT` at the same route atomically replaces the collection:

```json
{
  "concurrencyToken": "project-rowversion-base64",
  "requirements": [
    {
      "majorId": 10,
      "minMembers": 1,
      "maxMembers": 3,
      "responsibility": "Software design and implementation"
    }
  ]
}
```

The aggregate token is the existing project rowversion, not a per-row token.
GET/PUT return the new aggregate token for subsequent edits/submission. This new
endpoint always requires a token; it does not change compatibility mode of the
existing execution endpoints. Each successful replace advances the project token
and affected requirement tokens, including a same-value replace.

Accept 1-100 distinct active majors within the proposal academic scope, positive
min/max with max >= min, and trimmed nonempty responsibility <= 2000 characters.
All configured team majors and majors represented by active members must remain
covered. Project and team quotas are both enforced; their intersection must be
feasible under the open registration period policy and current roster. A draft
may require members not yet recruited; a PASS check/submission may not.

An open, unambiguous, configured registration period in an active semester is
required. Existing proposal edits cannot remove a major while an explicit project
requirement still references it. Remove the requirement first, subject to roster
and team-scope guards. This API does not silently modify team scope or topic quotas.

## Role / scope / state matrix

| Operation | Allowed actor | State / scope |
| --- | --- | --- |
| Read requirements/history | Active project member, assigned lecturer, scoped Department Staff, Admin | Existing persisted project-access rules; submitted scope comes from registration evidence |
| Replace requirements | Active student leader, scoped Department Staff, Admin | DRAFT or REVISION_REQUIRED; team not LOCKED/DISBANDED; active academic scope and open period |
| Academic review/decisions | Existing Department Staff reviewer | Existing lead/participating department rules; Admin alone does not gain review authority |

JWT role claims alone never grant write permission. A lecturer/mentor or nonleader
member with project access cannot replace requirements. History additionally
filters Department Staff to rounds involving their persisted department. Existing
members/assigned lecturers and Admin can read all rounds within their project access.

Missing authentication returns 401; wrong role/scope returns 403; missing project
returns 404; malformed input/pagination returns 400; stale token, locked state,
out-of-scope major or infeasible quotas returns 409. Failed writes have no partial
requirement, token or audit changes. Audit actions are
`PROJECT_MAJOR_REQUIREMENTS_REPLACED` and `TEAM_ELIGIBILITY_INVALIDATED`.

## Eligibility and locking

Explicit project requirements (IDs, quota, responsibility, revision tokens) enter
the existing eligibility project-context hash. A replace makes prior checks STALE
without mutating immutable checks. Run an explicit check/refresh before lock,
submit or resubmit. Project quotas generate explainable `PROJECT_MAJOR_QUOTA` and
`PROJECT_MEMBER_MAJOR_NOT_ALLOWED` issues; a CURRENT FAIL cannot lock/submit.
Submission revalidates project quota against the active roster inside its transaction.

The old fingerprint is preserved for projects with no explicit requirements, so
this release does not invalidate all legacy checks. Empty requirements mean
"not configured at project level": existing team/topic/period rules still apply.
PR3/PR4 must not interpret an empty list as invented quotas or responsibilities.

Requirements and submission serialize using the same team -> project lock order.
Two mutations with the same project token have one winner, the other returns 409.
Edit vs submit cannot produce a mixed snapshot. Reads return a consistent token
and collection in a transaction. Shared project transaction handling maps SQL
deadlock/unique/concurrency conflicts to 409.

## Immutable review history

`GET /api/v1/projects/{projectId}/review-snapshots?page=1&pageSize=20` returns
`page`, `pageSize`, `totalCount`, `items`. Page size is 1-100. Ordering is registration
snapshot ID descending (submission order), including when timestamps are equal.
Submission numbers are one-based within the project, not IDs shared across projects.

Each item contains `id`, `submissionNumber`, `projectPeriodId`, `submittedBy`,
`submittedAt`, `proposalAvailable`, `evidence`, and decisions for **that snapshot**.
Evidence retains frozen scope, roster, policy/version, major-department mapping
from #76 and adds:

- `proposal`: title, description, problem statement, objectives, expected output,
  source/topic ID, required major IDs and tags.
- `projectRequirements`: IDs, major, quotas, responsibility and revision tokens.

The authoritative store remains `project_registration_snapshots.snapshot_json`.
No parallel review-history table is created. Each successful submit/resubmit adds
a new envelope and decision round in the existing transaction; historical JSON
is never overwritten. Requirement IDs/tokens and full frozen values preserve
meaning even if an editable later revision removes a requirement. Current policy
and current proposal changes do not rewrite historical values. Decision requests
still require the latest snapshot ID plus project token; older rounds return 409.

Legacy envelopes remain byte-for-byte unchanged. For missing historical data,
`proposalAvailable=false`, `proposal=null`, `projectRequirements=null`. New rounds
without explicit requirements use an empty array instead of null. Existing
`academic-review` API remains available and exposes the same additive evidence.

## Database and rollout

Apply `db/changes/20260929_add_project_major_requirements.sql` before deploying the
API. It adds `project_major_requirements`, FKs, unique/index/check constraints,
revision token and timestamps. It can run repeatedly and does not backfill guesses
from current team settings into legacy projects or snapshots. `db/schema.sql` and
the integration bootstrap contain the same DDL. No EF migration is used.

The entity is reverse-engineered with EF tooling from an isolated SQL database
after applying the script; mapping lives in the existing partial DbContext
configuration pattern, including FK/default/concurrency metadata. Regenerating the
entire production model is unnecessary and would overwrite unrelated extensions.

Rollback API deployment retains the additive table and snapshot JSON fields;
do not delete history. Once explicit project quotas are used, an older API does
not enforce them: disable registration writes during rollback until the compatible
API is restored. No frontend changes or production migration are part of this PR.

## Validation and E2E

Use `AIPMS_TEST_SQL_CONNECTION` via the process environment. SQL fixtures create
and dispose only owned `AI_PMS_TEST_<guid>` databases; never run mutation tests
against shared `AI_PMS`. Without that setting, existing Testcontainers fallback
applies. Existing interdisciplinary fixtures seed both departments and run the
create/invite/proposal/check/submit/revision/resubmit lifecycle through the API.

```powershell
dotnet build AIPMS.sln -c Release -warnaserror
dotnet test tests/AIPMS.UnitTests -c Release --no-build
dotnet test tests/AIPMS.IntegrationTests -c Release --no-build
```

Tests cover migration rerun/constraints and legacy preservation; invalid quota,
scope, role, account state and token; two writers; edit-vs-submit; audit rollback;
STALE and CURRENT FAIL checks; revision/resubmit preserving proposal and quota;
per-round decisions and stale decision rejection; pagination; auth/IDOR; Swagger
routes and the existing academic review endpoint. No credentials belong in Git.

Validation on 2026-09-29: Release solution build with warnings as errors passed
with 0 warnings/errors; 869 unit tests and 925 integration tests passed, none
skipped, on isolated SQL LocalDB databases. An initial full run had one unrelated
meeting authorization assertion return 409 instead of 403; that test passed in
isolation and the final complete run passed without changing meeting code.
