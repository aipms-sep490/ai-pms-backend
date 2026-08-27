# Academic

BE-11 implements the Organization, Department and Major hierarchy. Semester and
Project Period remain in BE-12.

## Authorization

- Every authenticated user can read the active academic hierarchy.
- `ADMIN` manages Organizations and creates Departments.
- `ADMIN` and `DEPARTMENT_STAFF` can manage Departments and Majors.
- Department Staff scope is resolved from the current database user and is
  enforced again in Application handlers.

## Lifecycle

DELETE endpoints perform soft deletion by setting `is_active = 0`. Deactivating
an Organization also deactivates its Departments and Majors. Deactivating a
Department also deactivates its Majors. Reactivating a parent does not
automatically reactivate its children.

Structural changes are emitted through `IAuditTrail`. The current Infrastructure
implementation writes structured audit events to the configured logs; BE-10 can
replace this implementation with persistent audit storage without changing the
Academic handlers.
