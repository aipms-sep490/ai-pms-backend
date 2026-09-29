# Google login (backend contract)

Google is an alternative login for accounts already provisioned in AI-PMS. It never
creates users, assigns roles, verifies academic profiles, or requests Drive/Gmail
access. Password login and the existing LoginResponse/refresh/logout APIs remain.

## Configuration

Keep Google login disabled until the SQL migration has been applied:
`db/changes/20260926_add_google_external_logins.sql`. The bootstrap includes it and
the migration is rerunnable. No existing emails are automatically linked.

Create a Google OAuth **Web application** client with the intended frontend origins.
Use a separate client from the Drive storage client. Configure with .NET User Secrets
(the API project already has a UserSecretsId), or equivalent deployment variables:

```text
GoogleAuth:Enabled = true
GoogleAuth:ClientId = <web-client-id>.apps.googleusercontent.com
GoogleAuth:AllowedOrigins:0 = http://localhost:5173
```

Production origins must use HTTPS; HTTP is accepted only for loopback development.
Client secret and Google refresh token are not needed for Google Identity Services
ID-token login. Never reuse GoogleDrive credentials here. Deployment behind a reverse
proxy must securely preserve the original HTTPS scheme; never trust arbitrary forwarded
headers. The default is same-site FE/API (different localhost ports are supported).
Cross-site cookie deployment is not supported by this first implementation.

## Browser flow for a future frontend

No frontend files are changed in this PR. Use Google Identity Services' JavaScript
callback, with JSON fetches to the backend and `credentials: 'include'`. The browser
sets Origin. Do not use Google's HTML form POST mode or accept ID tokens from URL
parameters. Use a fresh challenge for each attempt; do not share challenges across tabs.

1. First login with the AI-PMS password to obtain the normal Bearer JWT.
2. POST `/api/v1/auth/google/challenge` with `{ "purpose": "LINK" }` and Bearer JWT.
   The response is `{ challengeId, clientId, nonce, expiresAtUtc }`. The API also sets
   an HttpOnly, SameSite=Strict browser-binding cookie (Secure outside Development).
3. Initialize Google Identity Services with that clientId and nonce. On its credential
   callback, POST `/api/v1/auth/google/link` with
   `{ challengeId, idToken: credential, currentPassword }` and the Bearer JWT.
   The verified Google email must match the persisted AI-PMS email (trim/case-insensitive;
   no Gmail dot/plus alias rewriting). Success is 204. A repeat link to the same subject
   is idempotent with a fresh challenge; a different subject/user binding returns 409.
4. Later logins use a LOGIN challenge (anonymous), then POST
   `/api/v1/auth/google/login` with `{ challengeId, idToken }`. The response is the
   existing LoginResponse including AI-PMS access/refresh tokens. Store these using the
   existing application session flow. Google tokens are not stored by the backend.
5. GET `/api/v1/auth/external-logins` with Bearer JWT returns only provider, email at
   linking and linkedAtUtc. It never exposes subject, tokens or other users' links.
6. POST `/api/v1/auth/google/unlink` with Bearer JWT and `{ currentPassword }` removes
   the link and revokes all outstanding refresh tokens, including password sessions.
   Existing access JWTs expire normally. Reauthenticate by password afterwards.

Unlink and listing remain available for recovery when Google login is disabled,
provided the allowed origins remain configured. Login only looks up the unique
GOOGLE subject, then checks account status, lockout and current email; matching an
unlinked email does not grant access. An email change requires password login and
unlink/relink. Academic PENDING does not prohibit authentication.

## Security and operations

Challenges expire after five minutes, are bound to purpose/browser (and user for LINK),
and are consumed atomically with the result. A successful challenge cannot be replayed.
Failed account/password decisions consume the challenge and record audit; infrastructure
or audit failures roll back the operation. A hourly worker removes expired challenges
older than one day while the feature is enabled; residual rows are inert while disabled.

Google signatures are verified against HTTPS Google JWKS, cached for one hour; an
unknown key triggers a throttled refresh. Only RS256, Google issuers, the configured
audience/presenter, current expiry/issued-at, verified email, subject and nonce are
accepted. Timeout is 10 seconds and provider failures return sanitized 503 errors.

POSTs require JSON and an exact allowed Origin. Auth rate limits apply. Bearer JWTs
retain the existing transport; the binding cookie is not an authentication session.
Errors use existing ProblemDetails: 400 malformed input, 401 invalid token/challenge
or unlinked identity, 403 inactive account/origin, 409 link conflict, 429 rate limit,
503 disabled/unavailable provider. Logs/audits exclude raw credentials and Google claims.

Tests use generated RSA signatures and a fake identity provider with isolated SQL
databases, never Google production credentials. Real Google smoke testing requires
an authorized browser account and configured Web client. Disable GoogleAuth to roll
back the feature without removing tables or breaking password authentication.
