# AI-PMS on WSL and the existing Cloudflare Tunnel

Target: `https://api-staging.khaidz.com`, Ubuntu-24.04, Linux user `khaine161`
(UID/GID 1000), and the existing `AI_PMS` database in container `mssql`.
This deployment uses live shared data. A change through the deployed API is also
visible to the local backend. The setup does not seed, migrate, reset or copy SQL.

## 1. Prepare private configuration in Windows PowerShell

From the backend checkout:

```powershell
cd 'D:\hocdiiiii\tốt nghiệp\code\be-video-meeting'
./scripts/prepare-wsl-config.ps1
```

The script reads the existing API User Secrets without printing their values.
It copies only the SQL connection, JWT settings and LiveKit credentials. It
changes the SQL host to `mssql,1433`, preserves `Database=AI_PMS`, and writes:

```text
/home/khaine161/.config/aipms/appsettings.Staging.json
```

The containing directory has mode 700; the JSON has mode 600. The image runs
as UID/GID 1000 and mounts this file read-only. Nothing secret is put in Git,
the image, Compose environment variables, or command-line arguments.
If the file already exists, the script stops. `-ReplaceExisting` explicitly
regenerates it and resets manual edits to the generated settings.

Enabled: password login and video provider integration. Disabled initially:
Google login, password recovery, notification email and scheduled reminders.
This prevents the new instance from independently sending shared-database mail
before its SMTP/key-ring setup is reviewed. The video cleanup worker DOES run
when video is enabled and can process shared cleanup jobs using the copied
LiveKit project credentials.

The JSON can be edited locally in WSL with:

```bash
nano ~/.config/aipms/appsettings.Staging.json
```

Keep `Cors:AllowedOrigins` as exact FE origins. `http://localhost:5173` is already
included. Add the deployed FE origin when available, with no trailing slash.
The public API hostname is not a substitute for the FE origin.

## 2. Connect the API without replacing existing services

Run the remaining commands in Ubuntu WSL:

```bash
cd '/mnt/d/hocdiiiii/tốt nghiệp/code/be-video-meeting'
cp -n deploy/wsl/.env.example deploy/wsl/.env
sh deploy/wsl/prepare-network.sh
docker-compose --env-file deploy/wsl/.env -f deploy/wsl/compose.yaml config --quiet
```

This machine has the standalone `docker-compose` v2 command. The preparation
script creates `aipms-data` if needed and attaches the EXISTING `mssql` container
to it. Its current network and published port remain intact. The API joins
`aipms-data` for SQL and `go-exe_goexe-net` for the existing Cloudflare tunnel.
No new SQL, Redis or tunnel containers are created.

Do not use the root development `docker-compose.yml` for this deployment.
The SQL container currently has no mounted data volume; do not remove/recreate
it. Maintain a verified database backup separately before changing that service.

## 3. Preserve shared file storage and keys

`deploy/wsl/.env` contains paths only, not secrets. Its default upload bind mount
is the Windows backend's existing local storage directory:

```text
/mnt/c/Users/khaih/AppData/Local/AIPMS/private-files
```

Both backend instances must read/write this same directory while using the same
database. An empty replacement volume would make existing file metadata point
to missing files. Ensure the directory exists and UID 1000 can access it. If the
Windows API uses a custom `FileStorage:RootPath`, change `AIPMS_FILES_PATH` to its
WSL path before starting. This setup assumes the verified Local storage provider;
do not switch to Drive without migrating/configuring the matching file store.

Docker named volumes `aipms_keys` and `aipms_logs` persist keys/logs across API
rebuilds. The container root filesystem is read-only. Never use `down -v` for
routine upgrades. If password recovery is later enabled on multiple instances,
they must share its Data Protection key ring, application name and lookup key;
a new independent key ring cannot decrypt another instance's queued payloads.

## 4. Build and start only AI-PMS

```bash
docker-compose --env-file deploy/wsl/.env -f deploy/wsl/compose.yaml build api
docker-compose --env-file deploy/wsl/.env -f deploy/wsl/compose.yaml up -d --no-deps api
docker-compose --env-file deploy/wsl/.env -f deploy/wsl/compose.yaml ps
curl --fail http://127.0.0.1:5088/api/v1/system
```

Expected: `{"name":"AI-PMS API","version":"v1","status":"ok"}`.
This is liveness only, not database readiness. Then verify existing-account
login and an authorized read against `AI_PMS`; avoid destructive test suites
against this shared database. Use the local Swagger UI:

```text
http://localhost:5088/swagger/index.html
```

The API publishes only loopback port 5088. Existing websites using ports 8080,
3000 and 3333 are unaffected. No router port-forwarding is needed for the tunnel.
Windows-to-WSL localhost forwarding must be enabled to use the Windows URL;
the WSL curl check works independently of that setting.

