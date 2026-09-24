# Dashboard analytics

BE-15 exposes role-scoped dashboards for students, supervisors, department staff
and administrators. All portfolio facts are read in batches and progress/risk
values use the deterministic BE-06 analysis service.

## Endpoints

- `GET /api/v1/dashboards/student` returns the current student's workflow context,
  next actions, own team/project deadlines, unread notifications and own
  contribution summary.
- `GET /api/v1/dashboards/supervisor` returns the supervisor's active workload,
  project status counts, pending progress reviews, overdue tasks and a paged
  project list. It accepts `semesterId`, `status`, `search`, `page` and
  `pageSize` filters.
- `GET /api/v1/dashboards/department` returns the portfolio visible to the
  authenticated department staff member. It accepts `semesterId`, `majorId`,
  `status`, `search`, `page` and `pageSize`.
- `GET /api/v1/dashboards/admin` returns the full portfolio for an administrator.
  It accepts the department filter in addition to the common portfolio filters.
- `GET /api/v1/dashboards/portfolio/export?format=csv` exports the same filtered
  portfolio for department staff or administrators. The export is UTF-8 with a
  BOM, uses RFC 4180 escaping, is limited to 10,000 rows and records a
  `DASHBOARD_EXPORTED` audit event.

All endpoints resolve the persisted active role and academic scope from the
database. Supervisor project data is restricted to active supervisor assignments;
student project data is restricted to active team membership; department staff
data is restricted to projects in their persisted active department; admins may
filter the whole portfolio by department. Project lists use stable
`CreatedAt DESC, Id DESC` ordering.

Portfolio responses include status, major, risk and supervisor workload
aggregations plus per-project milestone/task/report progress. A filtered
portfolio over 10,000 projects is rejected with HTTP 422 and must be narrowed.
Student and supervisor contracts remain compatible with the original endpoints.
