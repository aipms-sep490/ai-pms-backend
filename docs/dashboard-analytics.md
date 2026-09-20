# Dashboard analytics

The first BE-15 slice exposes role-scoped dashboards for students and supervisors.

## Endpoints

- `GET /api/v1/dashboards/student` returns the current student's workflow context,
  next actions, own team/project deadlines, unread notifications and own
  contribution summary.
- `GET /api/v1/dashboards/supervisor` returns the supervisor's active workload,
  project status counts, pending progress reviews, overdue tasks and a paged
  project list. It accepts `semesterId`, `status`, `search`, `page` and
  `pageSize` filters.

Both endpoints resolve the persisted active role and academic scope from the
database. Supervisor project data is restricted to active supervisor assignments;
student project data is restricted to active team membership. Project progress
and risk indicators reuse the deterministic BE-06 analysis service.

Department/admin aggregation and PDF/Excel export remain separate BE-15 slices.
