# AI-PMS v5 P0 + P1 implementation

Status: **IN_PROGRESS**. Neither `P0_P1_BACKEND_READY` nor
`P0_P1_INTEGRATED_READY` has been achieved.

Baseline: backend `develop@09abf9595193cbb721130f5db8dd84d67cd13116`.
Frontend baseline: `develop@dc3951d58268840b09564901003e13948a736755`.
Frontend is maintained separately by Tin. This change contains no frontend edits.

## Delivery register

| Phase | Tickets | Status | Evidence / remaining work |
|---|---|---|---|
| 1 | BE-01, BE-02 | IMPLEMENTED_PENDING_REVIEW | Manifest-driven bootstrap, pre-v5 upgrade/parity/rerun tests and three-major input seed; 1,014 unit and 1,119 integration tests pass locally; shared rollout pending |
| 2 | BE-03/04/05/10/11, BE-N12 | NOT_STARTED | Frozen academic authority, D17, shared capabilities, permission catalog |
| 3 | BE-07/08 | DEPENDENCY_PENDING | PR #110 is draft at ce42e1362aef23f560b1c1a4f906ad58e4da4859; targeted locked evidence still requires review and positive acceptance |
| 4 | BE-09/17, BE-N11 | NOT_STARTED | Versioned scheme/calculation, typed results and publication readiness |
| 5 | BE-N01 | NOT_STARTED | Major-specific COLD checklist and immutable submission versions |
| 6 | BE-N02/03 | NOT_STARTED | Scoped COLD grading and readiness dashboard |
| 7 | BE-N04 | NOT_STARTED | Weekly demo rubric, primary-only scores and submitted history |
| 8 | BE-12/13/14 | NOT_STARTED | Evidence decisions, tracking-only checkpoint, defense scheduling |
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
  remain Admin-controlled. Existing pre-v5 Admin publication behavior is not yet
  changed by phase 1.
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

No migration or lifecycle mutation has been applied to shared `AI_PMS` by this
work. Run isolated tests first, obtain CI/review for each dependency PR, then
deploy compatible application/schema changes and enable features individually.
Do not infer readiness from table count, mock responses or this tracker.
