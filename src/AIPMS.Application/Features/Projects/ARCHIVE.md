# BE-16 project archive

SRS Report 3 sections 3.16-3.17 and issue #21. Archive is the final lifecycle
step after result publication has moved a project to `COMPLETED`.

## Contract

`POST /api/v1/projects/{id}/archive` accepts the current project concurrency
token and an optional reason up to 1000 characters. Only an authenticated
administrator or department staff member can archive. Department staff must
have an active academic scope matching at least one project major department.

The only allowed transition is `COMPLETED -> ARCHIVED`; an inactive, incomplete,
already archived or stale-token request returns `409`. The status history stores
the actor, UTC time and reason, and `PROJECT_ARCHIVED` is audited. No file,
evaluation or result rows are rewritten and no archive creates a new result.

Existing project reads and paginated search can filter `status=ARCHIVED` and
continue enforcing project permissions. Student team members, scoped academic
users and administrators can view archived projects through the existing read
contract. Existing write workflows already reject archived projects; archive is
terminal and there is no reopen/delete endpoint.

The archive command is intentionally separate from semester/project-period
archiving. Closing those academic containers remains governed by their existing
child-status rules. Retention, export and physical storage deletion are outside
this API slice.
