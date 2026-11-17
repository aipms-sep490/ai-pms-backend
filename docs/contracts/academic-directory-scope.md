# Academic directory read scope (SCOPE-01)

Applies to authenticated reads of `GET /api/v1/academic/semesters`, `GET /api/v1/academic/semesters/{id}`, `GET /api/v1/academic/project-periods`, `GET /api/v1/academic/project-periods/{id}`, `GET /api/v1/supervisors`, and `GET /api/v1/supervisors/{profileId}`.

| Persisted actor | Semester/Period reads | Supervisor directory |
| --- | --- | --- |
| Active Admin | Global catalog | Global directory |
| Active Department Staff in an active department/organization | Own organization | Own department only |
| Other active academic actor in an active department/organization | Own organization | Own organization; optional department filter remains within it |
| No active academic organization | 403 | 403 |

Client-provided `organizationId` or `departmentId` never widens the actor scope. Repository filters apply **before** count and pagination. A foreign detail ID is returned as 404; a Department Staff `departmentId` targeting another department returns 403. Profile update, supervisor candidate/assignment, registration and project-specific academic authority remain governed by their existing commands and policies. Admin's global catalog visibility does not grant academic approval authority.

The actor's active status, department, organization and Admin/Department Staff roles are read from persisted data. This contract is based on the user's 14 November 2026 policy decision; controlled fixture tests cover two organizations and multiple departments. Authenticated multi-role UAT against an approved environment is still required.
