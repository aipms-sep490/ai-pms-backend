# AI-PMS CIB v4 Isolated Acceptance Runbook

> **Strict Isolation Guarantee**: Acceptance tests must never execute against the shared development or staging databases (`AI_PMS`). Every acceptance test run creates a temporary, self-contained SQL Server LocalDB instance with synthetic data, verifies mutations through persistent readbacks, and drops the test catalog on teardown.

---

## 1. Prerequisites

1. **Operating System**: Windows with SQL Server LocalDB installed (`(localdb)\MSSQLLocalDB`).
2. **.NET SDK**: .NET 8.0 SDK installed (`dotnet --version` >= 8.0.x).
3. **Connection String Environment**:
   - By default, integration fixtures target `(localdb)\MSSQLLocalDB`.
   - To override the target SQL server for CI or specific environments, set:
     ```powershell
     $env:AIPMS_TEST_SQL_CONNECTION = "Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;"
     ```

---

## 2. Architecture of Isolated Test Fixtures

AI-PMS provides a layered fixture architecture in `tests/AIPMS.IntegrationTests`:

1. **`IsolatedSqlDatabase`**:
   - Dynamically creates a unique catalog name per test class (e.g. `aipms_test_[guid]`).
   - Executes `db/schema.sql`, `seed.sql`, and incremental migrations from `db/changes/`.
   - On completion (`DisposeAsync`), executes `ALTER DATABASE ... SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ...`.
2. **`SupervisorDatabaseFixture` & `TeamDatabaseFixture`**:
   - Extends the isolated database by seeding baseline academic structures (organizations, departments, semesters, project periods, and standard roles).
   - Generates deterministic actor credentials using synthetic GUIDs and hashed passcodes.
3. **`EvaluationDraftDatabaseFixture`**:
   - Provisions rubric criteria, evaluation windows, locked final submission packages, and supervisor assignments.

---

## 3. Two-Mode Fixture Setup

### 3.1 SINGLE_MAJOR Acceptance Fixture
- **Scope**: Single department (`LeadDepartmentId = Department_A`), single academic major (`Software Engineering`).
- **Team**: 1 Student Leader, 1 Student Member.
- **Project State**: Transitions from `FORMATION` -> `ACTIVE` -> `FINAL_SUBMISSION` -> `COMPLETED`.
- **Validation Focus**:
  - Verification of standard task queries.
  - Final package locking with optimistic tokens.
  - Common evaluator assignment (`Scope = 'COMMON'`).
  - Score aggregation and student owner privacy readback (`snapshotJson = "{}"`).

### 3.2 INTERDISCIPLINARY Acceptance Fixture
- **Scope**: Two departments (`LeadDepartment = Dept_A`, `ParticipatingDepartment = Dept_B`), distinct majors (`SE` + `IS` / `BA`).
- **Team**: Multi-major team with defined responsibility ratios and registration scope snapshot.
- **Validation Focus**:
  - Verification that participating staff cannot unilaterally alter lead decisions.
  - Assignment scoping across `MAJOR_SPECIFIC` and `COMMON` evaluators.
  - Authorization enforcement ensuring evaluators from different departments cannot access out-of-scope assignments (`403 Forbidden`).
  - Prevention of cross-major scoring leaks with frozen evaluation weights.
  - Persistent SQL readback on isolated LocalDB confirming multi-major assignment records.
  - Blocking unapproved features gracefully (`BLOCKED_BY_CONTRACT`).
- **Executed Acceptance Evidence**:
  - Primary Fixture Test: `AIPMS.IntegrationTests.Acceptance.CibV4AcceptanceFoundationTests.Interdisciplinary_fixture_enforces_multi_major_boundaries_and_persisted_readback` (seeds isolated 2-department, 2-major registration snapshot, creates `MAJOR_SPECIFIC` scheme/assignment, asserts foreign lecturer rejection with `403 Forbidden`, and verifies direct SQL persistence).
  - Multi-Evaluator Policy Test: `AIPMS.IntegrationTests.Evaluations.PolicyEvaluationEndpointTests.Two_departments_multiple_evaluators_and_individual_targets_use_frozen_weights_and_visibility` (asserts cross-department evaluator scoping and frozen weights).
  - Cross-Department Workflow Test: `AIPMS.IntegrationTests.Teams.InterdisciplinaryWorkflowTests.Cross_department_journey_requires_all_decisions_and_locks_roster` (asserts lead vs non-lead approval barriers).

---

## 4. Execution Workflow

### Step 1: Provision Isolated Test Database
```powershell
# Create a dedicated, disposable AI_PMS_E2E_<guid> database with full schema and seed
$dbName = 'AI_PMS_E2E_' + [Guid]::NewGuid().ToString('N')
$serverConn = "Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;"
& ".\scripts\new-e2e-database.ps1" -DatabaseName $dbName -ConnectionString $serverConn
```

### Step 2: Set Process Environment & Verify Schema Readiness
```powershell
# Point the test runner to the isolated database and verify all schema objects are ready
$env:AIPMS_TEST_SQL_CONNECTION = "Server=(localdb)\MSSQLLocalDB;Database=$dbName;Integrated Security=true;TrustServerCertificate=true;"
& ".\scripts\test-schema-readiness.ps1"
```

### Step 3: Build Solution Once
```powershell
# Build solution once before executing tests
dotnet build AIPMS.sln -c Release
```

### Step 4: Execute Targeted Acceptance Tests
```powershell
# Run acceptance foundation and contract validation without running full suite
dotnet test tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~CibV4AcceptanceFoundationTests|FullyQualifiedName~Two_departments_multiple_evaluators"
```

### Step 5: Teardown Isolated Database
```powershell
# Drop the disposable test database on completion
& ".\scripts\new-e2e-database.ps1" -DatabaseName $dbName -Drop -ConnectionString $serverConn
```

### Step 6: Verify Persistence & Readback
The acceptance tests automatically assert:
1. **Creation**: Entity is created via HTTP API.
2. **Mutation**: Status or payload is updated with `concurrencyToken`.
3. **Readback**: A fresh database context (`database.CreateContext()`) reads the underlying SQL row directly to prove that the mutation is committed and not merely held in cache.
4. **Teardown**: Database is dropped immediately upon test class disposal.

---

## 5. Security & Isolation Rules

- ❌ NEVER point `$env:AIPMS_TEST_SQL_CONNECTION` to production or shared staging.
- ❌ NEVER commit production connection strings, passwords, or JWT secrets.
- ❌ DO NOT use `Task.Delay` for synchronization; use deterministic database polling or interceptor events.
- ✅ Always use synthetic emails (e.g. `user_[guid]@example.test`).

---

## 6. OpenAPI Specification & Contract Validation

- The acceptance test `AIPMS.IntegrationTests.Acceptance.CibV4AcceptanceFoundationTests.OpenApi_specification_artifact_is_generated_and_covers_cib_v4_routes` is **strictly read-only** with a **zero-write path**. It validates in-memory runtime Swagger against the committed artifact (`docs/contracts/cib-v4/openapi.json`), asserts zero drift, and asserts that the committed file on disk was not modified.
- OpenAPI specification regeneration is strictly decoupled from test execution and must be executed manually via:
  ```powershell
  & ".\scripts\update-cib-v4-openapi.ps1"
  ```
