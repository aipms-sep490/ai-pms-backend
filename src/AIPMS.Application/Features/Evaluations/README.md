# Rubrics and evaluations

This module implements the rubric foundation and the agreed draft-evaluation slice
for BE-09 / issue #14. See [DRAFTS.md](DRAFTS.md) for evaluator assignment,
draft scores, preview calculation and the explicitly deferred BE-16 prerequisite.
Finalize and result publication are not implemented here.

## Requirements cross-check

Sources: AI-PMS Report 3 Software Requirement Specification, section 3.5.7
(UC-031 Manage Evaluation Rubric), sections 3.16.5-3.16.6 and BR-143/144/145;
Report 2 Project Management Plan, Sprint 2 rubric/evaluation-criteria foundation
and the later evaluation lifecycle.

| Requirement | Implementation / boundary |
| --- | --- |
| Create/edit DRAFT rubric | POST and aggregate PUT; incomplete criteria totals allowed in draft |
| Criteria, weights, max score, order | Scoped criterion definitions with exact two-decimal inputs and unique nonnegative ordering |
| Controlled publication | DRAFT -> PUBLISHED; at least one required criterion, positive weights/max scores and total weight exactly 100 |
| Protected published version | Published content, scope, criteria and weights are immutable even after retirement and before any evaluation exists |
| Publish/Retire actions | PUBLISHED -> RETIRED is terminal; create a new draft version to change or republish |
| Versioned rubric | Persisted family root, monotonically allocated version among retained versions, unique rubric ID and independent criterion definitions |
| Existing evaluation keeps old rubric | No reassignment or mutation of old rubric/criterion IDs, including when cloning or retiring |
| Department Staff manages rubric | Persisted active staff role and own active department/organization; active administrator may manage all scopes |
| BR-144 score bounds | Positive max score and two-decimal precision configured here; draft score validation is documented in DRAFTS.md |
| BR-143 and BR-145 | Draft assignment authorization and deterministic preview are documented in DRAFTS.md; finalize remains separate; no AI grading |

The 100-percent publication total follows issue #14's explicit acceptance criteria;
the SRS describes approved weight consistency. Requiring at least one required
criterion is this slice's concrete publication rule. Drafts may start empty.

COMMON / MAJOR_SPECIFIC / INDIVIDUAL rubric assignment, per-student evaluation,
course-offering bindings and BR-59 Hybrid evaluation are separate scope. Existing
schema does not yet model those assignments. This PR does not complete full BE-09.

## API contract

All endpoints are under `/api/v1/rubrics`. All accept CancellationToken and use
ProblemDetails. Controllers dispatch MediatR; persistence models never leave
Infrastructure. Responses disable caching. Only administrators and department
staff can access these management endpoints; future assigned-evaluator views need
their own project authorization.

| Method | Route | Input / result |
| --- | --- | --- |
| GET | / | departmentId, academicSemesterId, status, search, page, pageSize; paged RubricDto |
| GET | /{id} | RubricDto with criteria and concurrencyToken |
| POST | / | departmentId, academicSemesterId, code, name, description, criteria; 201 draft |
| PUT | /{id} | name, description, full criteria array, concurrencyToken; 200 updated draft |
| POST | /{id}/publish | concurrencyToken; 200 published rubric |
| POST | /{id}/retire | concurrencyToken; 200 retired rubric |
| POST | /{id}/versions | new unique code, source concurrencyToken; 201 independent draft |
| DELETE | /{id}?concurrencyToken=... | Delete an unreferenced draft; 204 |

Criteria input: name, optional description, weightPercent, maxScore, sortOrder,
isRequired. PUT replaces the full draft set (omit a criterion to remove it; change
sortOrder to reorder). Draft criterion IDs can change after PUT. IDs become
protected at publication. The API owns criterion definitions and never exposes a
shared catalogue-edit endpoint that could mutate an older rubric indirectly.

New rubrics require BOTH departmentId and academicSemesterId. Their department
must belong to the semester's active organization. Code is normalized uppercase
and globally unique (max 50 characters, letters/digits/underscore/dot/hyphen).
Scope and code stay fixed; create a separate rubric for a different semester or
department. Name <=255, description <=1000, criteria <=100, maxScore <=999999.99,
weightPercent >0 and <=100, sortOrder 0..9999; decimal input is not silently rounded.

List status is DRAFT, PUBLISHED or RETIRED; default page=1/pageSize=20 (max 100).
Results order by ID descending; criteria by sortOrder then ID. Staff can only list
their own academic scope. Cross-scope IDs return 404; an explicitly foreign list
filter or create scope returns 403. Inactive or removed persisted roles cannot be
restored by stale JWT claims. Closed/archived semesters remain readable but cannot
be changed, published, cloned or retired through this module.

All mutations of an existing rubric require its latest concurrencyToken; stale
tokens return 409. Invalid inputs return 400, invalid publication/reference/state
transitions return 409. A new version increments the family version and receives
a new ID, new criterion IDs and a new token. It retains the same academic scope.
Cloning does not switch project periods or existing evaluations to the new version.
An unused draft may be deleted; published/retired/referenced rubrics cannot.

## Transaction and BE-12 integration

Mutations lock the family root then source rubric row and commit content, version
metadata and audit together. Failed audit/persistence writes roll back the whole
mutation. Concurrent duplicate codes and stale edits return 409; family locking
serializes version allocation. Database lock conflicts return 409 for retry.
Reads keep rubric content and its token consistent across repository queries.

The existing `rubrics.is_active` remains BE-12's compatibility field: false for
DRAFT/RETIRED, true only after successful publication for API-created rubrics.
BE-12 keeps its existing semester/organization validation. Retiring removes a
rubric from new selections but preserves historical references. Existing BE-12
updates also revalidate active eligibility; an update that keeps a retired rubric
may require an explicit new published selection. The module does not auto-rebind it.

Before any draft edit/delete/publish, references from evaluations, evaluation
details or project periods are checked under the mutation lock. Later evaluation
creation must require a published rubric and preserve its ID; this is not a scoring
endpoint and does not authorize assignment/finalization.

## Database deployment

Review and apply `db/changes/20260911_add_rubric_versions.sql` before deploying
this API. On a clean database run `db/schema.sql` first, then this change script.
The additive script creates `rubric_versions` and can be rerun. EF mapping uses a
partial context outside Persistence/Generated; schema.sql and generated files
remain unchanged. The application does not auto-run migrations at startup.

Legacy rubric IDs, criteria, active flags, period links and evaluations are
preserved. Existing active rubrics are backfilled as PUBLISHED; inactive ones as
RETIRED, each with its own family root/version 1. No historical publication state
is guessed from names such as _V1/_V2, and legacy inactive rows never become editable
drafts. Reruns preserve already-managed state and concurrency tokens. Legacy rows
inserted by other tools after deployment need metadata backfill before mutation.
Unscoped legacy rows remain admin-readable and protected; create a scoped rubric
instead of silently moving historical data. Migration preserves legacy eligibility;
it does not retrospectively certify old weights/criteria against the new publish rules.

Integration tests apply the migration only to isolated databases. Deployment
requires reviewing and applying this script to the target environment before
starting the Rubrics API. No external notifications or provider credentials are needed.
