# Project assistant router deployment

Deployment date: 2026-10-24 (Asia/Saigon).

- Baseline: `225cfcc` (previous staging image).
- Application commit: `196690b`.
- Running image: `aipms-api:196690b-router`.
- Provider: OpenAI-compatible HTTP, routed through local 9Router.
- Configured model: `cx/gpt-6-luna`.
- Academic knowledge feature remains disabled. This release contains only the
  provider integration, not the unfinished academic knowledge module.
- No database migration or seed was applied.

## Verification

- Release build with warnings as errors: passed.
- AI tests: 87 passed, including configured endpoint/model/token, unauthorized
  response, and cancellation tests for the provider.
- Complete unit suite: 1,082 passed.
- Container health: healthy.
- Public `/api/v1/system`: HTTP 200, status `ok`.
- A request from the recreated API container, using its private staging
  endpoint/key/model configuration and an eight-second timeout, returned
  `AI_PMS_STAGING_ROUTER_OK` from model `gpt-6-luna`.
- Authenticated project assistant acceptance is pending: old documented demo
  credentials returned HTTP 401. The container request proves provider
  connectivity, not the authenticated API/browser lifecycle or academic RAG.

## Configuration and rollback

Secrets stay in `/home/khaine161/.config/aipms/appsettings.Staging.json`, mode 600,
mounted read-only. No key is stored in this document, Git, or the image.
The previous configuration is preserved beside it as
`appsettings.Staging.json.before-router-20261024` (mode 600).

The existing deployment checkout's `deploy/wsl/.env` selects the new image.
To roll back, restore the preserved private configuration, set the image tag
back to `225cfcc`, and recreate only the API service using the existing
`deploy/wsl/compose.yaml` with `--no-deps --no-build --force-recreate`.
Do not remove data volumes or recreate SQL Server.

9Router must remain running on Windows. The configured Windows WSL interface
address can change after WSL/network restarts; recheck container connectivity
and update the private endpoint if necessary. Provider failure continues to
use the existing project assistant fallback.
