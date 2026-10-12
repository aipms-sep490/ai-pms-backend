# Progress report submission notification follow-up

The final v5 review found an existing source-resolution bug: a
ProgressReportSubmitted event fell through to the supervisor-request branch.
A report ID therefore looked up an unrelated table, usually creating no inbox or
email-queue entry despite a successful report submission.

The writer now locks/reads the progress report itself and resolves its project and
team from that source. It only handles SUBMITTED reports. Recipients are active
current PRIMARY lecturers with active department/organization in the project
organization. A DISCIPLINE_MENTOR is not given unclassified whole-project report
notifications. No report contents or project details enter the generic message.

Submission, audit, inbox and existing delivery queue remain in the same source
transaction. Repeated submit returns 409; the existing notification identity guard
also prevents duplicate occurrences. SMTP workers run after commit. Queue creation
is not a claim that email was delivered. This change does not send test email or
mutate shared business data and requires no database migration or OpenAPI change.

Validation: Release warnings-as-errors; 166 report/concurrency/notification
integration tests passed, followed by 11 report tests including new ended,
major-only Mentor and inactive-account negatives. The positive lifecycle test
asserts both the exact PRIMARY inbox recipient and durable email queue record.
Final full-suite CI is required before merge.
