# Infrastructure services

Group application-service implementations by capability:

- `Auditing`: database and logging audit adapters.
- `Projects`: project access and execution guards.
- `Teams`: team formation policy adapters.

Repository implementations belong in `Persistence/Repositories`, including the
authentication repository. Token issuance, password hashing and account-token
validation remain in `Identity`. Feature workflows stay in Application.
