# Password recovery

The password recovery flow keeps the existing frontend contract:

- `POST /api/v1/auth/forgot-password` with `{ "email": "..." }` returns `202` after the request is durably queued.
- `POST /api/v1/auth/reset-password` with `{ "token": "...", "newPassword": "..." }` returns `204` once the one-time token is consumed.
- `POST /api/v1/auth/change-password` with `{ "currentPassword": "...", "newPassword": "..." }` returns `204` for an authenticated user.

The forgot-password response is the same for existing, missing, and inactive accounts. The worker resolves the account after enqueueing, so the API does not reveal account existence. A `503` means that the request could not be accepted because recovery is disabled or not ready.

## Configuration

Recovery is disabled by default. Configure secrets outside Git:

```ini
PasswordRecovery__Enabled=true
PasswordRecovery__LookupKey=<at-least-32-random-characters>
PasswordRecovery__KeyRingPath=<persistent-absolute-directory>
Email__PasswordResetUrl=https://frontend.example/reset-password
```

The existing SMTP settings must also be present: `Email__Host`, `Email__Port`, `Email__EnableSsl`, `Email__SenderAddress`, `Email__SenderName`, `Email__Username`, `Email__Password`, and `Email__TimeoutSeconds`. Local development may use the loopback HTTP URL `http://localhost:5173/reset-password`; deployed environments require HTTPS. The key-ring directory must survive restarts and be shared by all API instances.

Generate `LookupKey` from a cryptographic random source (at least 32 random bytes, encoded as Base64), store it in User Secrets or a secret manager, and use the same value on every instance. Do not rotate it while requests or unexpired tokens exist; disable recovery and drain/expire them first. It prevents a database-only reader from dictionary-matching queue email fingerprints. Restrict key-ring directory access to the API service identity and protect the storage at rest; possession of both the database and the key ring permits decrypting outstanding links. Keep the key ring in backups and outside the repository.

Do not enable EF sensitive-data logging or HTTP request/response body logging for authentication routes. Diagnostics intentionally use request IDs and fixed error codes, never SMTP exception bodies or token payloads.

## Delivery behavior

`password_recovery_requests` stores only an HMAC email fingerprint and an ASP.NET Data Protection payload. The raw reset token is never stored in plaintext. The worker claims rows with a lease, creates one hashed `password_reset_tokens` row, sends outside the SQL transaction, and retries failures after 30 seconds, 1 minute, 2 minutes, and 4 minutes. A request expires after 30 minutes and is retained as metadata for seven days.

SMTP timeouts have an unknown delivery outcome. The worker may retry and a recipient can receive a duplicate email, but each reset token remains one-time use. A newer request supersedes older pending, retrying, sending, or sent requests. Changing or resetting the password invalidates active reset tokens, recovery requests, and all refresh sessions.

The additive `users.password_recovery_invalid_before` timestamp rejects requests that began before a password change but were queued after it. It is separate from the second-resolution `password_changed_at` JWT version to avoid ambiguity for changes within one second. A send already in progress cannot be recalled; reset still validates the account, token and request again under the user lock.

Defaults: 5-second poll interval, 20 requests per sweep, 120-second lease, maximum 5 attempts, 30-second SMTP timeout. Recovery runs independently of `NotificationEmail:Enabled`. Multiple workers use lease tokens; a stale worker cannot acknowledge a lease acquired by another instance. Terminal rows immediately erase their protected payload. Hourly cleanup deletes terminal metadata older than seven days.

## Rollout and verification

Apply `db/changes/20261001_add_password_recovery_queue.sql` before enabling the worker. Run unit and SQL integration tests with a fake SMTP transport. For the real smoke test, use a dedicated test account and mailbox: request a link, open the existing frontend page, reset the password, verify the old password and old sessions fail, and verify the link cannot be reused. Do not log SMTP credentials, raw reset tokens, protected payloads, or reset URLs.

Deploy in this order: apply the additive SQL migration, deploy backend code, configure SMTP/URL/key ring/lookup key, then enable recovery and restart the API. The new user column is read by the updated backend even when recovery is disabled, so the migration must precede code deployment. Roll back operationally by disabling `PasswordRecovery:Enabled`; keep the migration and keys. Password login and authenticated password changes continue working.

The frontend handoff stays pending real-mailbox acceptance until receipt and the browser flow have been demonstrated. SMTP acceptance alone is not inbox delivery proof. No frontend changes or email verification step are required for authenticated password changes.
