# AI-PMS v5 P0 + P1 implementation

Status: **IN_PROGRESS**. Neither `P0_P1_BACKEND_READY` nor
`P0_P1_INTEGRATED_READY` has been achieved.

Baseline: backend `develop@09abf9595193cbb721130f5db8dd84d67cd13116`.
Frontend baseline: `develop@dc3951d58268840b09564901003e13948a736755`.
Frontend is maintained separately by Tin. This change contains no frontend edits.

## Delivery register

| Phase | Tickets | Status | Evidence / remaining work |
|---|---|---|---|
| 1 | BE-01, BE-02 | DELIVERED | PR #111 merged as f24db3853f5bca12e28ac30aae763e2caf31d7b6; CI passed; shared additive contribution migration/rerun and selected readiness passed |
| 2 | BE-03/04/05/10/11, BE-N12 | DELIVERED | PR #112 merged as 942edb2c30deb55c8a544d994d3fa013576ef577; 1022 unit/1127 integration passed; CI passed; seed-only catalog applied/rerun on shared DB |
| 3 | BE-07/08 | DELIVERED | PR #110 merged as f5d9ef1bdd4b058274b183de4ba7cc23f894ab23; CI 1041 unit/1167 integration passed; positive targeted upload-lock-download-revoke journeys; target migration/rerun on shared DB verified |
| 4 | BE-09/17, BE-N11 | DELIVERED | PR #113 merged as 3619ea0505173f73370c700dabcf643218462f90; CI/1053 unit/1171 integration passed; nullable scheme metadata migration/rerun verified on shared DB |
| 5 | BE-N01 | DELIVERED | PR #114 merged as 9b5a6dfe5454edccabe7467a5523838a7d39d7a0; CI/1053 unit/1185 integration and isolated parity/bootstrap passed; five-table migration/rerun verified on shared DB |
| 6 | BE-N02/03 | DELIVERED | PR #115 merged as 868bd82725248290bef4169ecca984e76a9ba199; CI 1054 unit/1193 integration passed; nullable stage/package migration/rerun verified on shared DB |
| 7 | BE-N04 | DELIVERED | PR #116 merged as af4cce03b8c75a400f33260d46656b81f2760594; CI, 1055 unit/1197 integration, isolated bootstrap/rerun passed; two-table shared migration/rerun verified |
| 8 | BE-12/13/14 | IN_PROGRESS | Exact-source reviews/checkpoints/defense schedule implemented; 1055 unit/1205 integration passed with zero skips, isolated bootstrap/rerun passed; after queue/Swagger additions 9 focused tests passed; CI/rollout pending; contract in v5-academic-tracking.md |
| 9 | BE-06, BE-N05/06/07 | NOT_STARTED | Calendar, leader change, RSVP, recurrence and notes locking |
| 10 | BE-N08 | NOT_STARTED | Sprint, task board data and history-derived burndown |
| 11 | BE-N09 | NOT_STARTED | Multiple chat attachments, reactions, mute |
| 12 | BE-15, BE-N10 | NOT_STARTED | AI history, dashboards, A-F acceptance and final FE handoff |

## Decisions governing implementation

- D17: block new simultaneous primary/evaluator roles within the same project in
  both directions under one project lock, including replacement/activation.
  Mentors and other projects are unaffected. Stop creating SUPERVISOR evaluation
  assignments; retain legacy completion behavior without retrospective revocation.
- Academic project scheme/evaluator/publication authority belongs to its frozen
  lead department. Participating departments act only within assigned components.
  Admin is not an academic evaluator/publisher by platform role alone; semester,
  period, qualification policy and existing supervisor appointment administration
  remain Admin-controlled. Phase 2 replaces the pre-v5 Admin publication behavior.
- Scoring stage ON_GOING/COLD/DEFENSE is independent of target scope.
- Demo student weights: Common 50%, Own Major 30%, Individual 20%. Demo project:
  Common 50%, MajorAggregate 50%. Industry is advisory; major gate is off. No
  CourseGrade conversion. D4/D5/D6/D13-D16 academic decisions remain separate.
- Missing scores remain pending, not zero. Versioned decimal calculation preserves
  historical results. Demo rounding is two decimal places, not retroactive.
- COLD templates are per major; IT's seven reports do not constrain Marketing or
  Design. Weekly scores use configurable demo rubrics and do not update final results.
