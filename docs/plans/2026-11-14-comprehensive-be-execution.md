# BE execution plan for the comprehensive AIPMS audit

**Target:** `feat/be-access-audit-20261113`. **Source:** audit reports `04_AIPMS_FE_BE_AUDIT.md`, `07_IMPLEMENTATION_DECISIONS.md`, and `08_DETAILED_BACKLOG.md` in the parent workspace, checked against the current code. The FE branch is `feat/fe-fresearch-reference-20261113`. This plan does not change the existing Capstone state machine or academic rules.

## Backend impact by audit item

| Item | BE disposition | Evidence and next action |
| --- | --- | --- |
| Q-01, UX-01–08, UX-10–12 | Existing API; no new BE feature for presentation | FE owns the route census, role layout, PDF identity, loading and mutation states. Validate responses against each role; open a BE defect only with a reproducible contract mismatch. |
| Access-event and audit-log read | Implemented in this slice | Access events already exist on this branch. Verify persisted active Admin role before reading sensitive audit rows, even if an older JWT still names Admin; disable response caching. Unit-test revocation and inactivity. |
| Role changes after token issue | Implemented in the next slice | Token validation checks every claimed role against current persisted assignments. A removed role invalidates the old token for all endpoints; acquiring a new role requires a refreshed token before it can be used. Preserve the existing active-account/password-version checks. |
| SCOPE-01 Period/supervisor directories | Implemented on this branch | User decision: non-Admin readers see semesters, Project Periods and supervisors in their active organization; Department Staff see supervisors only in their own department. Admin remains global. Actor scope comes from persisted DB assignments, not a client filter. List count/paging and detail reads apply scope server-side; foreign detail returns 404, a staff request for a foreign department returns 403. |
| UX-09 room/schedule | Separate BE-16 integration gate | The room/scheduling code is on a different branch. Compare its exact contract and ancestry with this target before any integration; do not silently copy branch-only endpoints or invent publish states. |
| GEO-01 access geography | Cancelled by user on 14/11/2026 | No map, coordinates, location inference or online-count feature will be implemented. Keep the existing access-event list and its role checks; IP is not a location or presence signal. |
| SKIP-01 | No BE work | LMS attendance, password class, WiFi tracking, facial recognition and unrelated AI features are outside the Capstone requirements. |

## Ordered delivery and acceptance

1. **Access audit and role-token hardening (this branch):** active persisted Admin check before audit read; staff and revoked Admin denied; account/network data not cached. Reject a token carrying a role removed from DB. Run focused and full unit tests. This is code-level evidence, not authenticated runtime acceptance.
2. **Scope implementation:** verify actor-derived organization and staff department filters in repository queries, including list totals and direct detail URLs. FE must label this scope honestly and treat 403/404 as server decisions.
3. **Cancelled geography item:** do not schedule map or location work. Preserve the access-event list without deriving geographic or online claims from IP or login/logout.
4. **Integration verification:** run authenticated Admin/staff/student API checks and readback against an isolated or approved environment, then verify the corresponding FE branch. Do not label fixture tests as production UAT.

**Delivery boundary:** commit and push this BE branch only. No PR, merge, rebase, shared-DB write or production location lookup is part of this slice.

## Verification on 14/11/2026

- `SupervisorEndpointTests`: 12/12 passed against an isolated SQL Server Testcontainer. The added test covers organization and department scope for supervisors, semester/period list counts, and direct detail URLs. It does not use the shared `AI_PMS` database.
- `AIPMS.UnitTests`: 1,296/1,296 passed. `AIPMS.Api` Release build passed with no warnings or errors.
- These checks establish controlled integration behavior on this branch, not staging or production UAT. UX-09 remains dependent on the separately governed BE-16 branch.
