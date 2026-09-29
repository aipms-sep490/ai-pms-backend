# Google Drive and SMTP operations

AI-PMS uses Google Drive as a server-side file provider and SMTP for notification
and password-reset delivery. A user's Google login never grants access to Drive or
Gmail. These providers use separate credentials and remain configured in User Secrets
or the deployment secret store.

## Google Drive

When `FileStorage:Provider=GoogleDrive`, all of these values are required:

```text
GoogleDrive:ClientId
GoogleDrive:ClientSecret
GoogleDrive:RefreshToken
GoogleDrive:FolderId
GoogleDrive:TimeoutSeconds=60
```

`FolderId` is mandatory. The application never searches for or creates an `AI-PMS`
folder and never changes sharing permissions. Every list/upload/download/delete query
includes the configured folder as a parent. Startup validation rejects missing or
missing credentials or malformed folder ID (credential validity is checked remotely).
Run `pwsh scripts/Test-ExternalIntegrations.ps1` to check User Secrets/environment
configuration without displaying its values. Add `-CheckRemote` to read folder metadata
and verify SMTP STARTTLS/certificates. It sends no messages, uploads no files, and
does not authenticate to SMTP. Follow with an authorized delivery smoke test.

Storage keys remain server-generated 32-character hexadecimal values. Upload is
create-only, checks for an existing key inside the configured folder, and has a bounded
per-key lock inside the process. Duplicate objects are reported for manual
reconciliation. The application deliberately does not retry an upload after an
ambiguous response because that could create a second file. A timeout reports that the
remote result may be unknown; callers must not blindly retry. Provider exceptions are
sanitized and do not expose Google response bodies or tokens. Cancellation from the
caller remains cancellation. Download results are buffered and rewound before the API
reads them; missing files remain `FileNotFoundException`.

`Google.Apis.Drive.v3` uses the least scope needed by the existing storage account:
`DriveFile`. Keep the Drive refresh token separate from `GoogleAuth` and rotate it if
the provider reports `invalid_grant`.

## SMTP

Existing Gmail SMTP delivery remains the transport; Gmail API is not added. Configure:

```text
Email:Host=smtp.gmail.com
Email:Port=587
Email:EnableSsl=true
Email:SenderAddress=...
Email:SenderName=AI-PMS
Email:Username=...
Email:Password=...
Email:TimeoutSeconds=30
NotificationEmail:Enabled=true
```

Use a Gmail App Password or an SMTP credential issued for the deployment. Do not put
it in tracked files or send it in an API request. When notification email is enabled,
startup validation requires TLS, host, sender, username and password plus a bounded
timeout. Password-reset delivery uses the same timeout but keeps its generic response
so account existence is never disclosed.

Notification queue semantics are unchanged: only a confirmed transport success is
marked `SENT`; SMTP errors and timeouts remain retryable. A timeout can mean the remote
server accepted the message before the client lost the response, so retrying can result
in duplicate email. The worker's existing exponential retry and maximum-attempt rules
apply. Password-reset errors are logged with recipient domain only and do not expose
the token; notification errors omit message content and provider details.

## Verification and rollout

CI uses fake Drive/SMTP adapters and never contacts Google or sends mail. For a real
smoke test, use a dedicated Drive folder and a test recipient, verify upload/list,
download and delete, then revoke/restore the refresh token and confirm sanitized
failure. Set `FileStorage:Provider=Local` or `NotificationEmail:Enabled=false` to
disable a provider without affecting core authentication or in-app notifications.
