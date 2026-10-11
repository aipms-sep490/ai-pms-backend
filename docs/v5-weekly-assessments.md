# v5 phase 7: weekly individual demo assessments

BE-N04. Weekly feedback/monitoring is separate from official grading. Every DTO
returns isDemo=true and affectsOfficialResult=false. These APIs never create an
EvaluationAssignment, evaluation, StudentResult or ProjectResult.

## FE contract

| Endpoint under /api/v1 | Purpose |
|---|---|
| GET projects/{id}/weekly-assessments/weeks | Semester weeks, actual start/end dates, timezone, hasStarted |
| GET projects/{id}/weekly-assessments/rubrics?page=&pageSize= | Current Primary selects published rubric in frozen lead department/semester |
| POST projects/{id}/weekly-assessments/draft | Create next version or update own draft |
| GET projects/{id}/weekly-assessments?studentId=&weekNumber=&page=&pageSize= | Scoped version history, stable ordering and server count |
| GET weekly-assessments/{id} | Authorized exact historical version |
| POST weekly-assessments/{id}/submit | Lock complete draft with concurrencyToken |

pageSize is 1..100; page 1..100000. History sorts by week descending, student ID,
then version descending before paging. Student requests filter to their own
SUBMITTED versions before count/paging. An explicit foreign student filter is 403.

Configuration: `WeeklyAssessment__TimeZoneId=Asia/Ho_Chi_Minh` (default).
Invalid timezone fails startup validation. Week 1 begins on the semester's actual
start date; each week has seven calendar days, with the last clipped to semester
end. This is not ISO week-of-year. API supplies dates so FE must not recompute them
from UTC or the browser timezone. Calendar supports a bounded semester of at most
731 inclusive days. Future weeks cannot be assessed.

```json
{
  "studentId": 12,
  "weekNumber": 4,
  "rubricId": 34,
  "expectedLatestVersion": 0,
  "scores": [
    { "rubricCriterionId": 56, "score": 8, "comments": "Synthetic example" }
  ],
  "comments": "Weekly monitoring only"
}
```

IDs are illustrative. Existing published rubric APIs configure the criteria;
using a rubric for weekly feedback does not give it academic approval. The server
captures rubric ID/version, criteria/tree/weights/maxima, date range, timezone,
student major, Primary assignment and author in the assessment version. Later
rubric/profile/configuration changes do not rewrite prior assessments.

Draft update sends current expectedLatestVersion and concurrencyToken. Omitted
criteria clear that draft's score; null/missing means pending, not zero. Only leaf
criteria from the frozen rubric can be scored; each must be within its maximum
with at most two decimal places. Every weighted criterion requires an explicit
score for submit. Demo total uses the existing weighted 0..10 calculator, rounded
once to two decimals. Zero is valid.

Submit body is `{ "concurrencyToken": "current-guid" }`. Submitted content is
immutable. Correction sends expectedLatestVersion, a nonempty revisionReason and
no old draft token, creating version + 1 with explicit scores and the same frozen
rubric/dates/targets. Do not silently replace a historical rubric during correction.

The replacement Primary can read history and create corrections. They cannot edit
or submit the previous Primary's draft. Creating a replacement draft requires a
reason, marks the prior draft SUPERSEDED and creates the new version atomically.
Previous draft content is retained; SUPERSEDED versions are visible only to the
current Primary and are never shown to students as submitted scores.

## Authority and errors

Current active PRIMARY assignment is the sole writer. Mentor, unrelated lecturer,
department staff, Admin alone and students cannot write. Writes also require active
account/frozen project scope, ACTIVE/FINAL_SUBMISSION project, open active semester,
and a verified active team student present in the frozen roster. Completed/archived
projects are read-only. Only current Primary can browse draft/history for the team;
students can read their own submitted history (including after leaving the team
while their account/frozen academic scope remains valid).

DTO canEdit/canSubmit use current scope/state and version author. A replaced Primary
loses access. Server rechecks all guards in the project transaction even after an
earlier capability read. History batches current student eligibility and uses fixed
set queries, with no query per displayed version.

401 unauthenticated; 403 actor/student scope; 404 missing resource; 400 invalid
week/paging/criterion payload; 409 stale token/version, future snapshotted week,
closed semester, immutable version, missing revision reason, unavailable/wrong
rubric or incomplete/out-of-range scores. Reload after 409.

## Database, tests and rollout

`20261011_add_weekly_assessments.sql` adds weekly_assessments (append-only version
history plus mutable current draft) and weekly_assessment_scores. Unique
project/student/week/version and filtered unique current draft protect races.
FKs preserve project/student/rubric/Primary lineage; rubric JSON contains historical
criterion definitions so historical display does not depend on mutable criterion
labels. Audit and mutation share a serializable project transaction. No official
grading table is modified.

Migration is additive/rerunnable with QUOTED_IDENTIFIER ON, synchronized with
bootstrap/manifest/EF/readiness. Deploy after isolated replay and CI, before the
compatible application. No fixtures/backfill are applied to shared AI_PMS.

WeeklyAssessmentEndpointTests exercise actual API draft/edit/submit/correction,
historical rubric readback, replacement Primary, scope/state denial, concurrent
draft/submit, stale token, audit rollback and migration rerun on isolated SQL.
WeeklyAssessmentWeeksTests verifies the Vietnam midnight boundary and final-week
clipping. Counts, CI and rollout evidence belong to the v5 tracker. Browser/FE
acceptance is Tin's separate work; keep the weekly UI behind its feature flag and
display the demo/monitoring label.
