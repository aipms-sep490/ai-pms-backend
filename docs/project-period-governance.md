# Project Period Governance

Project periods expose a versioned policy for the project modes and proposal sources accepted during that period.

`allowedProjectModes` is a comma-separated set containing `SINGLE_MAJOR` and/or `INTERDISCIPLINARY`. `allowedProposalSources` contains `PUBLISHED_TOPIC` and/or `STUDENT_PROPOSAL`. Existing periods are backfilled with both values so legacy workflows keep working. Each change to either set increments `policyVersion`.

The policy is persisted with the period response and is intended to be captured by the eligibility and registration snapshots. Submission code must reject a mode or source that is not enabled by the current period policy; an already-submitted snapshot remains governed by its captured policy version.

Supervisor candidate and request APIs accept `assignmentType=PRIMARY` (the default) or `DISCIPLINE_MENTOR`. Mentor requests require `majorId`, the major must be required by the project, and accepted mentor requests create an assignment without re-running project activation. Candidate lookup for a mentor is scoped to the requested major's department.

The migration `db/changes/20260925_add_project_period_governance_policy.sql` is additive and safe to run repeatedly.
