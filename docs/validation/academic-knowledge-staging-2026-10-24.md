# Academic assistant staging rollout

Date: 2026-10-24, Asia/Saigon. Application image: `aipms-api:066f588-academic`.
Backend branch: `feat/academic-ai-assistant`.

## Deployed

- Additive migration `20261023_add_academic_knowledge.sql` applied to `AI_PMS`.
- Six tables: documents, versions, chunks, index jobs, publications, query runs.
- Migration manifest and fresh bootstrap synchronized; isolated schema parity,
  upgrade, and rerun checks passed. Shared knowledge foreign keys are trusted.
- Three published, explicitly labelled DEMO documents seeded for the active
  semester `DST-20261024`. They contain no invented dates or approved policies.
  Projects in other semesters do not inherit these documents.
- `AiAssistant:KnowledgeEnabled=true` in the private staging configuration.
- Provider: OpenAI-compatible 9Router, model `cx/gpt-6-luna`; key stays private.
- Local frontend `.env.local`: `VITE_ENABLE_AI_ACADEMIC_ASSISTANT=true`.
  Committed example defaults remain off.

## Verification

- Release build: zero warnings/errors with warnings as errors enabled.
- Unit tests: 1,082 passed, including provider request/error/cancellation tests.
- SQL tests: 10 passed (academic knowledge lifecycle/security/fallback/seed and
  fresh-upgrade-rerun schema parity). Destructive tests used owned isolated DBs.
- Frontend: typecheck and production build passed; 20 AI tests passed. Existing
  bundle size warnings remain.
- Real password login with a dedicated test-only student, then public
  `POST /api/v1/academic/assistant/ask`: HTTP 200, `COMPLETED`, grounded response
  distinguishing rubric and scheme, DEMO disclaimer, versioned citations.
- Browser lifecycle at `http://localhost:5175/academic/assistant`: login, submit,
  answer and source rendering verified on desktop and 390px mobile. No page
  errors or horizontal overflow. Saved run 2.
- An unrelated query returned `INSUFFICIENT_EVIDENCE`, zero citations, saved run 3.
- Student management request and unauthorized project-scoped question: HTTP 403.
- DB readback: runs 1, 2, 3 each have one `ACADEMIC_ASSISTANT_ASKED` audit event.
- Sanitized screenshots and response summaries are in the workspace's
  `outputs/academic-rollout-20261024/` directory; no tokens/passwords included.

## Usage and limits

- Students: `/academic/assistant`, or the academic panel on `/project/ai`.
- Admin/department staff: `/academic/knowledge`. Create text drafts, select the
  department/semester/period, add versions, publish, retire, reindex, view history.
- School-wide documents require a semester identifying the organization.
- PDF/DOCX extraction and automatic synchronization of rubric/scheme/gate data
  are not included. Source documents must be entered as text/JSON.
- DEMO sources are technical test data, not academic approval. Replace them with
  authorized institutional source text before relying on official answers.
- Keyword retrieval is bounded; a question may return insufficient evidence.
- Admin/staff management is covered by SQL and UI unit tests, not a full browser
  acceptance with institutional accounts. Full original-plan acceptance remains
  separate from this limited staging rollout.

## Backup and rollback

Verified COPY_ONLY/CHECKSUM backup before migration:
`/var/opt/mssql/backup/AI_PMS_before_academic_20261024_144829.bak` in SQL container.
An additional mode-600 copy is outside the container:
`/home/khaine161/.config/aipms/AI_PMS_before_academic_20261024_144829.bak`.

Previous config:
`/home/khaine161/.config/aipms/appsettings.Staging.json.before-academic-20261024`.
Previous image: `aipms-api:196690b-router`.

Rollback by disabling the knowledge feature/frontend flag, restoring the private
configuration, selecting image tag `196690b-router` in `deploy/wsl/.env`, and
recreating only the API with `--no-build --no-deps --force-recreate`. Preserve new
tables and history; do not restore/delete shared database data for app rollback.

9Router must stay running on Windows. Its WSL host address may change after a
network restart; verify reachability before updating the private endpoint.
