# Account security feature

BE-10 owns account lifecycle, self-service profile/password changes, role and
permission administration, refresh-token rotation, password-reset tokens and
security audit queries.

Routes are versioned under `/api/v1/users`, `/api/v1/auth` and
`/api/v1/security`. Administrative endpoints require the `ADMIN` role in both
the API policy and Application handlers. Access tokens are JWT bearer tokens;
refresh and password-reset values are opaque tokens whose SHA-512 hashes are
stored in SQL Server.

Do not place SMTP credentials, JWT signing keys or token values in committed
configuration or logs.
