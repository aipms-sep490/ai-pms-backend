# CIB v4 Error Semantics & Token Lifecycle

> **Standards Compliance**: AI-PMS API endpoints return machine-readable errors adhering to **RFC 7807 (Problem Details for HTTP APIs)**. Error codes must not be arbitrarily invented; client integration relies on the standard HTTP status code, `type`, `title`, `detail`, and the `errors` validation dictionary.

---

## 1. HTTP Status Code Catalog

| HTTP Status | Name | Condition & Semantics in Current Backend |
|---|---|---|
| **400** | Bad Request | Request payload fails structural or validation rules (e.g. FluentValidation failures, invalid dates, negative numbers, missing required body fields). |
| **401** | Unauthorized | Bearer token is missing, expired, signed by an untrusted key, or malformed. |
| **403** | Forbidden | The authenticated actor lacks permission for the specific resource, project, department, or assignment scope. Stale JWT roles lacking DB backing also yield 403. |
| **404** | Not Found | The requested entity (project, task, assignment, file, etc.) does not exist in the database, or is hidden due to authorization scoping. |
| **409** | Conflict | Domain state conflict, optimistic concurrency token mismatch, duplicate assignment/draft creation attempt, or stale confirmation token. |
| **413** | Payload Too Large | Multipart upload exceeds server body limit (e.g. certificate upload > 22 MB limit). |
| **422** | Unprocessable Entity | Payload is syntactically valid JSON/multipart, but fails domain business invariants (e.g. duplicate unverified certificate submission). |
| **500** | Internal Server Error | Unhandled server exception. The server guarantees transactional rollback; no partial mutation or false success is ever committed. |

---

## 2. ProblemDetails Response Format

All 4xx and 5xx responses produce a standard RFC 7807 ProblemDetails body.

### 2.1 Standard Error Structure
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "Validation failed for the supplied request.",
  "instance": "/api/v1/tasks/project/42",
  "traceId": "00-4b82d9ef784a0d8c7c2b3e8e2194d2f0-0000000000000000-00",
  "errors": {
    "Title": ["The Title field is required."],
    "Priority": ["Priority must be one of: LOW, MEDIUM, HIGH, CRITICAL."]
  }
}
```

### 2.2 Concurrency Conflict Structure (409)
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.8",
  "title": "Concurrency Conflict",
  "status": 409,
  "detail": "The resource has been modified by another operation. Please reload the latest state and retry.",
  "instance": "/api/v1/evaluation-assignments/101/revoke",
  "traceId": "00-8c2e4f0a9b8d7c6e5a4b3c2d1e0f9a8b-0000000000000000-00"
}
```

---

## 3. Token Lifecycles & Mutation Semantics

### 3.1 Optimistic Concurrency Tokens (`concurrencyToken`)
- **Format**: Standard UUID/GUID string representation (e.g., `"e81a3b4c-9f01-4b2a-8c7d-3e5f1a2b4c6d"`).
- **Location**:
  - GET responses deliver the current token in the DTO field `concurrencyToken`.
  - Mutation endpoints accept it via query parameter (`?concurrencyToken=...`) or within the JSON command payload (`{ "concurrencyToken": "..." }`).
- **Enforcement**:
  - The repository compares the incoming token against the persisted row's token within a database transaction.
  - If a mismatch occurs, execution aborts with `409 Conflict`.
- **Client Handling**:
  - The client must not retry blindly.
  - The client must re-fetch the latest resource representation, present updated state to the user, and supply the newly fetched token in subsequent mutations.

### 3.2 Confirmation Tokens (`confirmationToken`)
- **Purpose**: Two-phase verification for irreversible or critical lifecycle publishing operations (e.g., publishing calculated Project Results or Student Results).
- **Lifecycle**:
  1. **Phase 1 (Preview)**:
     - Client calls `GET /api/v1/projects/{id}/result/preview`.
     - Server computes scores, stores an ephemeral hashed confirmation token, and returns `ProjectResultPreviewDto` with `confirmationToken`.
  2. **Phase 2 (Publish)**:
     - Client calls `POST /api/v1/projects/{id}/result` with `{ "confirmationToken": "..." }`.
     - Server validates that the token matches the calculated preview, has not expired, and has not been used previously.
     - On match, result is permanently published (`201 Created`).
     - On mismatch or stale token, server returns `409 Conflict`.
