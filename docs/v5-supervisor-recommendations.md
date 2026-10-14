# Supervisor recommendations (expansion PR2)

Backend/database only. `SupervisorRecommendations:Enabled` defaults false. This
module suggests eligible profiles; it never creates a request or an assignment.

## Contract

Base `/api/v1/projects/{projectId}/supervisor-recommendations`:

| Method / suffix | Response | Meaning |
| --- | --- | --- |
| GET | SupervisorRecommendationPreview | Current bounded ranked page, no persistence |
| POST | SupervisorRecommendationRun | Persist exactly this ranked page and its total count |
| GET /history | PagedResult<SupervisorRecommendationRunSummary> | Latest run first, visibility filtered before count/page |
| GET /history/{runId} | SupervisorRecommendationRun | Original snapshot plus live eligibility |

GET query and POST body use `SupervisorRecommendationRequest`: assignmentType
PRIMARY (default) or DISCIPLINE_MENTOR, majorId (required only for mentor), optional
search <=255 characters, page 1..100000, pageSize 1..100 (default 20).

Example POST body:

```json
{"assignmentType":"DISCIPLINE_MENTOR","majorId":42,"page":1,"pageSize":20}
```

`engineVersion=rules-v1`. `score` is matched required majors / required majors,
between zero and one, not a probability. Missing expertise yields score zero and
`expertiseStatus=UNKNOWN`; known unrelated expertise yields `NO_MATCH`. Neither
implies ineligibility for PRIMARY. Mentor still requires exact matching expertise
and the responsible department. Match normalizes case and surrounding spaces only;
no fuzzy subject inference or accent-insensitive matching.

`candidate` is the generation-time identity/capacity snapshot. `matchedExpertise`,
score, generation reasons and creation time never change. On history detail, use
`currentlyEligible` and `currentReasons` for live eligibility; a historical
`candidate.eligible=true` is not an authorization grant. Capacity/eligibility is
revalidated by existing request/accept/replace transactions before assignment.
No content or concurrency token is required for append-only advisory runs.

## Shared predicate and ordering

Candidate API and recommendations share SupervisorCandidatePolicy. Repository
eligibility filters active account/department/organization/lecturer, availability,
project departments, pending slot request, D17 Primary/evaluator conflict, mentor
major/expertise and both profile/semester caps. Filtering happens before count,
ranking and pagination. SQL orders matched-major count descending, remaining slots
descending, profile ID ascending. History rechecks stored profile IDs in one
bounded query (at most 100); there is no query loop per candidate.

Current frozen registration scope is required. Unknown legacy scope returns 409,
without reconstructing historical scope from current team membership. PRIMARY
generation requires APPROVED/no active primary. Mentor generation requires ACTIVE,
primary present and an unoccupied required-major slot. History remains readable
after slot occupation/lifecycle change and returns SELECTION_NOT_AVAILABLE.

| Actor | Scope |
| --- | --- |
| Current student member | Project candidates/history |
| Current Primary | Project candidates/history |
| Lead staff | Project candidates/history |
| Participating staff | Mentor runs for majors in own frozen department |
| Current Mentor | Mentor runs in assigned majors |
| Admin | Existing platform candidate-read permission, no academic assignment grant |
| Outsider, ended lecturer, disabled account | Denied |

Department/mentor limits are applied to history count and pages as well as detail.
All reads recheck persisted account and project access; JWT role claims alone do
not grant access. Generation repeats checks inside the project transaction.

## Errors, storage and FE

400 invalid type/target/paging; 401 anonymous; 403 actor/slot scope; 404 disabled
feature or missing run under project; 409 unavailable selection/policy/capacity
context or ACADEMIC_SCOPE_UNKNOWN. A valid selection with no eligible profiles
returns 200 with empty items and totalCount=0, not fabricated recommendations.

Migration `20261012_add_supervisor_recommendations.sql` adds runs and items with
actor/project/profile/major FKs, scope/paging/JSON checks, unique profile per run,
ordered position and project-history index. Additive, QUOTED_IDENTIFIER ON,
transactional and rerunnable; bootstrap, manifest and model updated together.
Snapshot persistence and SUPERVISOR_RECOMMENDATIONS_CREATED audit are atomic.
Audit records IDs/type/count/version, not search text, biography or email.

Tin's existing recommendation adapter must map preview.candidates.items or saved
run.items. Display generation time and rules-v1, distinguish UNKNOWN from NO_MATCH,
and refresh current eligibility before selection. Keep FE flag off until adapter
and browser acceptance pass. POST stores the requested bounded page, not an
unbounded directory dump. Existing candidate and assignment routes stay available.

Tests: SupervisorExpertiseMatchTests; Recommendations_* in
SupervisorRequestEndpointTests; existing supervisor request/replacement/D17 tests;
SchemaParityTests fresh/upgrade/rerun and mapped-column checks. SQL/API tests use
owned isolated databases. API evidence is allowlisted synthetic request/response,
run/item DB readback and audit; browser and real-mailbox tests are separate.

Registration-expiry compatibility: a PENDING request whose expiresAt is at or before server time no longer hides the profile from candidates/recommendations. This filter runs before count/ranking/paging and does not mutate request history during a read; legacy null expiry remains pending.

