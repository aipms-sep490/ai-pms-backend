# Assistant study tools

The existing ask, ask-with-files and stream endpoints accept optional `toolMode`
and `rubricId`. Existing requests remain valid. No SQL migration is required;
the existing run stores both the response and evidence snapshot.

| Mode | Requirements | Behavior |
| --- | --- | --- |
| PROGRESS | Authorized project; PROJECT/COMBINED scope; no upload | Deterministic summary of retrieved tasks/milestones, explicit coverage, overdue work; works without the text provider |
| REPORT_REVIEW | Authorized project; PROJECT/COMBINED; published rubric; upload | Advisory review, strengths, missing evidence and improvements; requires both file and rubric citations |
| WEEKLY_REPORT | User instructions; optional project/files | Editable weekly report with missing facts marked for completion |
| MEETING_MINUTES | User notes; optional project/files | Draft minutes; missing decisions/people/dates cannot be invented |
| WORK_PLAN | User objective; optional project/files | Proposed work plan, with suggestions distinguished from recorded facts |

`GET /api/v1/academic/assistant/review-rubrics?projectId=...` returns at most 100
published active rubrics in the caller's department and project semester, with
the exact version number. `assignedToProject` indicates a published scheme
binding; a published unbound rubric is explicitly a reference, not a project
grading scheme. The server rechecks rubric content/publication before saving a
review. Draft, retired and other-department rubrics are unavailable.

`GET /api/v1/academic/assistant/runs/{id}/download?format=...` supports `txt`,
`md`, `docx`, `xlsx`, `pdf`. Exports use the saved answer and source snapshots,
require the run owner and current project/conversation access, and are audited.
They contain a draft advisory note and source names/versions. Excel stores
text, never model-supplied formulas. Word has no external links/macros. PDF
embeds the repository's Vietnamese-capable font. Excel is a content worksheet,
not a generated financial/calculation workbook.

The tools do not submit reports or change tasks, scores, assignments, gates or
lifecycle. File-size, extraction and rate limits are unchanged. Report review
needs a working provider; a failed/invalid response yields fallback rather than
an unsupported official conclusion. Progress is based on bounded retrieval,
not a complete portfolio audit. Current file sources must be uploaded again
when reviewing in a later turn.

## Acceptance

1. Open the floating assistant in a project; select progress and submit.
2. Select report review, choose a named/versioned rubric, attach a TXT/DOCX/PDF
   report and submit. Verify rubric and file references and advisory wording.
3. From any permitted page select weekly report, minutes or work plan, fill in
   actual facts, submit, then open Download and select Word/Excel/PDF.
4. Reopen saved history and download the same response. Old rubric versions
   remain in exported source snapshots even after a newer version is published.
5. Verify unauthorized project/foreign rubric/another user's run is denied;
   missing file/rubric is rejected before calling the provider.
