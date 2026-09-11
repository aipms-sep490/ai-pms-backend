# BE-08 Deliverables & Files

Issue #13. Implements deliverable definitions, immutable file versions, assigned
supervisor review and shared project attachments using the existing database.
Controllers dispatch MediatR requests; validators check input; DeliverableWorkflow
enforces current permissions and transaction boundaries. EF entities and storage
paths never appear in API responses.

## API contract

All routes require an authenticated active account. IDs are positive integers.
Lists return PagedResult with page (default 1), pageSize (default 20, maximum 100),
totalCount and items. Pages start at 1 and are bounded at 1,000,000.

| Method | Route | Purpose |
| --- | --- | --- |
| GET / POST | `/api/v1/projects/{projectId}/deliverables` | List / create definition |
| GET / PUT / DELETE | `/api/v1/deliverables/{id}` | Read / replace definition / delete unused definition |
| GET / POST | `/api/v1/deliverables/{id}/versions` | Version history / submit multipart file |
| GET | `/api/v1/deliverable-versions/{id}` | Version and file metadata |
| POST | `/api/v1/deliverable-versions/{id}/review` | Accept or reject latest pending version |
| GET | `/api/v1/deliverable-versions/{id}/feedback` | Paginated supervisor feedback |
| GET | `/api/v1/projects/{projectId}/files` | Search files within project scope |
| POST | `/api/v1/files` | Upload multipart report / meeting attachment |
| GET | `/api/v1/files/{id}` | Authorized metadata |
| GET | `/api/v1/files/{id}/download` | Authorized attachment stream |
| DELETE | `/api/v1/files/{id}` | Remove an editable attachment |

Create/update JSON: `{ "milestoneId": null, "title": "Report", "description": null,
"deliverableType": "REPORT", "dueAt": "2026-09-30T10:00:00Z" }`.
Optional values can be null. PUT replaces these fields; milestone must belong to
the project. Title is required (255 characters), description up to 10,000,
deliverableType up to 50. All supplied date filters/deadlines must use UTC.

Submit version as multipart/form-data with `file`, required
`expectedLatestVersion` (0 for the first version) and optional `note` (2,000
characters). First load the definition's latestVersion; submit that value. On
409, reload history before deciding to retry. Version numbers advance by one
under the project lock. A repeated/stale submission cannot create another version.
One file is created per submitted version; old bytes, uploader, checksum, note and
submission time cannot be edited or deleted. A new version may supersede a pending
or rejected version. Review changes status and creates feedback, never file content.

Review JSON: `{ "decision": "ACCEPTED", "feedback": "Ready" }`.
Only ACCEPTED / REJECTED are valid; feedback is required (10,000 characters).
Only the latest SUBMITTED version can be reviewed, once. New definitions are OPEN;
upload sets SUBMITTED; review sets ACCEPTED / REJECTED. ACCEPTED and CLOSED are
locked. Deletion is limited to DRAFT / OPEN definitions without any versions.
There is no unlock or final revision endpoint in this package.

Attachment multipart fields: `file`, `parentType` (REPORT / MEETING), `parentId`.
Version files must be submitted through the version endpoint. Every new file has
exactly one parent; standalone/project-only files and arbitrary parent paths are
not accepted. Existing feedback files can be read through project authorization
but cannot be created or deleted by this API.

Deliverable filters: `search` (title), `status`, `milestoneId`, `deliverableType`.
File filters: `search` (name), `contentType` (MIME), `uploadedBy`, `from` (inclusive),
`to` (exclusive), `parentType` (VERSION / REPORT / MEETING / FEEDBACK), `parentId`.
Parent ID requires parent type. Filters always apply after mandatory project
scoping; another project's parent cannot expose files. History sorts by descending
version number; other lists sort by descending ID.

## Authorization and state

Permissions use persisted roles, active account/academic scope and current
membership/assignment, not role claims alone.

| Operation | Permission |
| --- | --- |
| Read metadata / history / download | Current project team or assigned supervisor; department staff in project scope; admin |
| Create/update/delete definition | Current student leader or assigned lecturer |
| Submit version | Current student team member (including leader) |
| Review | Current assigned lecturer |
| Add report attachment | Current student team member, report DRAFT |
| Add meeting attachment | Current student team member or assigned lecturer, meeting SCHEDULED |
| Delete attachment | Same parent write permission, and uploader / student leader / assigned lecturer |

All mutations require ACTIVE project status. Submission also requires one active
EXECUTION period containing the current time in an active current semester with an
active organization. The earlier of deliverable dueAt and execution end is the
effective deadline; reaching that instant rejects submission. These checks run
under a serializable transaction and are repeated after storage writes to catch
uploads that cross the deadline. Archived/final projects remain read-only.

Validation failures return 400, missing authentication 401, forbidden scope/role
403, missing records/content 404, and state/concurrency conflicts 409, using the
shared ProblemDetails middleware. Unexpected storage/database failures return a
non-sensitive 500. Downloads use attachment disposition, no-store and nosniff.

## Persistence and coordination

Project update locks serialize submit, review and deletion; related SQL rows are
protected by serializable isolation. SQL deadlock/lock timeout, conflicting unique
keys, foreign key races and optimistic concurrency failures map to 409. Metadata,
review status, audit and reviewer notifications commit together. Responses are
materialized before commit; there is no post-commit database read in the workflow.

Storage creation is create-only. After successful creation, a confirmed rollback
before any commit attempt cleans up that request's file. Failed storage creation
owns its partial cleanup. A commit exception can have an uncertain outcome, so
the object is retained even if the rollback call subsequently succeeds. Retry a
version submission using its original expectedLatestVersion to avoid duplicates.
Deletes commit metadata/audit first, then remove private content on a best-effort
basis. Cleanup errors are logged for reconciliation; see Infrastructure/Storage.

`Deliverables:NotifyReviewer` defaults to true. A successful submission stores an
in-app notification and recipients for current active lecturer assignments in the
same transaction. Set false to suppress this notification. Delivery channels and
notification inbox UI are outside BE-08.

Issue #7 remains DongVV's Progress Reports & Meetings scope. These file endpoints
attach only to existing editable parents; BE-08 does not create/submit reports,
schedule meetings or manage their participants. Parent lifecycle implementations
must transact their state checks/changes and preserve file references on submitted
or completed records. Use the same project lock before child mutation to keep lock
ordering consistent; serializable parent reads also block conflicting SQL updates.
Task evidence should reference authorized file/version IDs through its own domain
contract; this endpoint does not invent a TASK parent absent from the schema.

Assigned evaluator access, evaluation feedback, final-package snapshots and a
controlled final revision policy depend on their later modules. Until those exist,
review requires an actual supervisor assignment and accepted/closed records stay
locked. No schema migration, generated model edit or shared SQL execution is needed.

## Validation evidence

Unit tests cover validators, submission handler permissions/transaction boundaries,
content/size checks, office archives, path safety and local storage failure cleanup.
SQL integration tests use an isolated database and fake storage for the endpoint
lifecycle, current authorization, deadlines, parent scope, filters, concurrent
submit/review, transactional audit/notification failures, response-read rollback,
lost commit acknowledgement and post-commit physical cleanup failure.
