# Rule-based progress analysis

`GET /api/v1/projects/{projectId}/progress-analysis` reads authorized project facts
and applies the provisional deterministic rules. It does not call an external AI
provider or change project state, ownership, supervision or grades.

## Deadline evidence

Task deadline coverage is the fraction of active tasks with DueAt. Milestone
deadline coverage is the fraction of outstanding, non-cancelled milestones with
DueDate. Completed milestones still contribute to completion rate, but their
dates cannot supply evidence for outstanding work. If all non-cancelled work is
complete, current delay is zero without requiring historical deadlines. If all
milestones are cancelled, milestone features remain unavailable.

With no dates for outstanding work, its deadline features are null. With some
dates, deadline features describe only the dated subset and Limitations indicates
partial evidence; observed risk factors remain available. SUFFICIENT requires
complete deadline coverage for outstanding tasks and milestones, non-cancelled
milestone evidence and at least six effective feature slots. Partial coverage
therefore cannot produce an overall LOW assessment. Strong observed blocked-task
signals can still produce HIGH/CRITICAL alongside INSUFFICIENT_DATA.

Confidence is an evidence-coverage indicator, not a calibrated probability.
Each available non-deadline feature contributes one slot; each of the two task
deadline slots contributes task coverage, and each of the two milestone deadline
slots contributes milestone coverage. The total is divided by 11 and rounded to
two decimal places. Missing deadlines reduce confidence rather than counting as
fully observed, healthy values.

Reporting-policy evidence is not yet available: PeriodEnd is never a submission
deadline, and missing/late report features remain null. Contribution variance is
also unavailable pending BE-13. These limitations are included in each analysis.

## Deferred persistence

The returned feature snapshot is not persisted history. Facts/source identities,
timestamps, evaluated features, policy/configuration, rule/model versions and
outputs still need a separately reviewed persistence design and migration.
BE-06 remains incomplete until that work is accepted.
