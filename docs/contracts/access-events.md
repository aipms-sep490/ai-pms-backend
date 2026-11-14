# Admin access events (read-only)

`GET /api/v1/security/access-events` is an Admin-only, paginated projection of existing audit records. It does not represent active sessions or geographic location.

Query parameters: `search` (current account name/email, max 255), `userId`, `eventType` (`LOGIN`/`LOGOUT`), `outcome` (`SUCCESS`/`FAILURE`/`DENIED`), `fromUtc`, `toUtc`, `page`, `pageSize`. Dates are UTC; `fromUtc` and `toUtc` are inclusive. Validation follows the existing paging rules. Results are ordered by `occurredAt` descending, then ID descending; filters are applied before count and pagination.

Source audit actions: `AUTH_LOGIN`, `AUTH_GOOGLE_LOGIN`, `AUTH_LOGOUT`. Other security actions remain available through `/api/v1/security/audit-logs`. Failed login for an unknown email can have no `userId`; the API deliberately does not store or reconstruct the submitted email. Display name/email are the **current account values** when an actor account still exists, not a historical snapshot.

Response page items contain `id`, `userId`, `fullName`, `email`, `eventType`, `authenticationMethod` (`PASSWORD`/`GOOGLE`/`UNKNOWN`), `outcome`, `ipAddress`, and `occurredAt`. IP is an audit network address, not a user location. User agent, audit details JSON, token information, coordinates, and online status are excluded. The endpoint requires `AccountSecurityManagement` at the controller and verifies Admin in the query handler. Department staff receive 403.

Before reading audit rows, the handler checks the actor's current active account and persisted Admin role. A token issued before Admin revocation cannot continue reading access events. The same check protects the general audit-log query. Responses disable caching because they contain sensitive account and network metadata.

This feature reads the existing Database First schema and adds no migration or background worker. Authenticated runtime readback and role checks remain necessary before release; unit tests cover filtering, pagination, unknown actor, validation, and non-Admin denial.
