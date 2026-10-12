# V5 expansion implementation register

Baseline: develop f479878 (PR #123). Backend/database only; Tin owns frontend PRs.

| Slice | Status | Evidence |
| --- | --- | --- |
| Working Agreement / frozen policy | BACKEND_READY | PR #124 / f796f95; CI 1050 unit + 1265 integration; shared migration rerun and deployment verified; flag off |
| Supervisor recommendations | IN_PROGRESS | v5-supervisor-recommendations.md; shared eligible candidates, deterministic advisory ranking |
| Peer evaluation rounds | NOT_STARTED | Evidence only, private submissions |
| Immutable result revisions | NOT_STARTED | Required before corrections |
| Appeals and two-person correction | NOT_STARTED | Lead opens windows; independent review/publication |
| Industry advisory | NOT_STARTED | Scoped invitations, Google enrollment, exact shared files |
| Joint acceptance / Tin handoff | NOT_STARTED | No INTEGRATED_READY claim |

Keep changes in separate dependency-ordered PRs from updated develop. Each slice
includes additive bootstrap/manifest/model synchronization, isolated SQL lifecycle,
authorization and concurrency tests, OpenAPI and handoff. Feature flags default off.
Never run destructive lifecycle on shared AI_PMS. No historical provenance backfill.

Frozen decisions: Admin controls the agreement requirement; policy default false;
recommendations use deterministic rules; peer is visible to author/current Primary/
scoped staff only; appeals require lead-opened windows and two-person decisions;
Industry is advisory only. No re-defense, course-grade conversion or weighted
industry scoring. These additions do not close pending P0/P1 browser/provider gates.
