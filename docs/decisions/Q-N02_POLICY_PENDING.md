# Q-N02: Academic Authority and Rubric Approval Governance Policy

**Status:** PENDING_ACADEMIC_COUNCIL_SIGNOFF  
**Technical Implementation:** COMPLETED (`BE-08` four-eyes approval engine implemented & integration tested)  
**Date:** 2026-11-04  
**Applies to:** Single-Major & Interdisciplinary Academic Evaluation Rubrics (`AIPMS.Application.Features.Evaluations`)

---

## 1. Context & Business Intent

In academic capstone project governance, no single individual should author and unilaterally publish an evaluation rubric that directly influences official graduation scores. Prior to `BE-08`, rubrics followed a single-step `DRAFT -> PUBLISHED` transition by any staff member.

`BE-08` introduces a formal, auditable two-person ("four-eyes") governance workflow:
`DRAFT -> PENDING_APPROVAL -> PUBLISHED` (on approval) or `DRAFT` (on rejection with required reason).

---

## 2. Technical Implementation Architecture

1. **State Machine:**
   - Expanded `ck_rubric_versions_status` to include `PENDING_APPROVAL`.
   - Append-only audit table `dbo.rubric_approvals` enforcing `decided_by <> submitted_by`.
   - Idempotent approval transition: `approve` transitions status to `PUBLISHED` and records decider metadata in a single transaction.

2. **Authority & Scope Rules:**
   - **Lead Department Rule:** For `INTERDISCIPLINARY` mode with `COMMON` scope, only academic staff from the **Lead Department** (`leadDepartmentId`) can approve or reject. Participating departments receive `403 RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT`.
   - **Major Scope Rule:** For `MAJOR_SPECIFIC` rubrics, only academic staff within the department that owns the `majorId` can approve.
   - **Four-Eyes Enforcement:** Submitter attempting self-approval is rejected with `403 RUBRIC_SELF_APPROVAL_FORBIDDEN`.
   - **Admin Exclusion:** Pure administrative accounts without academic department affiliation receive `403 RUBRIC_APPROVAL_ADMIN_NOT_ACADEMIC`.

3. **Fail-Closed Official Grading Invariant (`BR-N7`):**
   - In demo mode (`IsDemo = true`), approval without `approvalReference` is permitted for testing.
   - In official mode (`IsDemo = false`), official publication requires both `ApprovalStatus = "APPROVED"` and a non-empty `ApprovalReference`.

---

## 3. Pending Academic Council Decisions

The following items are technical defaults pending official Academic Council sign-off:
- **Authority Delegation:** Whether Associate Department Heads or designated Capstone Coordinators hold approval authority equivalent to Department Heads.
- **Cross-Department Conciliation:** Dispute resolution protocol if participating departments object to a Common Scope rubric published by the Lead Department.
- **Reference Format Standard:** Mandatory naming convention for `approvalReference` (e.g. `QD-[Year]/[Month]/DH-[Dept]`).
