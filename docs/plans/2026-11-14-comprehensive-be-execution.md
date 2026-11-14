# BE execution plan for the comprehensive AIPMS audit

**Target:** `feat/be-access-audit-20261113`. **Source:** audit reports `04_AIPMS_FE_BE_AUDIT.md`, `07_IMPLEMENTATION_DECISIONS.md`, and `08_DETAILED_BACKLOG.md` in the parent workspace, checked against the current code. The FE branch is `feat/fe-fresearch-reference-20261113`. This plan does not change the existing Capstone state machine or academic rules.

## Backend impact by audit item

| Item | BE disposition | Evidence and next action |
| --- | --- | --- |
| Q-01, UX-01–08, UX-10–12 | Existing API; no new BE feature for presentation | FE owns the route census, role layout, PDF identity, loading and mutation states. Validate responses against each role; open a BE defect only with a reproducible contract mismatch. |
| Access-event and audit-log read | Implemented in this slice | Access events already exist on this branch. Verify persisted active Admin role before reading sensitive audit rows, even if an older JWT still names Admin; disable response caching. Unit-test revocation and inactivity. |
| SCOPE-01 Period/supervisor directories | Decision pending | Existing authenticated directory queries may intentionally be school-wide. Obtain the policy for organization and department visibility before changing a query that registration and assignment flows depend on. Once decided, enforce scope server-side for list **and detail** and test cross-organization/cross-department callers. FE filters do not establish authorization. |
| UX-09 room/schedule | Separate BE-16 integration gate | The room/scheduling code is on a different branch. Compare its exact contract and ancestry with this target before any integration; do not silently copy branch-only endpoints or invent publish states. |
| GEO-01 access geography | Data and policy gate | Existing audit IP is not a location, and login/logout is not presence. Do not emit coordinates, city labels or online counts until a permitted, licensed location source, accuracy/retention policy, audience, historical transfer rule and department scope are specified. Then implement an aggregate-only server query with unknown count, scope tests and an accessible FE list/map from the same snapshot. |
| SKIP-01 | No BE work | LMS attendance, password class, WiFi tracking, facial recognition and unrelated AI features are outside the Capstone requirements. |

## Ordered delivery and acceptance

1. **Access audit hardening (this branch):** active persisted Admin check before audit read; staff and revoked Admin denied; account/network data not cached. Run focused and full unit tests. This is code-level evidence, not authenticated runtime acceptance.
2. **Scope decision:** record an explicit role-by-organization-by-department matrix. If visibility is restricted, add actor-derived filters in repository queries, cover both list and detail endpoints, count/paging consistency and direct-URL denial. Avoid FE-only security filters.
3. **Geography decision:** specify purpose, data source, license, city/region precision, VPN/private/unknown handling, retention and historical department-transfer behavior. Only then define the API contract and implementation. Any online metric needs an independent session/presence source.
4. **Integration verification:** run authenticated Admin/staff/student API checks and readback against an isolated or approved environment, then verify the corresponding FE branch. Do not label fixture tests as production UAT.

**Delivery boundary:** commit and push this BE branch only. No PR, merge, rebase, shared-DB write or production location lookup is part of this slice.
