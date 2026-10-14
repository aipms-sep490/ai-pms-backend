# Industry advisory (expansion PR6)

Baseline develop `3337e60` (PR #128). Backend/database only; Tin owns the new expert
enrollment/advisory screens. `Industry:Enabled=false` by default. No expert, real
invitation, or academic policy is seeded by deployment.

## Identity and invitation

Admin POST `/api/v1/admin/industry/invitations` takes `email`, `fullName`,
`organization`; GET is paged, POST `/{id}/revoke` revokes an unused invitation.
The response has invitation/expert IDs, email, status and expiry, never a secret.
Invitation status is not an SMTP delivery receipt. Existing delivery records track
actual attempts; SMTP acceptance does not prove mailbox receipt.

An expert has a dedicated profile, active user and no global roles. Existing
academic accounts cannot be converted or auto-linked. Role assignment refuses an
expert account; no LECTURER/evaluator is created. Unlinked expert invitations may be
reissued; all prior unused invites are revoked. Tokens expire after three days,
are single use, and have only SHA-512 hashes in the verifier column. The delivery
copy is encrypted with persisted ASP.NET Data Protection keys; it is erased after
consumption/revocation/expiry cleanup. Inbox content, audit and API DTOs have no token.

The existing notification/email delivery queue sends the secret only to the invited
mailbox after commit. `Industry:InvitationUrl` must be HTTPS (HTTP allowed only on
loopback) with no query, fragment or credentials. FE receives `#invitation=...`,
clears it from browser history and holds it only for the enrollment exchange. Do not
log tokens, put them in analytics/referrers or persist them in local storage.

POST `/api/v1/auth/industry/google/challenge` sets its own HttpOnly/SameSite=Strict
browser cookie and returns challengeId/clientId/nonce. POST `.../enroll` accepts
`challengeId`, nonce-bound Google `idToken`, and `invitationToken`. Use credentials
and a configured Google allowed Origin. Non-development requires HTTPS. The Google
verifier validates signature/issuer/audience/expiry/email_verified/nonce as for the
existing login. The email must exactly match (case-insensitive) the invite and user.
For non-authoritative Google domains, possession of the independently delivered
mailbox secret is the extra email ownership proof. No ordinary Google auto-link rule
is broadened; student enrollment remains unchanged. Concurrent exchange has one
winner. After enrollment ordinary linked-Google login returns no academic roles.
FE routes experts using `/industry/assignments`, not an invented role.

## Assignment, shares and feedback

All routes below use `/api/v1`. List pages are 1..1000, sizes 1..100; authorization
filters precede count/paging. Ordering is deterministic by ID (feedback by version).

| Route | Authority and contract |
| --- | --- |
| GET/POST `/projects/{id}/industry-assignments` | Frozen lead staff; POST requires ACTIVE/FINAL_SUBMISSION project, invited active expert, explicit COMMON/no major or MAJOR_SPECIFIC/frozen major, startsAt/endsAt (up to 366 days). Overlapping same-target assignments denied. |
| GET `/industry/assignments` | Expert's active assignments only; no project roster or grades. |
| GET `/industry/assignments/{id}` | Lead or enrolled active assigned expert; minimal project ID/code/title, target, effective dates, status, token and allowedActions. |
| POST `.../{id}/revoke` | Lead + concurrencyToken; also on closed projects to withdraw access. |
| GET/POST `.../{id}/evidence` | Lead shares deliverableVersionId + fileId + assignment token. Expert only lists currently shared files. |
| GET `.../{id}/evidence/{shareId}/download` | Recheck active account, assignment/time and share; stream private exact object, no public URL. |
| POST `.../{id}/evidence/{shareId}/revoke` | Lead + assignment token, including closed projects. |
| GET/POST `.../{id}/feedback` | Lead reads; expert submits expectedVersion, assignment token, content 1..10000. Append-only version; no draft overwrite or scores. |

COMMON advisors receive only explicitly declared COMMON files. Major advisors only
receive declared MAJOR_SPECIFIC files for their exact major. Unknown/legacy targets,
INDIVIDUAL files and unrelated project files are refused, even if a caller knows IDs.
When a version is in a locked final package, target, file metadata and storage key
come from that snapshot. Otherwise capture the declared deliverable version and
exact file reference at sharing time. The share remains bound to those values when
a later version or file metadata changes. Storage lifecycle is reused; share FKs
retain referenced metadata/content, including historical revoked shares.
Files are limited to 10 MiB for this portal; missing content returns 404.

Sharing and feedback require a live assignment and writable project. Closed project
history is read-only; expiry/revocation denies the expert on the next request even
with a valid JWT. Lead can read retained feedback. Mutations use the project
lock, persisted actor scope and optimistic token, with audit in the same transaction.
Share creation changes the assignment token: GET assignment before the next
mutation. Unshare/revoke return the new token. Feedback history preserves
all versions, with supersedesVersion. Feedback never enters evaluations, results,
readiness, peer, chat or AI prompts.

## Errors and examples

`400` invalid paging/target/date/content; `401` anonymous or invalid/replayed/expired
enrollment proof; `403` wrong lead/major/expert, disabled account, inactive assignment;
`404` feature off, unknown file/share/resource; `409` stale token/version, overlap,
duplicate share, closed project, existing identity, unknown exact provenance;
`503` missing Google/enrollment configuration. Do not infer a grant from JWT roles
or cached capabilities. Mutation rechecks current authority transactionally.

```json
{"expertId":901,"scope":"MAJOR_SPECIFIC","majorId":7,"startsAt":"2026-10-13T01:00:00Z","endsAt":"2026-11-13T01:00:00Z"}
```

```json
{"concurrencyToken":"<latest-assignment-token>","deliverableVersionId":301,"fileId":401}
```

```json
{"concurrencyToken":"<latest-assignment-token>","expectedVersion":0,"content":"Advisory usability feedback; not an academic score."}
```

## Validation and rollout

`IndustryEndpointTests` covers actual HTTP invitation/enrollment with fake Google
verifier, the real isolated delivery queue, assignment/share/download/feedback,
exact-version read after replacement, reissue/replay/expiry/browser binding,
no academic roles, foreign scope, disabled account, revocation, archived write denial,
audit rollback and concurrent single winners. Fixtures use synthetic users and
in-memory storage. This is not proof of a real Google browser or mailbox.
`SchemaParityTests` covers fresh bootstrap, upgrade, rerun and mapped columns.

Additive `20261013_add_industry_advisory.sql` introduces six tables, checks/FKs and
indexes with QUOTED_IDENTIFIER ON; bootstrap, manifest and mapping synchronized.
After CI/review merge, apply twice to shared AI_PMS, audit schema and row-count
invariants, deploy merged image with Industry off. Preserve previous image/data;
rollback through application/feature configuration. Enable only after Tin implements
the enrollment route, adapters and provider/browser acceptance. Configure email
worker and persisted Data Protection keys before invites.

A dedicated isolated scenario exchanges the enrollment response's issued JWT through the production account validator (no academic role) and verifies immediate 401 after account disable. Google identity remains a fake verifier; this does not claim live provider acceptance.