- Gates track decisions only. Appeals, new defense attempts, peer assessment,
  Working Agreement and industry scoring are outside this delivery.
- Recurring meetings create at most 12 individual occurrences, each with the
  existing 15-minute reminder. Notes lock preserves action completion updates.
- One active sprint per project; carryover is explicit. Chat permits up to ten
  attachments, at most 10 MiB each, with message membership and recall controls.
- Keep backend evidence `items[]`; Tin maps to the current `files[]` view model.

## Phase 1 contract and database decision

No HTTP route, DTO, enum, authority or feature flag changes in phase 1. Existing
OpenAPI remains the FE contract. No new migration is necessary: the manifest
already contains `20260916_add_contribution_snapshots.sql` in dependency order.

The consolidated bootstrap now includes previously omitted topic tables,
contribution snapshots, notification occurrence/delivery tables and qualification
tables. Historical migration files and their checksums are unchanged. Every SQL
test now uses the same ordered `db/e2e/migrations.json` instead of a stale manual
list. Guards reject database switching and shared database names.

Parity is checked for bootstrap + migrations versus pre-v5 bootstrap + migrations,
then replay with existing snapshot data. No real business records are copied into
fixtures. Structural parity is distinct from API/security/lifecycle acceptance.
The isolated input fixture and runbook are in `docs/v5-e2e-runbook.md`.

Local Release build uses warnings-as-errors. Validation on 2026-10-11: 1,014
unit tests and 1,119 integration tests passed, zero skips. Bootstrap create,
schema readiness, stable aliases/password hashes, replay and ownership/checksum
guards passed. An initial unbounded run lost SQL connections; the final complete
run with bounded class concurrency and per-database pools passed. Tests asserting
business races retain their explicit concurrent requests.

Read-only shared readiness evidence is saved in
`docs/validation/v5-shared-schema-2026-10-11.json`: `contribution_snapshots`, its
unique index and two FKs are absent. This is a selected capability report, not a
full production schema certification. The existing contribution migration must
be applied after the release review; no historical snapshot backfill is planned.

## Rollout gate

After PR #111 passed CI and merged, the existing contribution migration and rerun
were applied to shared `AI_PMS` with QUOTED_IDENTIFIER ON. Snapshot rows remain zero;
checked users/projects/evaluations/student-results counts did not change. The
selected after-report is `docs/validation/v5-shared-schema-after-foundation-2026-10-11.json`.
No business backfill or lifecycle test was run there. After PR #112 passed CI and
merged, phase 2 catalog seed ran twice on AI_PMS. All six policy codes exist;
checked business and role-grant row counts are unchanged. Evidence:
`docs/validation/v5-academic-catalog-rollout-2026-10-11.json`.
After PR #110 passed CI and merged, the nullable deliverable target migration ran
twice on AI_PMS. Three columns, the trusted check/two FKs and index were verified;
declared targets remain zero and checked business counts are unchanged. Evidence:
`docs/validation/v5-evidence-target-rollout-2026-10-11.json`.
After PR #113 passed CI and merged, scheme governance metadata migration ran
twice on AI_PMS. Three nullable columns/two trusted JSON checks were verified,
existing schemes retain unknown metadata, checked business counts are unchanged:
`docs/validation/v5-scoring-metadata-rollout-2026-10-11.json`.
After PR #114 passed CI and merged, COLD submission migration ran twice on AI_PMS:
five tables, eight trusted checks, fifteen trusted FKs and eleven unique indexes
verified; COLD rows remain zero and checked business counts are unchanged.
Evidence: `docs/validation/v5-cold-submissions-rollout-2026-10-11.json`.
After PR #115 passed CI and merged, COLD evaluation stage migration ran twice on
AI_PMS. Three nullable columns, three trusted checks, one FK and one index verified;
legacy stages remain null and checked business counts are unchanged. Evidence:
`docs/validation/v5-cold-evaluation-rollout-2026-10-11.json`.
After PR #116 passed CI and merged, weekly assessment migration ran twice on
AI_PMS. Two tables, six trusted checks, eight trusted FKs and four unique indexes
verified; new tables are empty and checked business counts are unchanged.
Evidence: `docs/validation/v5-weekly-assessments-rollout-2026-10-12.json`.
Run isolated tests first, obtain CI/review for each dependency PR, then
deploy compatible application/schema changes and enable features individually.
Do not infer readiness from table count, mock responses or this tracker.
