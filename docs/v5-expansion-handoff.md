# V5 expansion: backend acceptance and Tin handoff

Audit date: 2026-10-14. Scope: the approved seven-PR expansion, backend/database only.
This package does not claim completion of outstanding P0/P1 browser/provider work.

Runtime release: develop `f5e515c` (Industry PR #129), CI run `38538251172`:
1076 unit and 1299 integration tests passed, including isolated SQL parity/rerun.
The deployed image is `aipms-api:f5e515c`; public system and Swagger were verified
on 2026-10-14. This handoff PR changes documentation, test evidence and CI only;
it requires no additional database migration or runtime deployment. Machine-readable
rollout summary: `validation/v5-industry-rollout-2026-10-14.json`.

## Delivery register

| Slice | PR / merged source | Backend evidence | State |
| --- | --- | --- | --- |
| Working Agreement | #124 / f796f95 | Frozen policy/roster, own acceptance, concurrent versions, activation 422 then success, migration rerun | BACKEND_READY |
| Supervisor recommendations | #125 / 7915781 | Shared candidates, ranking/count before paging, D17/capacity/scope, persisted history | BACKEND_READY |
| Peer evaluation | #126 / 6d57fbb | Private outgoing drafts, scoped submitted readback, roster generation, no official-score writes | BACKEND_READY |
| Result revision foundation | #127 / 3a3e5cd | v1 immutable, historical calculator, all affected targets, independent maker/publisher, redacted readback | BACKEND_READY |
| Appeals/corrections | #128 / 3337e60 | Versioned policy/window, own target, scoped reviewer, independent decision, concurrency/audit rollback | BACKEND_READY |
| Industry advisory | #129 / f5e515c | Isolated HTTP enrollment/share/feedback, queue, issued role-free JWT, live revocation, exact locked reference | BACKEND_READY |
| Joint backend acceptance | This PR | Seven redacted HTTP/readback reports required by CI; all expansion suites and schema parity | BACKEND_READY |

The baseline also contains registration rules at develop `8f2f06a`. Industry was
rebased logically by merging that develop into its branch and regenerating OpenAPI.
Expiry-aware candidate filtering fixes a cross-feature gap: an overdue PENDING
request must not hide an eligible profile. GET does not change request status;
the established request workflow owns expiry mutations. Null legacy expiry remains
pending. No additional database change was needed for this fix.

`BACKEND_READY` describes implemented contract, persistence, authority and isolated
technical evidence. `INTEGRATED_READY` is **not asserted**: Tin must implement/verify
real adapters and browser lifecycles. Academic approval is a separate decision,
especially appeal policy; technical tests and a recorded source string are not that
approval. Module flags remain off on the shared backend.

## Contract to screen mapping

All routes have prefix `/api/v1`. OpenAPI is in
`docs/contracts/cib-v4/openapi.json`, generated offline from the running API build.
The names below are feature destinations for Tin, not a claim that a specific FE
route has been inspected or is already integrated.

| FE destination | Routes | DTO / binding | Backend flag | Evidence |
| --- | --- | --- | --- | --- |
| Existing Working Agreement screen | `/projects/{id}/working-agreement`, `/draft`, `/publish`, `/acceptances`, `/history` | WorkingAgreementDto: contentJson, version, required, acceptedByMe, acceptances, readiness, actions | WorkingAgreement:Enabled | WA-01..06 |
| Existing supervisor suggestion screen | `/projects/{id}/supervisor-recommendations`, `/history`, `/history/{runId}` | SupervisorRecommendationPreview.candidates.items; saved run.items; current eligibility is separate from historical score | SupervisorRecommendations:Enabled | SR-01..06 |
| Existing peer screen + Primary round management | `/projects/{id}/peer-evaluations`, `/rounds`, `/mine`, `/rounds/{roundId}/mine`, `/mine/submit` | PeerRoundDto/MyPeerEvaluationDto/PeerEvaluationDto; explicit round, no-open-round state, outgoing-only student view | PeerEvaluation:Enabled | PE-01..04 |
| Existing published results/detail/history | `/projects/{id}/result`, `/students/{studentId}/result`, each `/history` and `/versions/{version}` | ProjectResultDto/StudentResultDto with version/revisionId/supersedesVersion; typed historical breakdown | No resolver flag | RV-01..02 |
| Existing correction shell + NEW student appeals/reviewer workflow | `/projects/{id}/result/appeal-policy`, `/appeal-windows`, `/correction-requests`, request `/reviewer`, `/review`, `/preview`, `/decision`, `/history`, `/files`, `/evidence` | Explicit source/target/student/version/component; token and confirmation; redacted student vs staff/reviewer read models | Appeals:Enabled + actual approved policy | AP-01..04 |
| NEW admin invitations/lead shares/expert portal | `/admin/industry/invitations`, `/projects/{id}/industry-assignments`, `/industry/assignments`, assignment `/evidence`, `/feedback`, `/revoke` | ExpertInvitationDto has no secret; IndustryAssignmentDto has advisoryOnly/token/actions; explicit exact version share | Industry:Enabled | IN-01..07 |
| NEW invited expert Google enrollment | `/auth/industry/google/challenge`, `/auth/industry/google/enroll`; subsequent normal Google login | GoogleChallenge/LoginResponse; cookie+nonce+invitation; no academic role returned | Industry:Enabled + GoogleAuth:Enabled/config | IN-01/05/07 |

Use per-module docs for actual field constraints and complete suffixes:
`v5-working-agreement.md`, `v5-supervisor-recommendations.md`,
`v5-peer-evaluations.md`, `v5-result-revisions.md`, `v5-result-appeals.md`,
`v5-industry-advisory.md`. Authority summary is
`contracts/cib-v4/permission-matrix.md`.

## Tokens, nullability and errors

| Workflow | Mutation token/version rule | Empty/null meaning |
| --- | --- | --- |
| Agreement | expectedLatestVersion + draft token; publish/accept agreementId + content token. Acceptance does not rotate shared content token. | GET 204 if absent; fresh draft token null; no inherited consent on successor |
| Recommendations | Run creation append-only; no mutation token. Assignment still revalidates its own transaction. | Empty eligible page is valid; expertise UNKNOWN differs from NO_MATCH |
| Peer | New draft token null; subsequent saves/submits token; round close uses round token. | NO_OPEN_ROUND with null roundId/submission is explicit; never silently pick a round |
| Results | v1 has null revisionId and supersedesVersion; latest result uses resolver regardless of appeal flag. | Missing provenance is LEGACY/UNKNOWN, never recalculated from current profiles |
| Appeals | Request concurrencyToken + preview confirmationToken for ACCEPTED. Result target/version is explicit; stale result needs a new reviewed request. | PROJECT requires null studentId; STUDENT requires positive studentId; no guessing target |
| Industry | Assignment token for share/revoke/feedback; feedback also expectedVersion. Share rotates token: reload assignment before next mutation. | COMMON requires null majorId; MAJOR_SPECIFIC requires positive frozen majorId; feedback v1 supersedesVersion null |

Common ProblemDetails: `400` invalid input; `401` unauthenticated/invalid enrollment;
`403` persisted scope/ownership denial; `404` missing or disabled resource;
`409` stale version/token, duplicate, state/provenance conflict; `422` captured
mandatory Working Agreement incomplete at activation; `503` unconfigured provider.
Refresh after 409 and require a new user decision; never auto-resubmit a stale score
correction. Do not assume every route returns every code. Validation/business errors
are detailed in the route-specific OpenAPI and docs.

## Evidence index and limits

| Test ID | Source and evidence |
| --- | --- |
| WA-01..05 | WorkingAgreementEndpointTests: publish/accept/successor, revoked roster/version, concurrent create, audit rollback, Admin policy guard |
| WA-06 | WorkingAgreementActivationTests: HTTP request acceptance returns 422 until both members consent, then ACTIVE; persisted assignment readback |
| SR-01..05 | SupervisorRecommendationEndpointTests: match/unknown/stable ties, pre-page D17/scope, persisted history, filled slot, audit rollback |
| SR-06 | Recommendations_ignore_expired_pending_requests_before_count_without_read_side_mutation |
| PE-01..04 | PeerEvaluationEndpointTests: private atomic submitted workflow, roster/self/duplicate/stale denial, concurrent rounds, disabled/archived |
| RV-01 | ResultRevisionCalculatorTests: exact historical inputs/rounding, missing vs zero, individual excludes project, common/major propagation |
| RV-02 | ResultRevisionEndpointTests: original publication/readback via HTTP; correction foundation explicitly uses internal store |
| AP-01..04 | ResultAppealEndpointTests: complete HTTP amendment/publication, independent decision, revoked reviewer, stale/archived, IT+Marketing evidence isolation, audit rollback |
| IN-01..07 | IndustryEndpointTests: email queue/invited Google proof, exact shared version, IDOR/wrong major, disabled/expired/revoked, audit rollback/concurrent feedback, reissue/concurrent enrollment, interdisciplinary isolation, issued JWT |
| DB-01 | SchemaParityTests: fresh bootstrap vs additive upgrade, rerun preserving snapshots, model columns, intentional missing-index detection |
| AUTH-01 | CI isolated real-password/JWT middleware smoke; Industry additionally tests its issued role-free JWT through production account validator |

Every CI run must produce all seven files under artifact `v5-backend-acceptance`:

- `expansion-working-agreement.json`
- `expansion-working-agreement-activation.json`
- `expansion-supervisor-recommendations.json`
- `expansion-peer-lifecycle.json`
- `expansion-result-revision-foundation.json`
- `expansion-appeal-lifecycle.json`
- `expansion-industry-lifecycle.json`

`scripts/verify-v5-expansion-evidence.ps1` rejects missing/unqualified reports and
sensitive fields in projected HTTP payloads. Reports contain synthetic request/
response projections, correlation IDs, status codes and asserted DB/audit readback.
They are evidence of the named scenarios, not all production data. The foundation
report labels its internal correction call; AP-01 is the actual HTTP correction
workflow. Google provider is a fake verifier; storage is in memory for file scenarios.
Email is claimed through the real isolated queue without sending to a real mailbox.
Report collection never captures tokens, passwords, raw snapshots or free-text peer
content. CI artifacts expire; archive redacted reports to the release record if needed.

## Reproduce on an isolated database

Provide `AIPMS_TEST_SQL_CONNECTION` privately using a local/test SQL host and master
catalog. Tests create owned `AI_PMS_TEST_*` databases and clean up only their own run.
Never substitute shared `AI_PMS` as the lifecycle target. No real student list belongs
in fixtures or public artifacts. The fixture labels do not mean academic approval.

```powershell
dotnet build AIPMS.sln -c Release -warnaserror
dotnet test AIPMS.sln -c Release --no-build
./scripts/verify-v5-expansion-evidence.ps1 -Directory $env:AIPMS_ACCEPTANCE_OUTPUT
./scripts/test-e2e-bootstrap.ps1
./scripts/test-v5-api-login.ps1
```

Set AIPMS_ACCEPTANCE_OUTPUT to a private run output directory before tests. For a
focused expansion rerun, filter WorkingAgreement, Required_agreement, Recommendations_,
Peer_, Correction_foundation, Appeals_, Industry_ and SchemaParityTests. Full CI remains
required before merge. Bootstrap refuses unsafe/shared names. Fixture setup seeds
organizations, identities, policies and frozen evidence inputs; the named lifecycle
mutations run via HTTP, except the explicitly marked internal foundation scenario.

## Database and rollout ledger

| Migration | PR | Shared rollout evidence |
| --- | --- | --- |
| 20261012_add_working_agreements.sql | #124 | Applied + rerun, membership-generation guard |
| 20261012_add_supervisor_recommendations.sql | #125 | Applied + rerun, 543 integrity checks |
| 20261012_add_peer_evaluations.sql | #126 | Applied + rerun, 562 checks |
| 20261012_add_result_revisions.sql | #127 | Applied + rerun, 581 checks |
| 20261013_add_result_appeals.sql | #128 | Applied + rerun, 36 new trusted checks/FKs, 617 checks; users/projects/assignments unchanged |
| 20261013_add_supervisor_request_expiry_and_release.sql | Develop registration work | expires_at and EXPIRED/RELEASED CHECK verified read-only on shared database |
| 20261013_add_industry_advisory.sql | #129 | Applied + rerun on 2026-10-14; 20 new trusted checks/FKs; 637 integrity checks; users/projects/assignments unchanged |

Shared schema audit is separate from isolated parity. At #129 the shared database
had 17 missing signatures (including eight duplicate expected FK occurrences and
nine module-text differences), 26 extra signatures (module/diagram helpers and a
task-comment index), with all 637 FK/CHECK data checks passing. Legacy business
signals remain three missing registration snapshots and one unknown lead/major map.
These counts do not mean all legacy projects are ready; no immutable provenance is
backfilled from current profiles. The Industry rollout retained this same drift and added the
new module signatures without new differences. Isolated fresh/upgrade/rerun parity
must be exact; shared legacy drift is recorded rather than silently certified clean.

Deploy only a reviewed, green merged SHA after its migrations. Preserve old image,
private config and Data Protection key volume; recreate only the API container.
Check image SHA/health, public system 200, Swagger routes and module flags. Rollback
keeps additive tables and data. After any real result correction, the application
must retain the effective-result resolver; reverting to a pre-revision image would
display obsolete results. Disabling the appeal flag does not disable that resolver.

## Remaining integration gates for Tin and academic owners

1. Adapt the existing agreement/recommendation/peer/correction screens to actual DTOs;
   create student appeals and expert invite/enrollment/share/feedback screens.
2. Exercise the browser with two students, current Primary, lead/participating staff,
   independent reviewer/publisher and two experts. Verify wrong scopes, revoked access,
   stale tokens, keyboard use and mobile layout; retain redacted evidence.
3. Verify real Google nonce/cookie/Origin and mailbox invitation delivery on the chosen
   FE origin. Clear invite URL fragment; test replay/expiry and a non-authoritative
   email domain with mailbox proof. Do not paste secrets into GitHub.
4. Obtain/record actual appeal policy and responsible decision owners. A lead-entered
   reference or synthetic demo is not institutional approval. Working Agreement is
   mandatory only in its frozen Admin-managed policy, without retroactive demotion.
5. Enable one module at a time only after its adapter and acceptance pass. Industry
   remains advisory; peer is evidence only; no re-defense/course-grade/industry weighting
   is introduced. Existing P0/P1 provider/browser gates remain tracked separately.