## 5. Add the Cloudflare hostname

In Cloudflare Zero Trust, open Networks > Tunnels (or Connectors), select the
existing tunnel used by `goexe-tunnel`, then add a Published application route:

| Field | Value |
| --- | --- |
| Subdomain | `api-staging` |
| Domain | `khaidz.com` |
| Path | Leave empty |
| Service type | `HTTP` |
| Service URL | `aipms-api:8080` |

Keep existing routes. If the hostname already has a DNS record, inspect it before
replacing it. Enable HTTPS at the Cloudflare edge and bypass caching for this API
hostname. Do not add a browser challenge or interactive Access login to the
LiveKit webhook path. Authentication on API resources remains enforced by AI-PMS.

Do not use `localhost:5088` as the tunnel service: localhost inside the tunnel
container means the tunnel itself. Docker DNS resolves `aipms-api` on the shared
network.

Verify:

```bash
curl --fail https://api-staging.khaidz.com/api/v1/system
```

Public Swagger: `https://api-staging.khaidz.com/swagger/index.html`.
API base for FE: `https://api-staging.khaidz.com/api/v1`.

## 6. Proxy trust and integration acceptance

`ReverseProxy:KnownProxies` contains only the inspected tunnel container IP.
The backend accepts `CF-Connecting-IP` and `X-Forwarded-Proto` from that address,
before authentication/rate limiting; other peers cannot spoof these headers.
Keep Cloudflare's real visitor-IP header enabled. No forwarded Host is trusted.
After recreating the tunnel, inspect its IP again; if changed, update the private
JSON and recreate only the API container. Do not enable the blanket
`ASPNETCORE_FORWARDEDHEADERS_ENABLED` setting or trust all networks.

Google remains disabled: its browser-binding cookie uses SameSite=Strict.
An FE on `http://localhost:5173` and API on this HTTPS domain are cross-site.
Google acceptance needs a suitable same-site HTTPS FE (or a separately reviewed
cookie/proxy design), Google OAuth origins and configured CORS. Password/JWT
login continues to work from the configured localhost FE.

LiveKit webhook URL:

```text
https://api-staging.khaidz.com/api/v1/integrations/video/livekit/webhook
```

Configure it in the same LiveKit Cloud project as the API credentials, with the
matching signing key. The verifier accepts the raw Authorization JWT (and legacy
Bearer form), validates HS256/issuer/expiry/not-before, and checks the standard
Base64 SHA-256 checksum against the unmodified body. An unsigned request returning
401 proves rejection, not successful delivery. Provider smoke evidence and its
limits are documented in `video-webhook-staging-verification.md`. Room webhook
delivery alone does not certify participant presence or joint media acceptance.

## 7. Updates and operations

To load a changed private configuration (including an atomically replaced JSON),
recreate the API so Docker mounts the new file:

```bash
docker-compose --env-file deploy/wsl/.env -f deploy/wsl/compose.yaml up -d --no-deps --force-recreate api
```

For source updates, build first, then recreate. Set `AIPMS_IMAGE_TAG` to a unique
release tag before building so the previous image remains available. To roll
back, restore that previous tag and use `up -d --no-build --no-deps api`; database
rollback is not performed by these files. Do not run global Docker prune/down
commands that could affect the other websites.

`restart: unless-stopped` works after the Docker daemon starts. It does not keep
Windows awake, start WSL after login, or restore the internet connection. Configure
those machine settings for the availability the team needs.

Troubleshooting:

| Symptom | Check |
| --- | --- |
| Cloudflare 502 | API health, shared network, service `aipms-api:8080` |
| Cloudflare 1033 | Existing tunnel connector is online |
| API exits at startup | Private config file mount, SQL/JWT/LiveKit settings |
| SQL connection fails | `mssql` membership in `aipms-data`, existing SQL credentials |
| Files missing | Shared upload path, not a fresh empty directory |
| All logins share a rate limit | Tunnel IP and real visitor-IP forwarding |
| Google fails despite CORS | HTTPS and SameSite requirements above |
| LiveKit webhook 401 | Signing key, unmodified body/Authorization, server clock and deployed verifier version |

## Preparation verification (2026-10-04)

- Release image `aipms-api:wsl-prep` built successfully using the Dockerfile.
- Nine trusted-proxy tests passed, including spoofing from an untrusted peer.
- Compose configuration and shell syntax checks passed.
- Private WSL configuration was generated with permissions 600, owner 1000:1000.
- An isolated, non-root, read-only probe container returned `/api/v1/system` OK
  and Swagger HTTP 200. Its network was disabled and video was overridden off.
  The probe was stopped and removed afterward.
- The persistent API container, public Cloudflare route, database mutations and
  real provider acceptance were NOT performed as part of this preparation.
