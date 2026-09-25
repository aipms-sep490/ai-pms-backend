# Topic catalogue API

This slice implements the published topic catalogue from SRS UC-032, UC-042 and
UC-129. A lecturer or lead-department staff member can create a draft; a lead
department staff member publishes or closes it. Published content is immutable.
The API is additive and uses `db/changes/20260913_add_topic_catalog.sql`; apply
that script to the intended database before deploying this build.

## Routes

All routes require authentication and send `Cache-Control: no-store`.

- `GET /api/v1/topics`: published discovery by default. Filters include
  `academicSemesterId`, `projectPeriodId`, `departmentId`, `majorId`,
  `projectMode`, `search`, `compatibleOnly`, `mineOnly`, `status`, `page` and
  `pageSize`.
- `GET /api/v1/topics/{id}`: detail, subject to catalogue visibility.
- `POST /api/v1/topics`: create `DRAFT`.
- `PUT /api/v1/topics/{id}`: update a draft with `concurrencyToken`.
- `POST /api/v1/topics/{id}/publish`: publish with `concurrencyToken`.
- `POST /api/v1/topics/{id}/close`: close/withdraw with token and a required
  reason. A published topic keeps its publication audit fields.

## Content and scope

`TopicContentRequest` stores title, description, problem statement, objectives,
expected output, domain, technologies, keywords, `SINGLE_MAJOR` or
`INTERDISCIPLINARY` mode, and major quotas. Requirements are mandatory and each
major can occur once. Single-major topics require one requirement equal to
`primaryMajorId`; interdisciplinary topics have no primary major and require at
least two distinct majors. The lead department must own one listed major and all
majors must be active in the period's organization. Quotas are bounded to 1..100
per major and are checked against BE-12's real `MaxTeamSize` when published.

Drafts may omit publication-only proposal fields. Publishing rechecks active
period/semester/organization, the current BE-12 policy, all structured fields
and tag lists. A future registration period can hold a published catalogue
entry; an ended/closed period cannot. Period dates/status and academic references
are re-read in the same serializable transaction as the status update and audit.

## Visibility and authorization

- Effective roles are the intersection of persisted roles and JWT roles.
- Students, lecturers and staff see published topics in their organization.
- Staff see drafts/closed entries for their own lead department.
- A lecturer sees only drafts they authored in their own department.
- ADMIN can read the catalogue across organizations but cannot create, publish or
  close topics. Academic writing is restricted to active department scope.
- `compatibleOnly=true` means the student's active verified major is one of the
  topic requirements. It does not reserve a topic or guarantee that a later
  team/project selection will succeed.

The response includes persisted major/department references, publication/close
metadata, a concurrency token and `matchesMyMajor` for student readers. Backend
commands still recheck scope, policy and token; listing is advisory.

## Selection and period policy

Projects can select a published topic through the existing selection endpoint or
create a draft with optional `topicId` in `POST /api/v1/projects`. The latter is
atomic and supports periods that allow only `PUBLISHED_TOPIC` proposals.
Creation/update/publication and selection enforce the period's allowed mode/source
sets; submit/resubmit records the current policy in registration evidence.
See [project-period-governance.md](project-period-governance.md) for contracts and
rollout. This does not reserve a topic or introduce per-topic team capacity.

The implementation follows SRS BR-46 and the existing backend convention that
frontend visibility never replaces server authorization. Errors use the normal
ProblemDetails mapping: validation 400, unauthorized 401, forbidden 403, not
found 404, and state/concurrency/policy conflicts 409.
