# Automatic staging deploy from develop

The user requested automatic deployment when new code reaches the development branch. Both repositories use **`develop`**, not `dev`. This host-side pull agent checks each minute while WSL is running. It does not depend on Codex staying open and does not install a public self-hosted Actions runner.

## Release rules

1. Read the latest `develop` commit directly from each repository. Work in dedicated bare clones and disposable build directories; never pull/reset/stash the user's active checkout.
2. Require a successful **push** run of the correct CI workflow for that exact commit in the original repository. A pull-request run cannot authorize deployment. All current check runs must also have completed successfully (neutral/skipped accepted). A later failed/in-progress rerun prevents falling back to an earlier success.
3. Backend deploys before frontend. Compare backend contract example hashes with the frontend contract lock against the actual running counterpart. Incompatible cross-repository changes require a coordinated manual release.
4. Database migration changes, backend Docker/Compose changes, non-fast-forward history, host Compose drift, changed upload policy or frontend feature flags stop automatic rollout. No migration is automatically applied. Read-only general and academic schema checks run before backend build; missing schema retains the existing application.
5. Build the exact tracked source. Frontend uses Node 24 and pinned pnpm in a local Docker builder and the existing staging build script. The emitted metadata must retain every deployed flag and the API URL, preventing a lost progress/risk tab. Backend embeds the full commit SHA.
6. Before backend replacement, create a SQL COPY_ONLY/CHECKSUM backup, verify it, and copy it outside the SQL container with matching SHA-256. Keep all private config, volumes, Drive credentials and storage provider unchanged.
7. Atomically update only image tags/revision in the existing Compose `.env`, recreate only the relevant service, then verify image, health, exact revision and upload/feature policies. Keep the previous image and environment for rollback.
8. On failed verification, restore the previous image/environment. An on-disk transaction journal recovers an interrupted rollout on the next tick. A failed candidate is not repeatedly redeployed; inspect the reason and clear its attempt record only after resolving it. All agent processes share an exclusive lock.

## Installation on the current WSL host

Build the non-secret tool image:

```bash
docker build -f deploy/auto/Dockerfile.builder -t aipms-deploy-node:24-pnpm11 deploy/auto
python3 deploy/auto/install.py
python3 ~/.local/lib/aipms-deploy/agent.py --config ~/.config/aipms/autodeploy.json --dry-run
systemctl --user daemon-reload
systemctl --user enable --now aipms-deploy.timer
```

The installer freezes the current Compose hashes, 25 FE flags, API URL and upload policies; it refuses to overwrite an existing configuration. The Windows GitHub CLI uses the existing OS credential store, tested through WSL user systemd; no token is copied into source/config. If it loses authentication or network access, existing services remain unchanged. Install after reviewing and testing the source; later changes to `deploy/auto` do not automatically replace the installed agent itself.

Enable user lingering if this machine should run the timer without an open WSL shell (`loginctl enable-linger khaine161`, administrator operation). Windows must be awake, WSL/Docker/tunnel must run and internet must be available. This is the same availability requirement as the current website, not an external always-on server.

## Inspect, pause and resume

```bash
systemctl --user status aipms-deploy.timer
cat ~/.local/state/aipms-deploy/status.json
journalctl --user -u aipms-deploy.service -n 30 --no-pager
systemctl --user stop aipms-deploy.timer
systemctl --user start aipms-deploy.timer
```

`status.json` records up-to-date, waiting-for-CI, deployed or blocked per service. Build logs, rollback environment and database backups are private under `~/.local/state/aipms-deploy/releases`. Do not publish private files. No email/Slack messages are sent. Stopping the timer prevents future ticks; let an active release finish before manual deployment.

After resolving a failed build, inspect `api-attempt.json`/`frontend-attempt.json`, stop the timer, archive that record, and run one tick manually to retry. To intentionally change feature flags/upload policy/Compose, do a reviewed manual release and update the local baseline; do not silently weaken the guards.

Backups and images are retained rather than automatically pruned. Monitor disk capacity and rotate verified old backups/images manually. The agent does not restore SQL on application rollback, because it never applies schema/data migrations. Every accepted version must remain compatible with the current database and Drive metadata.

## Validation

```bash
python3 -m unittest discover -s deploy/auto -p test_agent.py -v
```

Tests cover exact successful-CI selection, stale reruns, pending checks, migration guards, flag/API preservation, environment preservation, failed-health rollback and interrupted-transaction recovery. Runtime verification is recorded separately: dry-run GitHub selection, systemd execution, real image build, actual replacement, metadata/health and rollback evidence. Unit fixtures are not browser UAT.
