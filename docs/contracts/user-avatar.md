# Personal avatar contract

The authenticated user alone manages and reads their image. These endpoints do not expose another account's avatar or change academic profile status. All responses are `Cache-Control: no-store`; image bytes are streamed from private file storage with `nosniff`.

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/v1/users/me/avatar/metadata` | Return `{hasAvatar, concurrencyToken, contentType, sizeBytes, updatedAt}`. With no image all optional fields are null. |
| GET | `/api/v1/users/me/avatar` | Return PNG/JPEG bytes, or 404 when absent. |
| POST multipart | `/api/v1/users/me/avatar` | `File` and optional `ConcurrencyToken`; token must match the current revision, or be omitted when no image exists. |
| POST JSON | `/api/v1/users/me/avatar/remove` | `{ "concurrencyToken": "..." }`; removing an already empty slot with a null token is idempotent. |

Upload accepts one non-empty `.png`, `.jpg` or `.jpeg` up to 2 MiB. Extension, MIME, signature, basic image envelope and PNG dimensions are checked server side. Invalid files return 400, size overflow 413, missing or inactive account 401/403, and stale revision 409 (`AVATAR_VERSION_CONFLICT`). Each successful update/remove records an audit entry. The image is private; the API never returns a storage key or public URL.

Database First release order: apply `db/changes/20261117_user_avatars.sql` through the existing controlled migration process, verify table/index and rerun safety, then deploy the API and FE together. Fresh databases use the same DDL in `db/schema.sql`; the isolated migration manifest includes the change. This source change does not apply the migration to a shared database.

Isolated SQL Server 2022 Testcontainers check passed on 2026-11-17: fresh schema, two migration reruns, preserved metadata, FK rejection and EF-backed upload/readback/remove. This does not establish the state of the configured shared database.
