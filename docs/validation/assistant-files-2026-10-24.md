# Assistant images, documents and answer downloads

## Contract and scope

- Existing JSON `POST /api/v1/academic/assistant/ask` remains compatible.
- `POST /api/v1/academic/assistant/ask-with-files` accepts multipart fields
  `query`, `scope`, optional `projectId`, and repeated `files`.
- `GET /api/v1/academic/assistant/runs/{id}/download?format=txt|md` exports a
  persisted answer, limitations and source references. It checks active account,
  run ownership and current project access before every download; adds an audit.
- New response `answerType=ATTACHMENT` identifies user-uploaded evidence.
  `attachments` contains the filenames/media types actually cited by the model.
  Uploaded documents cannot qualify as official sources by themselves.
- Uploads are bounded: 4 files, 4 MiB each, 8 MiB total; 20,000 extracted characters
  per document and 40,000 total. PDF has a 60-page limit. DOCX has bounded ZIP/XML
  expansion and external entities are prohibited. Files are never executed.
- Supported: PNG/JPEG/WebP (signature checked), PDF text, DOCX paragraphs, UTF-8
  TXT/Markdown/CSV/JSON. Scanned PDFs require sending the relevant page as an image;
  spreadsheet files, macros, slide decks and encrypted documents are unsupported.
- Image bytes are sent as multimodal provider content. File content/instructions
  are untrusted. Raw uploads are not persisted. Extracted text, checksums and file
  metadata are stored in the owner's run evidence snapshot; response is persisted
  with history/audit in the existing transaction. Image bytes are not in history.
- Exports are TXT/Markdown answers, not generated Office/PDF files or a persistent
  original-upload download library. No SQL migration is needed.

## Provider capability

The configured 9Router `cx/gpt-6-luna` accepted a real image and identified its
color. Its available model list contains no configured image-generation model.
Image generation remains unavailable and is explicitly labelled on the UI; no
fake generation button or invented download link is exposed.

## Validation and deployment

- Release build: warnings as errors, zero errors/warnings.
- 12 provider/attachment-reader unit tests, 16 academic SQL integration tests,
  and the OpenAPI snapshot check passed. SQL tests use owned isolated databases.
- 23 FE AI tests passed; typecheck/production build passed. Existing effect/bundle
  warnings remain; no horizontal overflow in the mobile browser check.
- Real browser login, multipart submission, model response and TXT download:
  run 31 PNG color; run 32 TXT code AURORA-731; run 33 PDF code AURORA-731;
  run 34 DOCX code ORBIT-642. All HTTP 200, ATTACHMENT, correct extracted facts.
  Downloaded text matched the actual response in all four cases.
- Real spoofed `.png` content returned HTTP 400. SQL tests cover cross-user
  download, disabled actor, project scope, and upload-only official claim rejection.
- Sanitized browser results/screenshots: parent workspace
  `outputs/assistant-files-20261024/`. Repeat with FE script
  `scripts/assistant-files-smoke.mjs` and test credentials in process environment.

Staging uses healthy image `aipms-api:assistant-files-20261024`; local FE at
localhost:5175 includes file selection, Ctrl+V image paste, previews, removal and
answer download buttons. Prior image `assistant-profile-20261024` is retained.
Rollback by changing the private compose image tag and recreating only API.
Keep existing query history; no database restore/reset is required.
