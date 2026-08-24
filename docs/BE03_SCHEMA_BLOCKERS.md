# BE-03 additive database extension

The canonical `Database/schema.sql` supports progress reports, meetings, participants,
attendance, supervisor feedback, files, and notifications. BE-03 implements those
capabilities without changing the schema or generated EF models.

Migration `Database/migrations/003_be03_progress_meetings_extensions.sql` resolves the
original storage gaps without modifying canonical scripts or existing business rows:

- Member contributions and all five report sections have dedicated child tables.
- Report periods provide server-owned dates, deadlines, and `BLOCK`/`FLAG` late policy.
- Meeting decisions, blockers, and action items have dedicated child tables.
- Action items support project-member ownership and optional task/milestone links.

No JSON fallback was introduced. An administrator must configure an `OPEN`
`progress_report_periods` row before a team can create a report; the API deliberately
does not infer or accept deadline policy from clients.
