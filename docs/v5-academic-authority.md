# V5 academic authority and D17 contract

Scope: BE-03/04/05/10/11 and BE-N12 foundation. This changes academic authority
from the pre-v5 Admin publication decision. Semester, project period and
qualification policy administration retain their existing Admin-only routes.
No frontend changes or feature activation are included.

## API additions

`GET /api/v1/projects/{projectId}/my-capabilities` returns
`ProjectCapabilitiesDto`: project status/token, frozen scope provenance and lead,
managed department IDs, registration snapshot ID, scheme ID/token, policy version
and `actions[]` (`code`, `allowed`, `reasons[]`). Published scheme references take
precedence over a newer draft. Existing `/actions` and `/execution-actions` remain.

The additive academic action codes are `manage_evaluation_scheme`,
`manage_evaluators`, and `publish_project_result`. Other action codes come from
the existing project/execution capability services. These are context-level
capabilities; choosing an evaluator, component or input still requires validation
at mutation time. Unknown action codes must be treated as denied by clients.

`PUT /api/v1/evaluation-schemes/{id}/components/{componentId}` updates one draft
component with `UpdateSchemeComponentRequest`:

```json
{
  "concurrencyToken": "e0202b465f584e2581974540770b847c",
  "component": {
    "name": "Marketing report",
    "scope": "MAJOR_SPECIFIC",
    "majorId": 203,
    "rubricId": 304,
    "projectWeightPercent": 30,
    "studentWeightPercent": 40,
    "requiredEvaluators": 2
  }
}
```

The IDs/token above are illustrative, not shared database records. Successful
response is `EvaluationSchemeDto` with a new token. Department staff receive only
their visible component/roster subset unless they are the frozen lead department.
Targets cannot move to another scope/major through this route. All scheme weight
invariants still apply; edits that require balancing other components use the
lead department's whole-scheme workflow. Published components are immutable.

## Authority matrix

| Actor | Scheme create/version/publish | Draft component / evaluator management | Result publication | Task/milestone structure |
|---|---|---|---|---|
| Frozen lead department staff | Whole project | All project components | Whole project, when ready | No automatic execution authority |
| Participating department staff | Denied | Own responsible components only | Denied; own student preview may be readable | Denied |
| Admin alone | Denied | Denied | Denied | Denied |
| Current Primary | Denied | Cannot also become evaluator of same project | Denied | Allowed under active project/workflow guards |
| Active discipline Mentor | Denied | May separately be assigned evaluator within scope | Denied | Denied; own scoped execution writes remain |
| Student leader | Denied | Denied | Denied | Existing leader authority |
| Assigned evaluator | Denied | Own assigned scoring only | Denied | No automatic authority |

`AcademicAuthority` is shared by workflow/capability checks. It requires an active
department-scoped staff actor and validated `FROZEN_REGISTRATION_SNAPSHOT`.
Department ID ordering and current team configuration cannot select the lead.
Invalid/missing legacy provenance grants no mutation. Current account, department
and organization availability is checked separately from frozen ownership.

`ProjectExecutionAuthority` is shared by task and milestone repositories, which
are also used by execution capabilities. It requires a persisted active student
leader or active lecturer with an unended PRIMARY assignment. Mentor membership
does not satisfy Primary authority. The task-filter/evidence changes owned by
PR #110 remain a separate dependency; its diff must be reviewed again when merged.

## D17 and errors

Both evaluator creation and Primary creation/replacement acquire the same
transaction-held exclusive project lock before checking the opposite assignment.
Supervisor capacity locks are retained. New `SUPERVISOR` evaluation assignments
return `409 SUPERVISOR_EVALUATION_TYPE_RETIRED`. The value remains accepted by the
request parser so existing clients receive that stable business error rather
than silently mapping it to LECTURER. Existing legacy assignments are not revoked
and still require their original active supervisor/scoring scope checks.

Concurrent Primary/evaluator attempts cannot both create the conflicting state.
An active opposite assignment returns:

```json
{
  "title": "Conflict",
  "status": 409,
  "code": "PRIMARY_EVALUATOR_CONFLICT",
  "detail": "The current primary supervisor cannot evaluate the same project."
}
```

This is an illustrative ProblemDetails excerpt; `type`, `instance` and trace
fields are supplied by the middleware. Candidates exclude conflicts before
count/paging, including Primary replacement candidates. Mutation rechecks even
after a successful candidate preview. Revoked/ended assignments, other projects,
and discipline Mentor assignments do not trigger this conflict.

| HTTP | Meaning / FE handling |
|---|---|
| 400 | Malformed body, range or enum; show field validation |
| 401 | Missing authentication or inactive session; reauthenticate |
| 403 | Wrong actor/department/component authority; refresh capabilities |
| 404 | Missing resource or mismatched parent resource |
| 409 | D17, retired type, stale token, immutable scheme or invalid workflow state; reload before retry |

Scheme writes use the scheme GUID token; project capabilities return the existing
project row-version token. Result writes still use the preview confirmation token.
Never interchange them. Publication capability delegates to the same readiness
preview used by publication, including all finalized input requirements.

## Database and catalog

`20261011_seed_enforced_authorization_catalog.sql` additively seeds six existing
ASP.NET policy codes from `AuthorizationPolicies`. It adds no table or role grant,
preserves existing catalog records and is rerunnable with QUOTED_IDENTIFIER ON.
Bootstrap and manifest include it; generated models need no changes.

The catalog describes deployed policy names. Editing role-permission rows does
not bypass the backend's role, assignment, frozen scope or state checks. The
frontend catalog remains a read-only integration surface in this delivery.

## Validation and remaining work

Tests include two-direction D17 lock contention, active/revoked/mentor behavior,
legacy finalize, admin denial, lead/participating scope, component stale token and
immutability, capability/OpenAPI, real single/interdisciplinary lifecycle, schema
parity and migration rerun. All test data is isolated and synthetic.

This contract does not mark locked target evidence (PR #110), versioned demo versus
official approval metadata, COLD or full v5 acceptance delivered. See
`v5-backend-implementation.md` for the phased status and rollout evidence.
