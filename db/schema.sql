/*
 AI-PMS Initial Database Schema
 Target: Microsoft SQL Server
 Naming convention: snake_case for tables/columns/constraints/indexes
 Primary keys: BIGINT IDENTITY unless a different key is clearly more appropriate.
*/


USE [AI_PMS];
GO
IF DB_ID(N'AI_PMS') IS NULL
BEGIN
    CREATE DATABASE [AI_PMS];
END;
GO

USE [AI_PMS];
GO

SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

/* =========================================================
   ACADEMIC STRUCTURE (root tables first)
   ========================================================= */

CREATE TABLE dbo.organizations (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    code            NVARCHAR(50) NOT NULL,
    name            NVARCHAR(255) NOT NULL,
    description     NVARCHAR(1000) NULL,
    is_active       BIT NOT NULL CONSTRAINT df_organizations_is_active DEFAULT (1),
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_organizations_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_organizations_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_organizations PRIMARY KEY (id),
    CONSTRAINT uq_organizations_code UNIQUE (code)
);
GO

CREATE TABLE dbo.departments (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    organization_id BIGINT NOT NULL,
    code            NVARCHAR(50) NOT NULL,
    name            NVARCHAR(255) NOT NULL,
    description     NVARCHAR(1000) NULL,
    is_active       BIT NOT NULL CONSTRAINT df_departments_is_active DEFAULT (1),
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_departments_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_departments_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_departments PRIMARY KEY (id),
    CONSTRAINT uq_departments_org_code UNIQUE (organization_id, code),
    CONSTRAINT fk_departments_organization FOREIGN KEY (organization_id)
        REFERENCES dbo.organizations(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.majors (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    department_id   BIGINT NOT NULL,
    code            NVARCHAR(50) NOT NULL,
    name            NVARCHAR(255) NOT NULL,
    description     NVARCHAR(1000) NULL,
    is_active       BIT NOT NULL CONSTRAINT df_majors_is_active DEFAULT (1),
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_majors_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_majors_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_majors PRIMARY KEY (id),
    CONSTRAINT uq_majors_department_code UNIQUE (department_id, code),
    CONSTRAINT fk_majors_department FOREIGN KEY (department_id)
        REFERENCES dbo.departments(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.academic_semesters (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    organization_id BIGINT NOT NULL,
    code            NVARCHAR(50) NOT NULL,
    name            NVARCHAR(255) NOT NULL,
    start_date      DATE NOT NULL,
    end_date        DATE NOT NULL,
    status          NVARCHAR(20) NOT NULL CONSTRAINT df_academic_semesters_status DEFAULT (N'DRAFT'),
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_academic_semesters_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_academic_semesters_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_academic_semesters PRIMARY KEY (id),
    CONSTRAINT uq_academic_semesters_org_code UNIQUE (organization_id, code),
    CONSTRAINT ck_academic_semesters_dates CHECK (end_date >= start_date),
    CONSTRAINT ck_academic_semesters_status CHECK (status IN (N'DRAFT', N'UPCOMING', N'ACTIVE', N'CLOSED', N'ARCHIVED')),
    CONSTRAINT fk_academic_semesters_organization FOREIGN KEY (organization_id)
        REFERENCES dbo.organizations(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.project_periods (
    id                          BIGINT IDENTITY(1,1) NOT NULL,
    academic_semester_id         BIGINT NOT NULL,
    code                        NVARCHAR(50) NOT NULL,
    name                        NVARCHAR(255) NOT NULL,
    period_type                 NVARCHAR(50) NOT NULL,
    start_at                    DATETIME2(0) NOT NULL,
    end_at                      DATETIME2(0) NOT NULL,
    status                      NVARCHAR(20) NOT NULL CONSTRAINT df_project_periods_status DEFAULT (N'DRAFT'),
    min_team_size               INT NULL CONSTRAINT df_project_periods_min_team_size DEFAULT (3),
    max_team_size               INT NULL CONSTRAINT df_project_periods_max_team_size DEFAULT (5),
    min_distinct_majors         INT NULL CONSTRAINT df_project_periods_min_distinct_majors DEFAULT (1),
    max_projects_per_supervisor INT NULL CONSTRAINT df_project_periods_max_projects_per_supervisor DEFAULT (5),
    allowed_project_modes       VARCHAR(200) NOT NULL CONSTRAINT df_project_periods_allowed_modes DEFAULT ('SINGLE_MAJOR,INTERDISCIPLINARY'),
    allowed_proposal_sources    VARCHAR(200) NOT NULL CONSTRAINT df_project_periods_allowed_sources DEFAULT ('PUBLISHED_TOPIC,STUDENT_PROPOSAL'),
    policy_version               INT NOT NULL CONSTRAINT df_project_periods_policy_version DEFAULT (1),
    milestone_template_id          BIGINT NULL,
    milestone_template_version_id  BIGINT NULL,
    rubric_id                   BIGINT NULL,
    created_at                  DATETIME2(0) NOT NULL CONSTRAINT df_project_periods_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at                  DATETIME2(0) NOT NULL CONSTRAINT df_project_periods_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_project_periods PRIMARY KEY (id),
    CONSTRAINT uq_project_periods_semester_code UNIQUE (academic_semester_id, code),
    CONSTRAINT ck_project_periods_dates CHECK (end_at >= start_at),
    CONSTRAINT ck_project_periods_type CHECK (period_type IN (
        N'REGISTRATION', N'PROJECT_REVIEW', N'SUPERVISOR_SELECTION',
        N'EXECUTION', N'FINAL_SUBMISSION', N'EVALUATION'
    )),
    CONSTRAINT ck_project_periods_status CHECK (status IN (N'DRAFT', N'UPCOMING', N'ACTIVE', N'CLOSED', N'ARCHIVED')),
    CONSTRAINT ck_project_periods_team_size CHECK (
        (min_team_size IS NULL AND max_team_size IS NULL) OR
        (min_team_size >= 1 AND max_team_size >= min_team_size)
    ),
    CONSTRAINT ck_project_periods_majors CHECK (
        min_distinct_majors IS NULL OR
        (min_distinct_majors >= 1 AND (max_team_size IS NULL OR min_distinct_majors <= max_team_size))
    ),
    CONSTRAINT ck_project_periods_supervisor CHECK (
        max_projects_per_supervisor IS NULL OR max_projects_per_supervisor >= 1
    ),
    CONSTRAINT ck_project_periods_policy_version CHECK (policy_version >= 1),
    CONSTRAINT fk_project_periods_semester FOREIGN KEY (academic_semester_id)
        REFERENCES dbo.academic_semesters(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

/* =========================================================
   IDENTITY / RBAC
   ========================================================= */

CREATE TABLE dbo.roles (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    code            NVARCHAR(50) NOT NULL,
    name            NVARCHAR(100) NOT NULL,
    description     NVARCHAR(500) NULL,
    is_system_role  BIT NOT NULL CONSTRAINT df_roles_is_system_role DEFAULT (1),
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_roles_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_roles_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_roles PRIMARY KEY (id),
    CONSTRAINT uq_roles_code UNIQUE (code)
);
GO

CREATE TABLE dbo.permissions (
    id                      BIGINT IDENTITY(1,1) NOT NULL,
    code                    NVARCHAR(100) NOT NULL,
    name                    NVARCHAR(150) NOT NULL,
    description             NVARCHAR(500) NULL,
    is_system_permission    BIT NOT NULL CONSTRAINT df_permissions_is_system_permission DEFAULT (0),
    created_at              DATETIME2(0) NOT NULL CONSTRAINT df_permissions_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at              DATETIME2(0) NOT NULL CONSTRAINT df_permissions_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_permissions PRIMARY KEY (id),
    CONSTRAINT uq_permissions_code UNIQUE (code)
);
GO

CREATE TABLE dbo.users (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    department_id   BIGINT NULL,  -- lecturer / department staff affiliation
    major_id        BIGINT NULL,  -- student major; department is derived through majors
    email           NVARCHAR(320) NOT NULL,
    password_hash   NVARCHAR(500) NOT NULL,
    full_name       NVARCHAR(255) NOT NULL,
    phone           NVARCHAR(30) NULL,
    student_code    NVARCHAR(50) NULL,
    curriculum_code NVARCHAR(100) NULL,
    employee_code   NVARCHAR(50) NULL,
    title           NVARCHAR(100) NULL,
    status          NVARCHAR(20) NOT NULL CONSTRAINT df_users_status DEFAULT (N'ACTIVE'),
    academic_profile_status NVARCHAR(20) NOT NULL CONSTRAINT df_users_academic_profile_status DEFAULT (N'PENDING'),
    academic_profile_reviewed_by BIGINT NULL,
    academic_profile_reviewed_at DATETIME2(0) NULL,
    academic_profile_rejection_reason NVARCHAR(2000) NULL,
    access_failed_count INT NOT NULL CONSTRAINT df_users_access_failed_count DEFAULT (0),
    lockout_end_at  DATETIME2(0) NULL,
    password_changed_at DATETIME2(0) NULL,
    last_login_at   DATETIME2(0) NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_users_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_users_updated_at DEFAULT (SYSUTCDATETIME()),
    row_version     ROWVERSION NOT NULL,
    CONSTRAINT pk_users PRIMARY KEY (id),
    CONSTRAINT uq_users_email UNIQUE (email),
    CONSTRAINT ck_users_status CHECK (status IN (N'ACTIVE', N'INACTIVE', N'SUSPENDED')),
    CONSTRAINT ck_users_academic_profile_status CHECK (academic_profile_status IN (N'PENDING', N'VERIFIED', N'REJECTED')),
    CONSTRAINT ck_users_access_failed_count CHECK (access_failed_count >= 0),
    CONSTRAINT fk_users_department FOREIGN KEY (department_id)
        REFERENCES dbo.departments(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_users_major FOREIGN KEY (major_id)
        REFERENCES dbo.majors(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_users_academic_profile_reviewer FOREIGN KEY (academic_profile_reviewed_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE UNIQUE INDEX ux_users_student_code_not_null
ON dbo.users(student_code)
WHERE student_code IS NOT NULL;
GO

CREATE UNIQUE INDEX ux_users_employee_code_not_null
ON dbo.users(employee_code)
WHERE employee_code IS NOT NULL;
GO

CREATE TABLE dbo.user_roles (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    user_id         BIGINT NOT NULL,
    role_id         BIGINT NOT NULL,
    assigned_by     BIGINT NULL,
    assigned_at     DATETIME2(0) NOT NULL CONSTRAINT df_user_roles_assigned_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_user_roles PRIMARY KEY (id),
    CONSTRAINT uq_user_roles_user_role UNIQUE (user_id, role_id),
    CONSTRAINT fk_user_roles_user FOREIGN KEY (user_id)
        REFERENCES dbo.users(id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_user_roles_role FOREIGN KEY (role_id)
        REFERENCES dbo.roles(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_user_roles_assigned_by FOREIGN KEY (assigned_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.role_permissions (
    role_id         BIGINT NOT NULL,
    permission_id   BIGINT NOT NULL,
    assigned_by     BIGINT NULL,
    assigned_at     DATETIME2(0) NOT NULL CONSTRAINT df_role_permissions_assigned_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_role_permissions PRIMARY KEY (role_id, permission_id),
    CONSTRAINT fk_role_permissions_role FOREIGN KEY (role_id)
        REFERENCES dbo.roles(id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_role_permissions_permission FOREIGN KEY (permission_id)
        REFERENCES dbo.permissions(id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_role_permissions_assigned_by FOREIGN KEY (assigned_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.refresh_tokens (
    id                  BIGINT IDENTITY(1,1) NOT NULL,
    user_id             BIGINT NOT NULL,
    token_hash          VARBINARY(64) NOT NULL,
    family_id           UNIQUEIDENTIFIER NOT NULL,
    expires_at          DATETIME2(0) NOT NULL,
    created_at          DATETIME2(0) NOT NULL CONSTRAINT df_refresh_tokens_created_at DEFAULT (SYSUTCDATETIME()),
    created_by_ip       NVARCHAR(45) NULL,
    user_agent          NVARCHAR(500) NULL,
    revoked_at          DATETIME2(0) NULL,
    revoked_by_ip       NVARCHAR(45) NULL,
    replaced_by_token_id BIGINT NULL,
    reuse_detected_at   DATETIME2(0) NULL,
    CONSTRAINT pk_refresh_tokens PRIMARY KEY (id),
    CONSTRAINT uq_refresh_tokens_token_hash UNIQUE (token_hash),
    CONSTRAINT ck_refresh_tokens_expires_at CHECK (expires_at > created_at),
    CONSTRAINT ck_refresh_tokens_revoked_at CHECK (revoked_at IS NULL OR revoked_at >= created_at),
    CONSTRAINT ck_refresh_tokens_reuse_detected_at CHECK (reuse_detected_at IS NULL OR reuse_detected_at >= created_at),
    CONSTRAINT fk_refresh_tokens_user FOREIGN KEY (user_id)
        REFERENCES dbo.users(id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_refresh_tokens_replaced_by FOREIGN KEY (replaced_by_token_id)
        REFERENCES dbo.refresh_tokens(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.password_reset_tokens (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    user_id         BIGINT NOT NULL,
    token_hash      VARBINARY(64) NOT NULL,
    expires_at      DATETIME2(0) NOT NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_password_reset_tokens_created_at DEFAULT (SYSUTCDATETIME()),
    requested_by_ip NVARCHAR(45) NULL,
    used_at         DATETIME2(0) NULL,
    CONSTRAINT pk_password_reset_tokens PRIMARY KEY (id),
    CONSTRAINT uq_password_reset_tokens_token_hash UNIQUE (token_hash),
    CONSTRAINT ck_password_reset_tokens_expires_at CHECK (expires_at > created_at),
    CONSTRAINT ck_password_reset_tokens_used_at CHECK (used_at IS NULL OR used_at >= created_at),
    CONSTRAINT fk_password_reset_tokens_user FOREIGN KEY (user_id)
        REFERENCES dbo.users(id) ON DELETE CASCADE ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.audit_logs (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    actor_user_id   BIGINT NULL,
    action          NVARCHAR(100) NOT NULL,
    entity_type     NVARCHAR(100) NOT NULL,
    entity_id       NVARCHAR(100) NULL,
    outcome         NVARCHAR(20) NOT NULL CONSTRAINT df_audit_logs_outcome DEFAULT (N'SUCCESS'),
    correlation_id  UNIQUEIDENTIFIER NULL,
    ip_address      NVARCHAR(45) NULL,
    user_agent      NVARCHAR(500) NULL,
    details_json    NVARCHAR(MAX) NULL,
    occurred_at     DATETIME2(0) NOT NULL CONSTRAINT df_audit_logs_occurred_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_audit_logs PRIMARY KEY (id),
    CONSTRAINT ck_audit_logs_outcome CHECK (outcome IN (N'SUCCESS', N'FAILURE', N'DENIED')),
    CONSTRAINT ck_audit_logs_details_json CHECK (details_json IS NULL OR ISJSON(details_json) = 1),
    CONSTRAINT fk_audit_logs_actor FOREIGN KEY (actor_user_id)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

/* =========================================================
   TEAM
   ========================================================= */

CREATE TABLE dbo.teams (
    id                  BIGINT IDENTITY(1,1) NOT NULL,
    academic_semester_id BIGINT NOT NULL,
    code                NVARCHAR(50) NOT NULL,
    name                NVARCHAR(255) NOT NULL,
    description         NVARCHAR(1000) NULL,
    status              NVARCHAR(20) NOT NULL CONSTRAINT df_teams_status DEFAULT (N'FORMING'),
    created_by          BIGINT NOT NULL,
    created_at          DATETIME2(0) NOT NULL CONSTRAINT df_teams_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at          DATETIME2(0) NOT NULL CONSTRAINT df_teams_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_teams PRIMARY KEY (id),
    CONSTRAINT uq_teams_semester_code UNIQUE (academic_semester_id, code),
    -- Supports a composite FK from team_members so the member semester must match the team semester.
    CONSTRAINT uq_teams_id_semester UNIQUE (id, academic_semester_id),
    CONSTRAINT ck_teams_status CHECK (status IN (N'FORMING', N'ELIGIBLE', N'LOCKED', N'DISBANDED')),
    CONSTRAINT fk_teams_semester FOREIGN KEY (academic_semester_id)
        REFERENCES dbo.academic_semesters(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_teams_created_by FOREIGN KEY (created_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.team_members (
    id                   BIGINT IDENTITY(1,1) NOT NULL,
    team_id              BIGINT NOT NULL,
    academic_semester_id BIGINT NOT NULL,
    user_id              BIGINT NOT NULL,
    is_leader            BIT NOT NULL CONSTRAINT df_team_members_is_leader DEFAULT (0),
    joined_at            DATETIME2(0) NOT NULL CONSTRAINT df_team_members_joined_at DEFAULT (SYSUTCDATETIME()),
    left_at              DATETIME2(0) NULL,
    created_at           DATETIME2(0) NOT NULL CONSTRAINT df_team_members_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at           DATETIME2(0) NOT NULL CONSTRAINT df_team_members_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_team_members PRIMARY KEY (id),
    CONSTRAINT uq_team_members_team_user UNIQUE (team_id, user_id),
    CONSTRAINT ck_team_members_left_at CHECK (left_at IS NULL OR left_at >= joined_at),
    -- Composite FK guarantees academic_semester_id always matches the selected team.
    CONSTRAINT fk_team_members_team FOREIGN KEY (team_id, academic_semester_id)
        REFERENCES dbo.teams(id, academic_semester_id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_team_members_user FOREIGN KEY (user_id)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE UNIQUE INDEX ux_team_members_one_leader_per_team
ON dbo.team_members(team_id)
WHERE is_leader = 1 AND left_at IS NULL;
GO

-- A user can belong to only one active team in the same academic semester.
CREATE UNIQUE INDEX ux_team_members_one_active_team_per_semester
ON dbo.team_members(academic_semester_id, user_id)
WHERE left_at IS NULL;
GO

CREATE TABLE dbo.team_invitations (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    team_id         BIGINT NOT NULL,
    invited_user_id BIGINT NOT NULL,
    invited_by      BIGINT NOT NULL,
    status          NVARCHAR(20) NOT NULL CONSTRAINT df_team_invitations_status DEFAULT (N'PENDING'),
    message         NVARCHAR(1000) NULL,
    expires_at      DATETIME2(0) NULL,
    responded_at    DATETIME2(0) NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_team_invitations_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_team_invitations_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_team_invitations PRIMARY KEY (id),
    CONSTRAINT ck_team_invitations_status CHECK (status IN (N'PENDING', N'ACCEPTED', N'REJECTED', N'CANCELLED', N'EXPIRED')),
    CONSTRAINT fk_team_invitations_team FOREIGN KEY (team_id)
        REFERENCES dbo.teams(id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_team_invitations_invited_user FOREIGN KEY (invited_user_id)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_team_invitations_invited_by FOREIGN KEY (invited_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE UNIQUE INDEX ux_team_invitations_pending
ON dbo.team_invitations(team_id, invited_user_id)
WHERE status = N'PENDING';
GO

/* =========================================================
   PROJECT
   ========================================================= */

CREATE TABLE dbo.projects (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    team_id         BIGINT NOT NULL,
    code            NVARCHAR(50) NOT NULL,
    title           NVARCHAR(500) NOT NULL,
    description     NVARCHAR(MAX) NULL,
    objectives      NVARCHAR(MAX) NULL,
    status          NVARCHAR(30) NOT NULL CONSTRAINT df_projects_status DEFAULT (N'DRAFT'),
    registered_at   DATETIME2(0) NOT NULL CONSTRAINT df_projects_registered_at DEFAULT (SYSUTCDATETIME()),
    submitted_at    DATETIME2(0) NULL,
    approved_at     DATETIME2(0) NULL,
    completed_at    DATETIME2(0) NULL,
    created_by      BIGINT NOT NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_projects_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_projects_updated_at DEFAULT (SYSUTCDATETIME()),
    problem_statement NVARCHAR(MAX) NULL,
    expected_output NVARCHAR(MAX) NULL,
    row_version     ROWVERSION NOT NULL,
    CONSTRAINT pk_projects PRIMARY KEY (id),
    CONSTRAINT uq_projects_code UNIQUE (code),
    CONSTRAINT ck_projects_status CHECK (status IN (
        N'DRAFT', N'SUBMITTED', N'UNDER_REVIEW', N'REVISION_REQUIRED', N'REJECTED', N'APPROVED',
        N'SUPERVISOR_PENDING', N'ACTIVE', N'FINAL_SUBMISSION', N'COMPLETED', N'ARCHIVED'
    )),
    CONSTRAINT fk_projects_team FOREIGN KEY (team_id)
        REFERENCES dbo.teams(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_projects_created_by FOREIGN KEY (created_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE UNIQUE NONCLUSTERED INDEX uq_projects_active_team
ON dbo.projects (team_id)
WHERE status IN (
    N'DRAFT', N'SUBMITTED', N'UNDER_REVIEW', N'REVISION_REQUIRED',
    N'APPROVED', N'SUPERVISOR_PENDING', N'ACTIVE', N'FINAL_SUBMISSION'
);
GO

CREATE TABLE dbo.project_majors (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    project_id      BIGINT NOT NULL,
    major_id        BIGINT NOT NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_project_majors_created_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_project_majors PRIMARY KEY (id),
    CONSTRAINT uq_project_majors_project_major UNIQUE (project_id, major_id),
    CONSTRAINT fk_project_majors_project FOREIGN KEY (project_id)
        REFERENCES dbo.projects(id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_project_majors_major FOREIGN KEY (major_id)
        REFERENCES dbo.majors(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.tags (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    name            NVARCHAR(100) NOT NULL,
    normalized_name NVARCHAR(100) NOT NULL,
    tag_type        NVARCHAR(30) NOT NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_tags_created_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_tags PRIMARY KEY (id),
    CONSTRAINT uq_tags_normalized_name_type UNIQUE (normalized_name, tag_type),
    CONSTRAINT ck_tags_type CHECK (tag_type IN (N'DOMAIN', N'TECHNOLOGY', N'KEYWORD'))
);
GO

CREATE TABLE dbo.project_tags (
    project_id      BIGINT NOT NULL,
    tag_id          BIGINT NOT NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_project_tags_created_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_project_tags PRIMARY KEY CLUSTERED (project_id, tag_id),
    CONSTRAINT fk_project_tags_project FOREIGN KEY (project_id)
        REFERENCES dbo.projects(id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_project_tags_tag FOREIGN KEY (tag_id)
        REFERENCES dbo.tags(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE NONCLUSTERED INDEX ix_project_tags_tag_project ON dbo.project_tags (tag_id, project_id);
GO

CREATE TABLE dbo.project_status_history (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    project_id      BIGINT NOT NULL,
    old_status      NVARCHAR(30) NULL,
    new_status      NVARCHAR(30) NOT NULL,
    changed_by      BIGINT NOT NULL,
    reason          NVARCHAR(1000) NULL,
    changed_at      DATETIME2(0) NOT NULL CONSTRAINT df_project_status_history_changed_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_project_status_history PRIMARY KEY (id),
    CONSTRAINT ck_project_status_history_old CHECK (old_status IS NULL OR old_status IN (
        N'DRAFT', N'SUBMITTED', N'UNDER_REVIEW', N'REVISION_REQUIRED', N'REJECTED', N'APPROVED',
        N'SUPERVISOR_PENDING', N'ACTIVE', N'FINAL_SUBMISSION', N'COMPLETED', N'ARCHIVED'
    )),
    CONSTRAINT ck_project_status_history_new CHECK (new_status IN (
        N'DRAFT', N'SUBMITTED', N'UNDER_REVIEW', N'REVISION_REQUIRED', N'REJECTED', N'APPROVED',
        N'SUPERVISOR_PENDING', N'ACTIVE', N'FINAL_SUBMISSION', N'COMPLETED', N'ARCHIVED'
    )),
    CONSTRAINT fk_project_status_history_project FOREIGN KEY (project_id)
        REFERENCES dbo.projects(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_project_status_history_changed_by FOREIGN KEY (changed_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

/* =========================================================
   SUPERVISOR
   ========================================================= */

CREATE TABLE dbo.supervisor_profiles (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    user_id         BIGINT NOT NULL,
    bio             NVARCHAR(MAX) NULL,
    max_active_projects INT NULL,
    is_available    BIT NOT NULL CONSTRAINT df_supervisor_profiles_is_available DEFAULT (1),
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_profiles_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_profiles_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_supervisor_profiles PRIMARY KEY (id),
    CONSTRAINT uq_supervisor_profiles_user UNIQUE (user_id),
    CONSTRAINT ck_supervisor_profiles_capacity CHECK (max_active_projects IS NULL OR max_active_projects >= 0),
    CONSTRAINT fk_supervisor_profiles_user FOREIGN KEY (user_id)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.supervisor_expertise (
    id                      BIGINT IDENTITY(1,1) NOT NULL,
    supervisor_profile_id   BIGINT NOT NULL,
    expertise_name          NVARCHAR(255) NOT NULL,
    proficiency_level       NVARCHAR(50) NULL,
    created_at              DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_expertise_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at              DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_expertise_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_supervisor_expertise PRIMARY KEY (id),
    CONSTRAINT uq_supervisor_expertise_name UNIQUE (supervisor_profile_id, expertise_name),
    CONSTRAINT fk_supervisor_expertise_profile FOREIGN KEY (supervisor_profile_id)
        REFERENCES dbo.supervisor_profiles(id) ON DELETE CASCADE ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.supervisor_requests (
    id                      BIGINT IDENTITY(1,1) NOT NULL,
    project_id              BIGINT NOT NULL,
    supervisor_profile_id   BIGINT NOT NULL,
    requested_by            BIGINT NOT NULL,
    assignment_type         VARCHAR(30) NOT NULL CONSTRAINT df_supervisor_requests_assignment_type DEFAULT ('PRIMARY'),
    major_id                BIGINT NULL,
    status                  NVARCHAR(20) NOT NULL CONSTRAINT df_supervisor_requests_status DEFAULT (N'PENDING'),
    request_message         NVARCHAR(2000) NULL,
    response_message        NVARCHAR(2000) NULL,
    requested_at            DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_requests_requested_at DEFAULT (SYSUTCDATETIME()),
    responded_at            DATETIME2(0) NULL,
    created_at              DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_requests_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at              DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_requests_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_supervisor_requests PRIMARY KEY (id),
    CONSTRAINT ck_supervisor_requests_status CHECK (status IN (N'PENDING', N'ACCEPTED', N'REJECTED', N'CANCELLED')),
    CONSTRAINT ck_supervisor_requests_assignment_type CHECK (assignment_type IN ('PRIMARY','DISCIPLINE_MENTOR')),
    CONSTRAINT ck_supervisor_requests_assignment_slot CHECK ((assignment_type = 'PRIMARY' AND major_id IS NULL)
        OR (assignment_type = 'DISCIPLINE_MENTOR' AND major_id IS NOT NULL)),
    CONSTRAINT fk_supervisor_requests_project FOREIGN KEY (project_id)
        REFERENCES dbo.projects(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_supervisor_requests_profile FOREIGN KEY (supervisor_profile_id)
        REFERENCES dbo.supervisor_profiles(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_supervisor_requests_requested_by FOREIGN KEY (requested_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_supervisor_requests_major FOREIGN KEY (major_id)
        REFERENCES dbo.majors(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE UNIQUE INDEX ux_supervisor_requests_pending
ON dbo.supervisor_requests(project_id, supervisor_profile_id, assignment_type, major_id)
WHERE status = N'PENDING';
GO

CREATE TABLE dbo.supervisor_assignments (
    id                      BIGINT IDENTITY(1,1) NOT NULL,
    project_id              BIGINT NOT NULL,
    supervisor_profile_id   BIGINT NOT NULL,
    supervisor_request_id   BIGINT NOT NULL,
    is_primary              BIT NOT NULL CONSTRAINT df_supervisor_assignments_is_primary DEFAULT (0),
    assignment_type         VARCHAR(30) NOT NULL CONSTRAINT df_supervisor_assignments_assignment_type DEFAULT ('PRIMARY'),
    major_id                BIGINT NULL,
    assigned_by             BIGINT NULL,
    ended_by                BIGINT NULL,
    end_reason              NVARCHAR(2000) NULL,
    replaces_assignment_id  BIGINT NULL,
    assigned_at             DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_assignments_assigned_at DEFAULT (SYSUTCDATETIME()),
    ended_at                DATETIME2(0) NULL,
    created_at              DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_assignments_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at              DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_assignments_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_supervisor_assignments PRIMARY KEY (id),
    CONSTRAINT uq_supervisor_assignments_request UNIQUE (supervisor_request_id),
    CONSTRAINT ck_supervisor_assignments_dates CHECK (ended_at IS NULL OR ended_at >= assigned_at),
    CONSTRAINT ck_supervisor_assignments_assignment_type CHECK (assignment_type IN ('PRIMARY','DISCIPLINE_MENTOR')),
    CONSTRAINT ck_supervisor_assignments_assignment_slot CHECK ((assignment_type = 'PRIMARY' AND major_id IS NULL)
        OR (assignment_type = 'DISCIPLINE_MENTOR' AND major_id IS NOT NULL AND is_primary = 0)),
    CONSTRAINT fk_supervisor_assignments_assigned_by FOREIGN KEY (assigned_by) REFERENCES dbo.users(id),
    CONSTRAINT fk_supervisor_assignments_ended_by FOREIGN KEY (ended_by) REFERENCES dbo.users(id),
    CONSTRAINT fk_supervisor_assignments_replaces_assignment_id FOREIGN KEY (replaces_assignment_id) REFERENCES dbo.supervisor_assignments(id),
    CONSTRAINT fk_supervisor_assignments_project FOREIGN KEY (project_id)
        REFERENCES dbo.projects(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_supervisor_assignments_profile FOREIGN KEY (supervisor_profile_id)
        REFERENCES dbo.supervisor_profiles(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_supervisor_assignments_request FOREIGN KEY (supervisor_request_id)
        REFERENCES dbo.supervisor_requests(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_supervisor_assignments_major FOREIGN KEY (major_id)
        REFERENCES dbo.majors(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE UNIQUE INDEX ux_supervisor_assignments_replacement
ON dbo.supervisor_assignments(replaces_assignment_id) WHERE replaces_assignment_id IS NOT NULL;
GO

CREATE UNIQUE INDEX ux_supervisor_assignments_active_major_mentor
ON dbo.supervisor_assignments(project_id, major_id)
WHERE assignment_type = 'DISCIPLINE_MENTOR' AND ended_at IS NULL;
GO

CREATE UNIQUE INDEX ux_supervisor_assignments_one_primary_active
ON dbo.supervisor_assignments(project_id)
WHERE is_primary = 1 AND ended_at IS NULL;
GO

CREATE TABLE dbo.team_leader_change_requests (
    id                      BIGINT IDENTITY(1,1) NOT NULL,
    team_id                 BIGINT NOT NULL,
    project_id              BIGINT NOT NULL,
    requested_by            BIGINT NOT NULL,
    current_leader_user_id BIGINT NOT NULL,
    new_leader_user_id     BIGINT NOT NULL,
    mentor_profile_id       BIGINT NOT NULL,
    status                  NVARCHAR(20) NOT NULL CONSTRAINT df_team_leader_change_requests_status DEFAULT (N'PENDING'),
    request_message         NVARCHAR(2000) NULL,
    response_message        NVARCHAR(2000) NULL,
    requested_at            DATETIME2(0) NOT NULL CONSTRAINT df_team_leader_change_requests_requested_at DEFAULT (SYSUTCDATETIME()),
    responded_at            DATETIME2(0) NULL,
    created_at              DATETIME2(0) NOT NULL CONSTRAINT df_team_leader_change_requests_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at              DATETIME2(0) NOT NULL CONSTRAINT df_team_leader_change_requests_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_team_leader_change_requests PRIMARY KEY (id),
    CONSTRAINT ck_team_leader_change_requests_status CHECK (status IN (N'PENDING', N'APPROVED', N'REJECTED', N'CANCELLED')),
    CONSTRAINT fk_team_leader_change_requests_team FOREIGN KEY (team_id)
        REFERENCES dbo.teams(id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_team_leader_change_requests_project FOREIGN KEY (project_id)
        REFERENCES dbo.projects(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_team_leader_change_requests_requested_by FOREIGN KEY (requested_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_team_leader_change_requests_current_leader FOREIGN KEY (current_leader_user_id)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_team_leader_change_requests_new_leader FOREIGN KEY (new_leader_user_id)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_team_leader_change_requests_mentor_profile FOREIGN KEY (mentor_profile_id)
        REFERENCES dbo.supervisor_profiles(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE UNIQUE INDEX ux_team_leader_change_requests_pending
ON dbo.team_leader_change_requests(team_id)
WHERE status = N'PENDING';
GO

CREATE INDEX ix_team_leader_change_requests_team_status
ON dbo.team_leader_change_requests(team_id, status);
GO

CREATE INDEX ix_team_leader_change_requests_mentor_status
ON dbo.team_leader_change_requests(mentor_profile_id, status);
GO

/* =========================================================
   EXECUTION - MILESTONES / TASKS
   ========================================================= */

CREATE TABLE dbo.milestones (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    project_id      BIGINT NOT NULL,
    title           NVARCHAR(255) NOT NULL,
    description     NVARCHAR(MAX) NULL,
    start_date      DATE NULL,
    due_date        DATE NULL,
    status          NVARCHAR(20) NOT NULL CONSTRAINT df_milestones_status DEFAULT (N'PLANNED'),
    sort_order      INT NOT NULL CONSTRAINT df_milestones_sort_order DEFAULT (0),
    created_by      BIGINT NOT NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_milestones_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_milestones_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_milestones PRIMARY KEY (id),
    CONSTRAINT ck_milestones_dates CHECK (due_date IS NULL OR start_date IS NULL OR due_date >= start_date),
    CONSTRAINT ck_milestones_status CHECK (status IN (N'PLANNED', N'IN_PROGRESS', N'COMPLETED', N'CANCELLED')),
    CONSTRAINT fk_milestones_project FOREIGN KEY (project_id)
        REFERENCES dbo.projects(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_milestones_created_by FOREIGN KEY (created_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.tasks (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    milestone_id    BIGINT NOT NULL,
    parent_task_id  BIGINT NULL,
    title           NVARCHAR(255) NOT NULL,
    description     NVARCHAR(MAX) NULL,
    status          NVARCHAR(20) NOT NULL CONSTRAINT df_tasks_status DEFAULT (N'TODO'),
    priority        NVARCHAR(20) NULL,
    start_at        DATETIME2(0) NULL,
    due_at          DATETIME2(0) NULL,
    completed_at    DATETIME2(0) NULL,
    created_by      BIGINT NOT NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_tasks_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_tasks_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_tasks PRIMARY KEY (id),
    CONSTRAINT ck_tasks_status CHECK (status IN (N'TODO', N'IN_PROGRESS', N'BLOCKED', N'IN_REVIEW', N'DONE', N'CANCELLED')),
    CONSTRAINT ck_tasks_priority CHECK (priority IS NULL OR priority IN (N'LOW', N'MEDIUM', N'HIGH', N'CRITICAL')),
    CONSTRAINT ck_tasks_dates CHECK (due_at IS NULL OR start_at IS NULL OR due_at >= start_at),
    CONSTRAINT fk_tasks_milestone FOREIGN KEY (milestone_id)
        REFERENCES dbo.milestones(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_tasks_parent FOREIGN KEY (parent_task_id)
        REFERENCES dbo.tasks(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_tasks_created_by FOREIGN KEY (created_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.task_assignees (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    task_id         BIGINT NOT NULL,
    user_id         BIGINT NOT NULL,
    assigned_by     BIGINT NOT NULL,
    assigned_at     DATETIME2(0) NOT NULL CONSTRAINT df_task_assignees_assigned_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_task_assignees PRIMARY KEY (id),
    CONSTRAINT uq_task_assignees_task_user UNIQUE (task_id, user_id),
    CONSTRAINT fk_task_assignees_task FOREIGN KEY (task_id)
        REFERENCES dbo.tasks(id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_task_assignees_user FOREIGN KEY (user_id)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_task_assignees_assigned_by FOREIGN KEY (assigned_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.task_dependencies (
    id                  BIGINT IDENTITY(1,1) NOT NULL,
    task_id             BIGINT NOT NULL,
    depends_on_task_id  BIGINT NOT NULL,
    dependency_type     NVARCHAR(30) NOT NULL CONSTRAINT df_task_dependencies_type DEFAULT (N'FINISH_TO_START'),
    created_at          DATETIME2(0) NOT NULL CONSTRAINT df_task_dependencies_created_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_task_dependencies PRIMARY KEY (id),
    CONSTRAINT uq_task_dependencies_pair UNIQUE (task_id, depends_on_task_id),
    CONSTRAINT ck_task_dependencies_not_self CHECK (task_id <> depends_on_task_id),
    CONSTRAINT ck_task_dependencies_type CHECK (dependency_type IN (N'FINISH_TO_START', N'START_TO_START', N'FINISH_TO_FINISH', N'START_TO_FINISH')),
    CONSTRAINT fk_task_dependencies_task FOREIGN KEY (task_id)
        REFERENCES dbo.tasks(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_task_dependencies_depends_on FOREIGN KEY (depends_on_task_id)
        REFERENCES dbo.tasks(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.task_status_history (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    task_id         BIGINT NOT NULL,
    old_status      NVARCHAR(20) NULL,
    new_status      NVARCHAR(20) NOT NULL,
    changed_by      BIGINT NOT NULL,
    reason          NVARCHAR(1000) NULL,
    changed_at      DATETIME2(0) NOT NULL CONSTRAINT df_task_status_history_changed_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_task_status_history PRIMARY KEY (id),
    CONSTRAINT ck_task_status_history_old CHECK (old_status IS NULL OR old_status IN (N'TODO', N'IN_PROGRESS', N'BLOCKED', N'IN_REVIEW', N'DONE', N'CANCELLED')),
    CONSTRAINT ck_task_status_history_new CHECK (new_status IN (N'TODO', N'IN_PROGRESS', N'BLOCKED', N'IN_REVIEW', N'DONE', N'CANCELLED')),
    CONSTRAINT fk_task_status_history_task FOREIGN KEY (task_id)
        REFERENCES dbo.tasks(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_task_status_history_changed_by FOREIGN KEY (changed_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

/* =========================================================
   PROGRESS & COLLABORATION
   ========================================================= */

CREATE TABLE dbo.progress_reports (
    id                  BIGINT IDENTITY(1,1) NOT NULL,
    project_id          BIGINT NOT NULL,
    submitted_by        BIGINT NOT NULL,
    report_type         NVARCHAR(20) NOT NULL,
    period_start        DATE NOT NULL,
    period_end          DATE NOT NULL,
    summary             NVARCHAR(MAX) NOT NULL,
    completed_work      NVARCHAR(MAX) NULL,
    planned_work        NVARCHAR(MAX) NULL,
    issues_and_risks    NVARCHAR(MAX) NULL,
    status              NVARCHAR(20) NOT NULL CONSTRAINT df_progress_reports_status DEFAULT (N'DRAFT'),
    submitted_at        DATETIME2(0) NULL,
    created_at          DATETIME2(0) NOT NULL CONSTRAINT df_progress_reports_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at          DATETIME2(0) NOT NULL CONSTRAINT df_progress_reports_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_progress_reports PRIMARY KEY (id),
    CONSTRAINT uq_progress_reports_period UNIQUE (project_id, report_type, period_start, period_end),
    CONSTRAINT ck_progress_reports_type CHECK (report_type IN (N'WEEKLY', N'MONTHLY')),
    CONSTRAINT ck_progress_reports_status CHECK (status IN (N'DRAFT', N'SUBMITTED', N'REVIEWED')),
    CONSTRAINT ck_progress_reports_dates CHECK (period_end >= period_start),
    CONSTRAINT fk_progress_reports_project FOREIGN KEY (project_id)
        REFERENCES dbo.projects(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_progress_reports_submitted_by FOREIGN KEY (submitted_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.meetings (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    project_id      BIGINT NOT NULL,
    title           NVARCHAR(255) NOT NULL,
    agenda          NVARCHAR(MAX) NULL,
    meeting_notes   NVARCHAR(MAX) NULL,
    start_at        DATETIME2(0) NOT NULL,
    end_at          DATETIME2(0) NULL,
    location        NVARCHAR(500) NULL,
    online_url      NVARCHAR(1000) NULL,
    status          NVARCHAR(20) NOT NULL CONSTRAINT df_meetings_status DEFAULT (N'SCHEDULED'),
    created_by      BIGINT NOT NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_meetings_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_meetings_updated_at DEFAULT (SYSUTCDATETIME()),
    meeting_delivery_mode VARCHAR(20) NULL CONSTRAINT df_meetings_delivery_mode DEFAULT ('ONSITE'),
    video_channel   VARCHAR(30) NULL CONSTRAINT df_meetings_video_channel DEFAULT ('NONE'),
    CONSTRAINT pk_meetings PRIMARY KEY (id),
    CONSTRAINT ck_meetings_dates CHECK (end_at IS NULL OR end_at >= start_at),
    CONSTRAINT ck_meetings_status CHECK (status IN (N'SCHEDULED', N'COMPLETED', N'CANCELLED')),
    CONSTRAINT ck_meetings_delivery_mode CHECK (meeting_delivery_mode IN ('ONSITE','REMOTE','HYBRID')),
    CONSTRAINT ck_meetings_video_channel CHECK (video_channel IN ('NONE','EXTERNAL_LINK','IN_APP_VIDEO')),
    CONSTRAINT fk_meetings_project FOREIGN KEY (project_id)
        REFERENCES dbo.projects(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_meetings_created_by FOREIGN KEY (created_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.meeting_participants (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    meeting_id      BIGINT NOT NULL,
    user_id         BIGINT NOT NULL,
    attendance_status NVARCHAR(20) NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_meeting_participants_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_meeting_participants_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_meeting_participants PRIMARY KEY (id),
    CONSTRAINT uq_meeting_participants_meeting_user UNIQUE (meeting_id, user_id),
    CONSTRAINT ck_meeting_participants_attendance CHECK (attendance_status IS NULL OR attendance_status IN (N'INVITED', N'ACCEPTED', N'DECLINED', N'ATTENDED', N'ABSENT')),
    CONSTRAINT fk_meeting_participants_meeting FOREIGN KEY (meeting_id)
        REFERENCES dbo.meetings(id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_meeting_participants_user FOREIGN KEY (user_id)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.meeting_video_sessions (
    id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_video_sessions PRIMARY KEY,
    meeting_id BIGINT NOT NULL,
    provider VARCHAR(30) NOT NULL,
    provider_room_key VARCHAR(255) NOT NULL,
    status VARCHAR(20) NOT NULL,
    started_by BIGINT NULL,
    started_at DATETIME2(7) NULL,
    ended_at DATETIME2(7) NULL,
    failure_code VARCHAR(80) NULL,
    concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_meeting_video_sessions_token DEFAULT NEWSEQUENTIALID(),
    created_at DATETIME2(7) NOT NULL CONSTRAINT df_meeting_video_sessions_created DEFAULT SYSUTCDATETIME(),
    updated_at DATETIME2(7) NOT NULL CONSTRAINT df_meeting_video_sessions_updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT pk_meeting_video_sessions_check CHECK(status IN ('CREATED','LIVE','ENDED','FAILED')),
    CONSTRAINT fk_meeting_video_sessions_meeting FOREIGN KEY(meeting_id) REFERENCES dbo.meetings(id),
    CONSTRAINT fk_meeting_video_sessions_started_by FOREIGN KEY(started_by) REFERENCES dbo.users(id)
);
CREATE UNIQUE INDEX uq_meeting_video_sessions_room ON dbo.meeting_video_sessions(provider,provider_room_key);
CREATE UNIQUE INDEX ux_meeting_video_sessions_one_active ON dbo.meeting_video_sessions(meeting_id) WHERE status IN ('CREATED','LIVE');
CREATE INDEX ix_meeting_video_sessions_meeting_status ON dbo.meeting_video_sessions(meeting_id,status);
GO
CREATE TABLE dbo.meeting_video_participant_bindings (
    id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_video_bindings PRIMARY KEY,
    meeting_video_session_id BIGINT NOT NULL,
    user_id BIGINT NOT NULL,
    provider_participant_identity VARCHAR(255) NOT NULL,
    created_at DATETIME2(7) NOT NULL CONSTRAINT df_meeting_video_bindings_created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT fk_meeting_video_bindings_session FOREIGN KEY(meeting_video_session_id) REFERENCES dbo.meeting_video_sessions(id) ON DELETE CASCADE,
    CONSTRAINT fk_meeting_video_bindings_user FOREIGN KEY(user_id) REFERENCES dbo.users(id)
);
CREATE UNIQUE INDEX ux_meeting_video_bindings_session_user ON dbo.meeting_video_participant_bindings(meeting_video_session_id,user_id);
CREATE UNIQUE INDEX ux_meeting_video_bindings_identity ON dbo.meeting_video_participant_bindings(provider_participant_identity);
GO
CREATE TABLE dbo.meeting_video_presence_sessions (
    id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_video_presence PRIMARY KEY,
    meeting_video_session_id BIGINT NOT NULL,
    user_id BIGINT NOT NULL,
    provider_participant_identity VARCHAR(255) NOT NULL,
    provider_connection_id VARCHAR(255) NULL,
    joined_at DATETIME2(7) NOT NULL,
    left_at DATETIME2(7) NULL,
    disconnect_reason NVARCHAR(255) NULL,
    created_at DATETIME2(7) NOT NULL CONSTRAINT df_meeting_video_presence_created DEFAULT SYSUTCDATETIME(),
    updated_at DATETIME2(7) NOT NULL CONSTRAINT df_meeting_video_presence_updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT fk_meeting_video_presence_session FOREIGN KEY(meeting_video_session_id) REFERENCES dbo.meeting_video_sessions(id) ON DELETE CASCADE,
    CONSTRAINT fk_meeting_video_presence_user FOREIGN KEY(user_id) REFERENCES dbo.users(id)
);
CREATE INDEX ix_meeting_video_presence_lookup ON dbo.meeting_video_presence_sessions(meeting_video_session_id,user_id,joined_at);
GO
CREATE TABLE dbo.video_provider_events (
    id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_video_provider_events PRIMARY KEY,
    provider VARCHAR(30) NOT NULL,
    provider_event_id VARCHAR(255) NOT NULL,
    event_type VARCHAR(80) NOT NULL,
    meeting_video_session_id BIGINT NULL,
    received_at DATETIME2(7) NOT NULL CONSTRAINT df_video_provider_events_received DEFAULT SYSUTCDATETIME(),
    processed_at DATETIME2(7) NULL,
    payload_hash CHAR(64) NOT NULL,
    processing_status VARCHAR(20) NOT NULL,
    error_code VARCHAR(80) NULL,
    CONSTRAINT fk_video_provider_events_session FOREIGN KEY(meeting_video_session_id) REFERENCES dbo.meeting_video_sessions(id)
);
CREATE UNIQUE INDEX ux_video_provider_events_id ON dbo.video_provider_events(provider,provider_event_id);
GO
CREATE TABLE dbo.video_provider_cleanup_jobs (
    id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_video_provider_cleanup_jobs PRIMARY KEY,
    meeting_video_session_id BIGINT NOT NULL,
    provider_room_key VARCHAR(255) NOT NULL,
    status VARCHAR(20) NOT NULL,
    attempt_count INT NOT NULL CONSTRAINT df_video_cleanup_attempts DEFAULT 0,
    next_attempt_at DATETIME2(7) NOT NULL,
    lease_token UNIQUEIDENTIFIER NULL,
    lease_until DATETIME2(7) NULL,
    last_error_code VARCHAR(80) NULL,
    created_at DATETIME2(7) NOT NULL CONSTRAINT df_video_cleanup_created DEFAULT SYSUTCDATETIME(),
    completed_at DATETIME2(7) NULL,
    CONSTRAINT ck_video_cleanup_status CHECK(status IN ('PENDING','PROCESSING','SUCCEEDED','FAILED')),
    CONSTRAINT fk_video_cleanup_session FOREIGN KEY(meeting_video_session_id) REFERENCES dbo.meeting_video_sessions(id) ON DELETE CASCADE
);
CREATE INDEX ix_video_cleanup_claim ON dbo.video_provider_cleanup_jobs(status,next_attempt_at,lease_until);
GO
CREATE TABLE dbo.deliverables (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    project_id      BIGINT NOT NULL,
    milestone_id    BIGINT NULL,
    title           NVARCHAR(255) NOT NULL,
    description     NVARCHAR(MAX) NULL,
    deliverable_type NVARCHAR(50) NULL,
    due_at          DATETIME2(0) NULL,
    status          NVARCHAR(20) NOT NULL CONSTRAINT df_deliverables_status DEFAULT (N'DRAFT'),
    created_by      BIGINT NOT NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_deliverables_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_deliverables_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_deliverables PRIMARY KEY (id),
    CONSTRAINT ck_deliverables_status CHECK (status IN (N'DRAFT', N'OPEN', N'SUBMITTED', N'ACCEPTED', N'REJECTED', N'CLOSED')),
    CONSTRAINT fk_deliverables_project FOREIGN KEY (project_id)
        REFERENCES dbo.projects(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_deliverables_milestone FOREIGN KEY (milestone_id)
        REFERENCES dbo.milestones(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_deliverables_created_by FOREIGN KEY (created_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.deliverable_versions (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    deliverable_id  BIGINT NOT NULL,
    version_number  INT NOT NULL,
    submitted_by    BIGINT NOT NULL,
    submission_note NVARCHAR(MAX) NULL,
    status          NVARCHAR(20) NOT NULL CONSTRAINT df_deliverable_versions_status DEFAULT (N'SUBMITTED'),
    submitted_at    DATETIME2(0) NOT NULL CONSTRAINT df_deliverable_versions_submitted_at DEFAULT (SYSUTCDATETIME()),
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_deliverable_versions_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_deliverable_versions_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_deliverable_versions PRIMARY KEY (id),
    CONSTRAINT uq_deliverable_versions_number UNIQUE (deliverable_id, version_number),
    CONSTRAINT ck_deliverable_versions_number CHECK (version_number > 0),
    CONSTRAINT ck_deliverable_versions_status CHECK (status IN (N'SUBMITTED', N'ACCEPTED', N'REJECTED', N'SUPERSEDED')),
    CONSTRAINT fk_deliverable_versions_deliverable FOREIGN KEY (deliverable_id)
        REFERENCES dbo.deliverables(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_deliverable_versions_submitted_by FOREIGN KEY (submitted_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.supervisor_feedback (
    id                      BIGINT IDENTITY(1,1) NOT NULL,
    project_id              BIGINT NOT NULL,
    supervisor_assignment_id BIGINT NOT NULL,
    progress_report_id      BIGINT NULL,
    deliverable_version_id  BIGINT NULL,
    meeting_id              BIGINT NULL,
    feedback_text           NVARCHAR(MAX) NOT NULL,
    created_at              DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_feedback_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at              DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_feedback_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_supervisor_feedback PRIMARY KEY (id),
    CONSTRAINT fk_supervisor_feedback_project FOREIGN KEY (project_id)
        REFERENCES dbo.projects(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_supervisor_feedback_assignment FOREIGN KEY (supervisor_assignment_id)
        REFERENCES dbo.supervisor_assignments(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_supervisor_feedback_progress_report FOREIGN KEY (progress_report_id)
        REFERENCES dbo.progress_reports(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_supervisor_feedback_deliverable_version FOREIGN KEY (deliverable_version_id)
        REFERENCES dbo.deliverable_versions(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_supervisor_feedback_meeting FOREIGN KEY (meeting_id)
        REFERENCES dbo.meetings(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.files (
    id                      BIGINT IDENTITY(1,1) NOT NULL,
    uploaded_by             BIGINT NOT NULL,
    deliverable_version_id  BIGINT NULL,
    progress_report_id      BIGINT NULL,
    meeting_id              BIGINT NULL,
    supervisor_feedback_id  BIGINT NULL,
    task_id                 BIGINT NULL,
    original_file_name      NVARCHAR(500) NOT NULL,
    stored_file_name        NVARCHAR(500) NULL,
    storage_path            NVARCHAR(2000) NOT NULL,
    file_url                NVARCHAR(2000) NULL,
    mime_type               NVARCHAR(255) NULL,
    file_size_bytes         BIGINT NOT NULL,
    checksum_sha256         CHAR(64) NULL,
    created_at              DATETIME2(0) NOT NULL CONSTRAINT df_files_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at              DATETIME2(0) NOT NULL CONSTRAINT df_files_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_files PRIMARY KEY (id),
    CONSTRAINT ck_files_size CHECK (file_size_bytes >= 0),
    CONSTRAINT ck_files_single_parent CHECK (
        (CASE WHEN deliverable_version_id IS NULL THEN 0 ELSE 1 END) +
        (CASE WHEN progress_report_id IS NULL THEN 0 ELSE 1 END) +
        (CASE WHEN meeting_id IS NULL THEN 0 ELSE 1 END) +
        (CASE WHEN supervisor_feedback_id IS NULL THEN 0 ELSE 1 END) +
        (CASE WHEN task_id IS NULL THEN 0 ELSE 1 END) <= 1
    ),
    CONSTRAINT fk_files_uploaded_by FOREIGN KEY (uploaded_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_files_deliverable_version FOREIGN KEY (deliverable_version_id)
        REFERENCES dbo.deliverable_versions(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_files_progress_report FOREIGN KEY (progress_report_id)
        REFERENCES dbo.progress_reports(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_files_meeting FOREIGN KEY (meeting_id)
        REFERENCES dbo.meetings(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_files_task FOREIGN KEY (task_id)
        REFERENCES dbo.tasks(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_files_supervisor_feedback FOREIGN KEY (supervisor_feedback_id)
        REFERENCES dbo.supervisor_feedback(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

/* =========================================================
   EVALUATION
   ========================================================= */

CREATE TABLE dbo.evaluation_criteria (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    code            NVARCHAR(50) NOT NULL,
    name            NVARCHAR(255) NOT NULL,
    description     NVARCHAR(1000) NULL,
    is_active       BIT NOT NULL CONSTRAINT df_evaluation_criteria_is_active DEFAULT (1),
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_evaluation_criteria_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_evaluation_criteria_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_evaluation_criteria PRIMARY KEY (id),
    CONSTRAINT uq_evaluation_criteria_code UNIQUE (code)
);
GO

/*
 Rubrics scope evaluation rules to a department, an academic semester, or both.
 At least one scope must be provided, so a rubric is never an unscoped global grading rule.
*/
CREATE TABLE dbo.rubrics (
    id                   BIGINT IDENTITY(1,1) NOT NULL,
    department_id        BIGINT NULL,
    academic_semester_id BIGINT NULL,
    code                 NVARCHAR(50) NOT NULL,
    name                 NVARCHAR(255) NOT NULL,
    description          NVARCHAR(1000) NULL,
    is_active            BIT NOT NULL CONSTRAINT df_rubrics_is_active DEFAULT (1),
    created_by           BIGINT NOT NULL,
    created_at           DATETIME2(0) NOT NULL CONSTRAINT df_rubrics_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at           DATETIME2(0) NOT NULL CONSTRAINT df_rubrics_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_rubrics PRIMARY KEY (id),
    CONSTRAINT uq_rubrics_code UNIQUE (code),
    CONSTRAINT ck_rubrics_scope CHECK (department_id IS NOT NULL OR academic_semester_id IS NOT NULL),
    CONSTRAINT fk_rubrics_department FOREIGN KEY (department_id)
        REFERENCES dbo.departments(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_rubrics_semester FOREIGN KEY (academic_semester_id)
        REFERENCES dbo.academic_semesters(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_rubrics_created_by FOREIGN KEY (created_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

ALTER TABLE dbo.project_periods ADD CONSTRAINT fk_project_periods_rubric
    FOREIGN KEY (rubric_id) REFERENCES dbo.rubrics(id) ON DELETE NO ACTION ON UPDATE NO ACTION;
GO

CREATE TABLE dbo.rubric_criteria (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    rubric_id       BIGINT NOT NULL,
    criterion_id    BIGINT NOT NULL,
    parent_id       BIGINT NULL,
    weight_percent  DECIMAL(5,2) NOT NULL,
    max_score       DECIMAL(8,2) NULL,
    sort_order      INT NOT NULL CONSTRAINT df_rubric_criteria_sort_order DEFAULT (0),
    is_required     BIT NOT NULL CONSTRAINT df_rubric_criteria_is_required DEFAULT (1),
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_rubric_criteria_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_rubric_criteria_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_rubric_criteria PRIMARY KEY (id),
    CONSTRAINT uq_rubric_criteria_id_rubric UNIQUE (id, rubric_id),
    CONSTRAINT ck_rubric_criteria_parent CHECK (parent_id <> id),
    CONSTRAINT fk_rubric_criteria_parent FOREIGN KEY (parent_id, rubric_id)
        REFERENCES dbo.rubric_criteria(id, rubric_id),
    CONSTRAINT uq_rubric_criteria_rubric_criterion UNIQUE (rubric_id, criterion_id),
    CONSTRAINT ck_rubric_criteria_weight CHECK (weight_percent >= 0 AND weight_percent <= 100),
    CONSTRAINT ck_rubric_criteria_max_score CHECK (max_score > 0),
    CONSTRAINT fk_rubric_criteria_rubric FOREIGN KEY (rubric_id)
        REFERENCES dbo.rubrics(id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_rubric_criteria_criterion FOREIGN KEY (criterion_id)
        REFERENCES dbo.evaluation_criteria(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.evaluations (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    project_id      BIGINT NOT NULL,
    evaluator_id    BIGINT NOT NULL,
    rubric_id       BIGINT NOT NULL,
    evaluation_type NVARCHAR(30) NOT NULL,
    status          NVARCHAR(20) NOT NULL CONSTRAINT df_evaluations_status DEFAULT (N'DRAFT'),
    total_score     DECIMAL(10,2) NULL,
    comments        NVARCHAR(MAX) NULL,
    evaluated_at    DATETIME2(0) NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_evaluations_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_evaluations_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_evaluations PRIMARY KEY (id),
    CONSTRAINT ck_evaluations_type CHECK (evaluation_type IN (N'SUPERVISOR', N'LECTURER', N'COMMITTEE', N'FINAL')),
    CONSTRAINT ck_evaluations_status CHECK (status IN (N'DRAFT', N'SUBMITTED', N'FINALIZED')),
    CONSTRAINT ck_evaluations_total_score CHECK (total_score IS NULL OR total_score >= 0),
    CONSTRAINT fk_evaluations_project FOREIGN KEY (project_id)
        REFERENCES dbo.projects(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_evaluations_evaluator FOREIGN KEY (evaluator_id)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_evaluations_rubric FOREIGN KEY (rubric_id)
        REFERENCES dbo.rubrics(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.evaluation_details (
    id                  BIGINT IDENTITY(1,1) NOT NULL,
    evaluation_id       BIGINT NOT NULL,
    rubric_criterion_id BIGINT NOT NULL,
    score               DECIMAL(8,2) NOT NULL,
    comments            NVARCHAR(2000) NULL,
    created_at          DATETIME2(0) NOT NULL CONSTRAINT df_evaluation_details_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at          DATETIME2(0) NOT NULL CONSTRAINT df_evaluation_details_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_evaluation_details PRIMARY KEY (id),
    CONSTRAINT uq_evaluation_details_eval_rubric_criterion UNIQUE (evaluation_id, rubric_criterion_id),
    CONSTRAINT ck_evaluation_details_score CHECK (score >= 0),
    CONSTRAINT fk_evaluation_details_evaluation FOREIGN KEY (evaluation_id)
        REFERENCES dbo.evaluations(id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_evaluation_details_rubric_criterion FOREIGN KEY (rubric_criterion_id)
        REFERENCES dbo.rubric_criteria(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

/* =========================================================
   NOTIFICATION
   ========================================================= */

CREATE TABLE dbo.notifications (
    id                  BIGINT IDENTITY(1,1) NOT NULL,
    created_by          BIGINT NULL,
    notification_type   NVARCHAR(50) NOT NULL,
    title               NVARCHAR(255) NOT NULL,
    content             NVARCHAR(MAX) NOT NULL,
    related_entity_type NVARCHAR(50) NULL,
    related_entity_id   BIGINT NULL,
    created_at          DATETIME2(0) NOT NULL CONSTRAINT df_notifications_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at          DATETIME2(0) NOT NULL CONSTRAINT df_notifications_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_notifications PRIMARY KEY (id),
    CONSTRAINT fk_notifications_created_by FOREIGN KEY (created_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE TABLE dbo.notification_recipients (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    notification_id BIGINT NOT NULL,
    user_id         BIGINT NOT NULL,
    is_read         BIT NOT NULL CONSTRAINT df_notification_recipients_is_read DEFAULT (0),
    read_at         DATETIME2(0) NULL,
    delivered_at    DATETIME2(0) NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_notification_recipients_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_notification_recipients_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_notification_recipients PRIMARY KEY (id),
    CONSTRAINT uq_notification_recipients_notification_user UNIQUE (notification_id, user_id),
    CONSTRAINT ck_notification_recipients_read_at CHECK ((is_read = 0 AND read_at IS NULL) OR is_read = 1),
    CONSTRAINT fk_notification_recipients_notification FOREIGN KEY (notification_id)
        REFERENCES dbo.notifications(id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_notification_recipients_user FOREIGN KEY (user_id)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

/* =========================================================
   INDEXES FOR COMMON FK / STATUS QUERIES
   ========================================================= */

CREATE INDEX ix_departments_organization_id ON dbo.departments(organization_id);
CREATE INDEX ix_majors_department_id ON dbo.majors(department_id);
CREATE INDEX ix_academic_semesters_organization_status ON dbo.academic_semesters(organization_id, status);
CREATE INDEX ix_project_periods_semester_status ON dbo.project_periods(academic_semester_id, status);
CREATE INDEX ix_users_department_id ON dbo.users(department_id) WHERE department_id IS NOT NULL;
CREATE INDEX ix_users_major_id ON dbo.users(major_id) WHERE major_id IS NOT NULL;
CREATE INDEX ix_user_roles_role_id ON dbo.user_roles(role_id);
CREATE INDEX ix_user_roles_assigned_by ON dbo.user_roles(assigned_by) WHERE assigned_by IS NOT NULL;
CREATE INDEX ix_role_permissions_permission_id ON dbo.role_permissions(permission_id);
CREATE INDEX ix_role_permissions_assigned_by ON dbo.role_permissions(assigned_by) WHERE assigned_by IS NOT NULL;
CREATE INDEX ix_refresh_tokens_user_active ON dbo.refresh_tokens(user_id, expires_at DESC) WHERE revoked_at IS NULL;
CREATE INDEX ix_refresh_tokens_family_id ON dbo.refresh_tokens(family_id, created_at DESC);
CREATE INDEX ix_password_reset_tokens_user_active ON dbo.password_reset_tokens(user_id, expires_at DESC) WHERE used_at IS NULL;
CREATE INDEX ix_audit_logs_occurred_at ON dbo.audit_logs(occurred_at DESC);
CREATE INDEX ix_audit_logs_actor_occurred_at ON dbo.audit_logs(actor_user_id, occurred_at DESC) WHERE actor_user_id IS NOT NULL;
CREATE INDEX ix_audit_logs_entity ON dbo.audit_logs(entity_type, entity_id, occurred_at DESC);
CREATE INDEX ix_audit_logs_correlation_id ON dbo.audit_logs(correlation_id) WHERE correlation_id IS NOT NULL;
CREATE INDEX ix_teams_semester_status ON dbo.teams(academic_semester_id, status);
CREATE INDEX ix_team_members_user_id ON dbo.team_members(user_id);
CREATE INDEX ix_team_members_semester_user ON dbo.team_members(academic_semester_id, user_id);
CREATE INDEX ix_team_invitations_invited_user_status ON dbo.team_invitations(invited_user_id, status);
CREATE INDEX ix_projects_status ON dbo.projects(status);
CREATE INDEX ix_project_majors_major_id ON dbo.project_majors(major_id);
CREATE INDEX ix_project_status_history_project_changed_at ON dbo.project_status_history(project_id, changed_at DESC);
CREATE INDEX ix_supervisor_expertise_profile_id ON dbo.supervisor_expertise(supervisor_profile_id);
CREATE INDEX ix_supervisor_requests_project_status ON dbo.supervisor_requests(project_id, status);
CREATE INDEX ix_supervisor_requests_supervisor_status ON dbo.supervisor_requests(supervisor_profile_id, status);
CREATE INDEX ix_supervisor_assignments_project ON dbo.supervisor_assignments(project_id);
CREATE INDEX ix_supervisor_assignments_supervisor ON dbo.supervisor_assignments(supervisor_profile_id);
CREATE INDEX ix_milestones_project_status ON dbo.milestones(project_id, status);
CREATE INDEX ix_tasks_milestone_status ON dbo.tasks(milestone_id, status);
CREATE INDEX ix_tasks_parent_task_id ON dbo.tasks(parent_task_id) WHERE parent_task_id IS NOT NULL;
CREATE INDEX ix_task_assignees_user_id ON dbo.task_assignees(user_id);
CREATE INDEX ix_task_dependencies_depends_on ON dbo.task_dependencies(depends_on_task_id);
CREATE INDEX ix_task_status_history_task_changed_at ON dbo.task_status_history(task_id, changed_at DESC);
CREATE INDEX ix_progress_reports_project_period ON dbo.progress_reports(project_id, period_start, period_end);
CREATE INDEX ix_meetings_project_start_at ON dbo.meetings(project_id, start_at);
CREATE INDEX ix_meeting_participants_user_id ON dbo.meeting_participants(user_id);
CREATE INDEX ix_deliverables_project_status ON dbo.deliverables(project_id, status);
CREATE INDEX ix_deliverables_milestone_id ON dbo.deliverables(milestone_id) WHERE milestone_id IS NOT NULL;
CREATE INDEX ix_deliverable_versions_deliverable_id ON dbo.deliverable_versions(deliverable_id);
CREATE INDEX ix_supervisor_feedback_project_id ON dbo.supervisor_feedback(project_id);
CREATE INDEX ix_files_uploaded_by ON dbo.files(uploaded_by);
CREATE INDEX ix_files_deliverable_version_id ON dbo.files(deliverable_version_id) WHERE deliverable_version_id IS NOT NULL;
CREATE INDEX ix_files_progress_report_id ON dbo.files(progress_report_id) WHERE progress_report_id IS NOT NULL;
CREATE INDEX ix_files_meeting_id ON dbo.files(meeting_id) WHERE meeting_id IS NOT NULL;
CREATE INDEX ix_rubrics_department_active ON dbo.rubrics(department_id, is_active) WHERE department_id IS NOT NULL;
CREATE INDEX ix_rubrics_semester_active ON dbo.rubrics(academic_semester_id, is_active) WHERE academic_semester_id IS NOT NULL;
CREATE INDEX ix_rubric_criteria_rubric_sort ON dbo.rubric_criteria(rubric_id, sort_order);
CREATE INDEX ix_rubric_criteria_criterion_id ON dbo.rubric_criteria(criterion_id);
CREATE INDEX ix_rubric_criteria_parent_id ON dbo.rubric_criteria(parent_id);
CREATE INDEX ix_evaluations_project_type_status ON dbo.evaluations(project_id, evaluation_type, status);
CREATE INDEX ix_evaluations_evaluator_id ON dbo.evaluations(evaluator_id);
CREATE INDEX ix_evaluations_rubric_id ON dbo.evaluations(rubric_id);
CREATE INDEX ix_evaluation_details_rubric_criterion_id ON dbo.evaluation_details(rubric_criterion_id);
CREATE INDEX ix_notifications_related_entity ON dbo.notifications(related_entity_type, related_entity_id);
CREATE INDEX ix_notification_recipients_user_read ON dbo.notification_recipients(user_id, is_read, created_at DESC);
GO

PRINT N'AI-PMS schema created successfully.';

-- Milestone templates are versioned so applied versions remain immutable.
IF OBJECT_ID(N'dbo.milestone_templates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.milestone_templates (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_milestone_templates PRIMARY KEY,
        name NVARCHAR(255) NOT NULL, description NVARCHAR(MAX) NULL,
        status NVARCHAR(20) NOT NULL CONSTRAINT df_milestone_templates_status DEFAULT (N'ACTIVE'),
        created_by BIGINT NOT NULL, created_at DATETIME2(0) NOT NULL CONSTRAINT df_milestone_templates_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_milestone_templates_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT ck_milestone_templates_status CHECK (status IN (N'ACTIVE', N'INACTIVE')),
        CONSTRAINT fk_milestone_templates_created_by FOREIGN KEY (created_by) REFERENCES dbo.users(id));
END;
IF OBJECT_ID(N'dbo.milestone_template_versions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.milestone_template_versions (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_milestone_template_versions PRIMARY KEY,
        milestone_template_id BIGINT NOT NULL, version_number INT NOT NULL,
        status NVARCHAR(20) NOT NULL CONSTRAINT df_milestone_template_versions_status DEFAULT (N'DRAFT'),
        created_by BIGINT NOT NULL, created_at DATETIME2(0) NOT NULL CONSTRAINT df_milestone_template_versions_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_milestone_template_versions_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT ck_milestone_template_versions_status CHECK (status IN (N'DRAFT', N'PUBLISHED', N'ARCHIVED')),
        CONSTRAINT uq_milestone_template_versions_template_version UNIQUE (milestone_template_id, version_number),
        CONSTRAINT fk_milestone_template_versions_template FOREIGN KEY (milestone_template_id) REFERENCES dbo.milestone_templates(id),
        CONSTRAINT fk_milestone_template_versions_created_by FOREIGN KEY (created_by) REFERENCES dbo.users(id));
END;
IF OBJECT_ID(N'dbo.milestone_template_items', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.milestone_template_items (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_milestone_template_items PRIMARY KEY,
        milestone_template_version_id BIGINT NOT NULL, title NVARCHAR(255) NOT NULL, description NVARCHAR(MAX) NULL,
        start_offset_days INT NULL, due_offset_days INT NULL, sort_order INT NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_milestone_template_items_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_milestone_template_items_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT ck_milestone_template_items_offsets CHECK (due_offset_days IS NULL OR start_offset_days IS NULL OR due_offset_days >= start_offset_days),
        CONSTRAINT fk_milestone_template_items_version FOREIGN KEY (milestone_template_version_id) REFERENCES dbo.milestone_template_versions(id) ON DELETE CASCADE);
END;
IF OBJECT_ID(N'dbo.project_milestone_template_applications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_milestone_template_applications (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_milestone_template_applications PRIMARY KEY,
        project_id BIGINT NOT NULL, milestone_template_version_id BIGINT NOT NULL, applied_by BIGINT NOT NULL,
        applied_at DATETIME2(0) NOT NULL CONSTRAINT df_project_milestone_template_applications_applied_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT uq_project_milestone_template_applications_project UNIQUE (project_id),
        CONSTRAINT fk_project_milestone_template_applications_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id) ON DELETE CASCADE,
        CONSTRAINT fk_project_milestone_template_applications_version FOREIGN KEY (milestone_template_version_id) REFERENCES dbo.milestone_template_versions(id),
        CONSTRAINT fk_project_milestone_template_applications_user FOREIGN KEY (applied_by) REFERENCES dbo.users(id));
END;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_project_periods_milestone_template')
    ALTER TABLE dbo.project_periods ADD CONSTRAINT fk_project_periods_milestone_template FOREIGN KEY (milestone_template_id) REFERENCES dbo.milestone_templates(id);
IF COL_LENGTH(N'dbo.files', N'task_id') IS NULL ALTER TABLE dbo.files ADD task_id BIGINT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_files_task') ALTER TABLE dbo.files ADD CONSTRAINT fk_files_task FOREIGN KEY (task_id) REFERENCES dbo.tasks(id);
IF OBJECT_ID(N'dbo.task_comments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.task_comments (id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_task_comments PRIMARY KEY, task_id BIGINT NOT NULL, author_id BIGINT NOT NULL, content NVARCHAR(4000) NOT NULL, created_at DATETIME2(0) NOT NULL CONSTRAINT df_task_comments_created_at DEFAULT (SYSUTCDATETIME()), updated_at DATETIME2(0) NOT NULL CONSTRAINT df_task_comments_updated_at DEFAULT (SYSUTCDATETIME()), CONSTRAINT fk_task_comments_task FOREIGN KEY (task_id) REFERENCES dbo.tasks(id), CONSTRAINT fk_task_comments_author FOREIGN KEY (author_id) REFERENCES dbo.users(id));
END;



GO
CREATE TABLE dbo.academic_profile_verifications (
    id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_academic_profile_verifications PRIMARY KEY,
    user_id BIGINT NOT NULL,
    status NVARCHAR(20) NOT NULL,
    reviewed_by BIGINT NULL,
    reviewed_at DATETIME2(0) NULL,
    rejection_reason NVARCHAR(2000) NULL,
    CONSTRAINT ck_academic_profile_verifications_status CHECK (status IN (N'PENDING', N'VERIFIED', N'REJECTED')),
    CONSTRAINT ck_academic_profile_verifications_reason CHECK (status <> N'REJECTED' OR (rejection_reason IS NOT NULL AND LEN(LTRIM(RTRIM(rejection_reason))) > 0)),
    CONSTRAINT fk_academic_profile_verifications_user FOREIGN KEY (user_id) REFERENCES dbo.users(id),
    CONSTRAINT fk_academic_profile_verifications_reviewer FOREIGN KEY (reviewed_by) REFERENCES dbo.users(id)
);
CREATE INDEX ix_academic_profile_verifications_user ON dbo.academic_profile_verifications(user_id, id);

GO
ALTER TABLE dbo.projects ADD milestones_initialized BIT NOT NULL CONSTRAINT df_projects_milestones_initialized DEFAULT (0);
GO
IF COL_LENGTH(N'dbo.milestone_template_versions', N'locked_at') IS NULL ALTER TABLE dbo.milestone_template_versions ADD locked_at DATETIME2(0) NULL;
GO

GO
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.user_external_logins', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.user_external_logins (
            id bigint IDENTITY PRIMARY KEY,
            user_id bigint NOT NULL REFERENCES dbo.users(id),
            provider varchar(30) NOT NULL,
            subject varchar(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
            email nvarchar(255) NOT NULL,
            created_at datetime2 NOT NULL,
            updated_at datetime2 NOT NULL,
            CONSTRAINT uq_external_login_subject UNIQUE(provider, subject),
            CONSTRAINT uq_external_login_user UNIQUE(user_id, provider)
        );
    END;
    IF OBJECT_ID(N'dbo.external_login_challenges', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.external_login_challenges (
            id uniqueidentifier NOT NULL PRIMARY KEY,
            purpose varchar(10) NOT NULL,
            user_id bigint NULL REFERENCES dbo.users(id),
            nonce_hash binary(64) NOT NULL,
            browser_hash binary(64) NOT NULL,
            created_at datetime2 NOT NULL,
            expires_at datetime2 NOT NULL,
            consumed_at datetime2 NULL,
            CONSTRAINT ck_external_challenge_purpose CHECK
                ((purpose='LOGIN' AND user_id IS NULL) OR (purpose='LINK' AND user_id IS NOT NULL))
        );
        CREATE INDEX ix_external_challenges_expiry ON dbo.external_login_challenges(expires_at);
    END;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
GO

/* Additive governance foundation for the v3 baseline. Safe to run repeatedly. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.tasks', N'concurrency_token') IS NULL
    ALTER TABLE dbo.tasks ADD concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_tasks_concurrency_token DEFAULT (NEWSEQUENTIALID());
IF COL_LENGTH(N'dbo.milestones', N'concurrency_token') IS NULL
    ALTER TABLE dbo.milestones ADD concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_milestones_concurrency_token DEFAULT (NEWSEQUENTIALID());
IF COL_LENGTH(N'dbo.progress_reports', N'concurrency_token') IS NULL
    ALTER TABLE dbo.progress_reports ADD concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_progress_reports_concurrency_token DEFAULT (NEWSEQUENTIALID());
IF COL_LENGTH(N'dbo.meetings', N'concurrency_token') IS NULL
    ALTER TABLE dbo.meetings ADD concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_meetings_concurrency_token DEFAULT (NEWSEQUENTIALID());

IF OBJECT_ID(N'dbo.meeting_decisions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meeting_decisions (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_decisions PRIMARY KEY,
        meeting_id BIGINT NOT NULL,
        content NVARCHAR(4000) NOT NULL,
        decided_by BIGINT NOT NULL,
        decided_at DATETIME2(0) NOT NULL CONSTRAINT df_meeting_decisions_decided_at DEFAULT (SYSUTCDATETIME()),
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_meeting_decisions_created_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_meeting_decisions_meeting FOREIGN KEY (meeting_id) REFERENCES dbo.meetings(id),
        CONSTRAINT fk_meeting_decisions_user FOREIGN KEY (decided_by) REFERENCES dbo.users(id)
    );
END;

IF OBJECT_ID(N'dbo.meeting_action_items', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meeting_action_items (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_action_items PRIMARY KEY,
        meeting_id BIGINT NOT NULL,
        title NVARCHAR(500) NOT NULL,
        description NVARCHAR(4000) NULL,
        assignee_user_id BIGINT NULL,
        due_at DATETIME2(0) NULL,
        status NVARCHAR(20) NOT NULL CONSTRAINT df_meeting_action_items_status DEFAULT (N'OPEN'),
        concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_meeting_action_items_token DEFAULT (NEWSEQUENTIALID()),
        created_by BIGINT NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_meeting_action_items_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_meeting_action_items_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT ck_meeting_action_items_status CHECK (status IN (N'OPEN',N'IN_PROGRESS',N'DONE',N'CANCELLED')),
        CONSTRAINT fk_meeting_action_items_meeting FOREIGN KEY (meeting_id) REFERENCES dbo.meetings(id),
        CONSTRAINT fk_meeting_action_items_assignee FOREIGN KEY (assignee_user_id) REFERENCES dbo.users(id),
        CONSTRAINT fk_meeting_action_items_creator FOREIGN KEY (created_by) REFERENCES dbo.users(id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_meeting_decisions_meeting' AND object_id = OBJECT_ID(N'dbo.meeting_decisions'))
    CREATE INDEX ix_meeting_decisions_meeting ON dbo.meeting_decisions(meeting_id, decided_at DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_meeting_action_items_meeting' AND object_id = OBJECT_ID(N'dbo.meeting_action_items'))
    CREATE INDEX ix_meeting_action_items_meeting ON dbo.meeting_action_items(meeting_id, status, due_at);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_meeting_action_items_assignee' AND object_id = OBJECT_ID(N'dbo.meeting_action_items'))
    CREATE INDEX ix_meeting_action_items_assignee ON dbo.meeting_action_items(assignee_user_id, status);
COMMIT TRANSACTION;
GO

SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.team_eligibility_checks', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.team_eligibility_checks (
            id                   bigint IDENTITY NOT NULL CONSTRAINT pk_team_eligibility_checks PRIMARY KEY,
            team_id              bigint NOT NULL REFERENCES dbo.teams(id),
            project_period_id    bigint NOT NULL REFERENCES dbo.project_periods(id),
            project_id           bigint NULL REFERENCES dbo.projects(id),
            round_type           varchar(20) NOT NULL,
            revision_history_id  bigint NULL REFERENCES dbo.project_status_history(id),
            project_mode         varchar(30) NOT NULL,
            policy_version       nvarchar(100) NOT NULL,
            rule_version         varchar(50) NOT NULL,
            roster_hash          varchar(64) NOT NULL,
            academic_scope_hash  varchar(64) NOT NULL,
            project_context_hash varchar(64) NOT NULL,
            fingerprint          varchar(64) NOT NULL,
            temporal_state_hash  varchar(64) NOT NULL,
            evaluation_key       varchar(64) NOT NULL,
            result               varchar(10) NOT NULL,
            valid_until_at       datetime2(0) NULL,
            checked_by           bigint NOT NULL REFERENCES dbo.users(id),
            checked_at           datetime2(0) NOT NULL CONSTRAINT df_team_eligibility_checks_checked_at DEFAULT (SYSUTCDATETIME()),
            trigger_source       varchar(30) NOT NULL,
            CONSTRAINT ck_team_eligibility_checks_round_type CHECK (round_type IN ('FORMATION', 'INITIAL', 'REVISION')),
            CONSTRAINT ck_team_eligibility_checks_round_integrity CHECK (
                (round_type = 'FORMATION' AND project_id IS NULL AND revision_history_id IS NULL)
                OR (round_type = 'INITIAL' AND project_id IS NOT NULL AND revision_history_id IS NULL)
                OR (round_type = 'REVISION' AND project_id IS NOT NULL AND revision_history_id IS NOT NULL)
            ),
            CONSTRAINT ck_team_eligibility_checks_mode CHECK (project_mode IN ('SINGLE_MAJOR', 'INTERDISCIPLINARY')),
            CONSTRAINT ck_team_eligibility_checks_result CHECK (result IN ('PASS', 'FAIL')),
            CONSTRAINT ck_team_eligibility_checks_trigger CHECK (trigger_source IN ('MANUAL_CHECK', 'REFRESH_ALIAS'))
        );
        CREATE UNIQUE INDEX ux_team_eligibility_checks_team_evaluation_key ON dbo.team_eligibility_checks(team_id, evaluation_key);
        CREATE INDEX ix_team_eligibility_checks_round_lookup ON dbo.team_eligibility_checks(team_id, project_id, round_type, revision_history_id, id DESC);
        CREATE INDEX ix_team_eligibility_checks_team_fingerprint ON dbo.team_eligibility_checks(team_id, fingerprint, id DESC);
    END;

    IF OBJECT_ID(N'dbo.team_eligibility_issues', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.team_eligibility_issues (
            id                   bigint IDENTITY NOT NULL CONSTRAINT pk_team_eligibility_issues PRIMARY KEY,
            eligibility_check_id bigint NOT NULL REFERENCES dbo.team_eligibility_checks(id),
            sort_order           int NOT NULL,
            rule_code            varchar(50) NOT NULL,
            severity             varchar(10) NOT NULL CONSTRAINT df_team_eligibility_issues_severity DEFAULT ('ERROR'),
            major_id             bigint NULL REFERENCES dbo.majors(id),
            user_id              bigint NULL REFERENCES dbo.users(id),
            expected_value       nvarchar(255) NULL,
            actual_value         nvarchar(255) NULL,
            message              nvarchar(1000) NOT NULL,
            created_at           datetime2(0) NOT NULL CONSTRAINT df_team_eligibility_issues_created_at DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT ck_team_eligibility_issues_sort_order CHECK (sort_order >= 0),
            CONSTRAINT ck_team_eligibility_issues_severity CHECK (severity IN ('ERROR', 'WARNING'))
        );
        CREATE UNIQUE INDEX ux_team_eligibility_issues_check_sort ON dbo.team_eligibility_issues(eligibility_check_id, sort_order);
        CREATE INDEX ix_team_eligibility_issues_check ON dbo.team_eligibility_issues(eligibility_check_id);
    END;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

GO
-- Existing registration snapshot JSON remains immutable. New submissions capture
-- proposal and project requirement values (including IDs/tokens) in that envelope.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.project_major_requirements', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_major_requirements (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_major_requirements PRIMARY KEY,
        project_id BIGINT NOT NULL,
        major_id BIGINT NOT NULL,
        min_members INT NOT NULL,
        max_members INT NOT NULL,
        responsibility NVARCHAR(2000) NOT NULL,
        concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_project_major_requirements_token DEFAULT NEWID(),
        created_at DATETIME2(7) NOT NULL CONSTRAINT df_project_major_requirements_created DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(7) NOT NULL CONSTRAINT df_project_major_requirements_updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT fk_project_major_requirements_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_project_major_requirements_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id),
        CONSTRAINT uq_project_major_requirements UNIQUE(project_id, major_id),
        CONSTRAINT ck_project_major_requirements_bounds CHECK(min_members >= 1 AND max_members >= min_members),
        CONSTRAINT ck_project_major_requirements_responsibility CHECK(LEN(LTRIM(RTRIM(responsibility))) > 0)
    );
    CREATE INDEX ix_project_major_requirements_major ON dbo.project_major_requirements(major_id);
END;
-- No speculative backfill from team quotas or current proposals into old snapshots.
COMMIT;

GO
-- Additive migration. Run in the intended AI-PMS database; no historical approvals are inferred.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.team_academic_configurations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.team_academic_configurations (
        team_id bigint NOT NULL CONSTRAINT pk_team_academic_configurations PRIMARY KEY,
        project_mode varchar(30) NOT NULL,
        primary_major_id bigint NULL,
        lead_department_id bigint NOT NULL,
        concurrency_token uniqueidentifier NOT NULL,
        CONSTRAINT fk_team_academic_team FOREIGN KEY (team_id) REFERENCES dbo.teams(id),
        CONSTRAINT fk_team_academic_primary FOREIGN KEY (primary_major_id) REFERENCES dbo.majors(id),
        CONSTRAINT fk_team_academic_lead FOREIGN KEY (lead_department_id) REFERENCES dbo.departments(id),
        CONSTRAINT ck_team_academic_mode CHECK (
            (project_mode = 'SINGLE_MAJOR' AND primary_major_id IS NOT NULL)
            OR (project_mode = 'INTERDISCIPLINARY' AND primary_major_id IS NULL))
    );
END;
IF OBJECT_ID(N'dbo.team_major_requirements', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.team_major_requirements (
        team_id bigint NOT NULL,
        major_id bigint NOT NULL,
        min_members int NOT NULL,
        max_members int NOT NULL,
        responsibility nvarchar(1000) NOT NULL,
        CONSTRAINT pk_team_major_requirements PRIMARY KEY (team_id, major_id),
        CONSTRAINT fk_team_requirement_team FOREIGN KEY (team_id) REFERENCES dbo.team_academic_configurations(team_id),
        CONSTRAINT fk_team_requirement_major FOREIGN KEY (major_id) REFERENCES dbo.majors(id),
        CONSTRAINT ck_team_requirement_quota CHECK (min_members >= 1 AND max_members >= min_members),
        CONSTRAINT ck_team_requirement_responsibility CHECK (LEN(LTRIM(RTRIM(responsibility))) > 0)
    );
END;
IF OBJECT_ID(N'dbo.project_registration_snapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_registration_snapshots (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_registration_snapshots PRIMARY KEY,
        project_id bigint NOT NULL,
        project_period_id bigint NOT NULL,
        lead_department_id bigint NOT NULL,
        submitted_by bigint NOT NULL,
        submitted_at datetime2(0) NOT NULL,
        snapshot_json nvarchar(max) NOT NULL,
        CONSTRAINT fk_project_registration_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_project_registration_period FOREIGN KEY (project_period_id) REFERENCES dbo.project_periods(id),
        CONSTRAINT fk_project_registration_lead FOREIGN KEY (lead_department_id) REFERENCES dbo.departments(id),
        CONSTRAINT fk_project_registration_submitter FOREIGN KEY (submitted_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_project_registration_json CHECK (ISJSON(snapshot_json) = 1)
    );
    CREATE INDEX ix_project_registration_latest ON dbo.project_registration_snapshots(project_id, id);
END;
IF OBJECT_ID(N'dbo.project_department_decisions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_department_decisions (
        snapshot_id bigint NOT NULL,
        department_id bigint NOT NULL,
        decision varchar(20) NOT NULL,
        decided_by bigint NULL,
        decided_at datetime2(0) NULL,
        reason nvarchar(2000) NULL,
        CONSTRAINT pk_project_department_decisions PRIMARY KEY (snapshot_id, department_id),
        CONSTRAINT fk_project_decision_snapshot FOREIGN KEY (snapshot_id) REFERENCES dbo.project_registration_snapshots(id),
        CONSTRAINT fk_project_decision_department FOREIGN KEY (department_id) REFERENCES dbo.departments(id),
        CONSTRAINT fk_project_decision_actor FOREIGN KEY (decided_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_project_decision_state CHECK (
            (decision = 'PENDING' AND decided_by IS NULL AND decided_at IS NULL)
            OR (decision IN ('APPROVED','REJECTED') AND decided_by IS NOT NULL AND decided_at IS NOT NULL)),
        CONSTRAINT ck_project_decision_reason CHECK (decision <> 'REJECTED' OR (reason IS NOT NULL AND LEN(LTRIM(RTRIM(reason))) > 0))
    );
END;
COMMIT TRANSACTION;

GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.team_academic_configurations', 'responsibility_version') IS NULL
    ALTER TABLE dbo.team_academic_configurations ADD responsibility_version UNIQUEIDENTIFIER NULL;
IF OBJECT_ID('dbo.team_major_responsibilities', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.team_major_responsibilities (
        id BIGINT IDENTITY PRIMARY KEY,
        team_id BIGINT NOT NULL, major_id BIGINT NOT NULL,
        content NVARCHAR(2000) NOT NULL, sort_order INT NOT NULL,
        concurrency_token UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        created_by BIGINT NOT NULL, created_at DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT fk_responsibility_requirement FOREIGN KEY(team_id,major_id) REFERENCES dbo.team_major_requirements(team_id,major_id) ON DELETE CASCADE,
        CONSTRAINT fk_responsibility_actor FOREIGN KEY(created_by) REFERENCES dbo.users(id),
        CONSTRAINT uq_responsibility_order UNIQUE(team_id,major_id,sort_order),
        CONSTRAINT ck_responsibility_content CHECK(LEN(LTRIM(RTRIM(content)))>0 AND sort_order>=0)
    );
END;
IF OBJECT_ID('dbo.task_disciplines', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.task_disciplines (
        task_id BIGINT NOT NULL, major_id BIGINT NOT NULL,
        role VARCHAR(20) NOT NULL,
        created_by BIGINT NOT NULL, created_at DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT pk_task_disciplines PRIMARY KEY(task_id,major_id),
        CONSTRAINT fk_task_discipline_task FOREIGN KEY(task_id) REFERENCES dbo.tasks(id) ON DELETE CASCADE,
        CONSTRAINT fk_task_discipline_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id),
        CONSTRAINT fk_task_discipline_actor FOREIGN KEY(created_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_task_discipline_role CHECK(role IN ('PRIMARY','SUPPORTING'))
    );
    CREATE UNIQUE INDEX uq_task_discipline_primary ON dbo.task_disciplines(task_id) WHERE role='PRIMARY';
    CREATE INDEX ix_task_discipline_major ON dbo.task_disciplines(major_id,task_id);
END;
IF OBJECT_ID('dbo.project_evidence', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_evidence (
        id BIGINT IDENTITY PRIMARY KEY,
        project_id BIGINT NOT NULL, major_id BIGINT NULL,
        source_type VARCHAR(30) NOT NULL, source_id BIGINT NOT NULL,
        task_id BIGINT NULL, deliverable_id BIGINT NULL, meeting_id BIGINT NULL, progress_report_id BIGINT NULL, file_id BIGINT NULL,
        notes NVARCHAR(2000) NULL,
        verification_status VARCHAR(20) NOT NULL DEFAULT 'PENDING',
        submitted_by BIGINT NOT NULL, submitted_at DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT fk_evidence_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_evidence_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id),
        CONSTRAINT fk_evidence_actor FOREIGN KEY(submitted_by) REFERENCES dbo.users(id),
        CONSTRAINT fk_evidence_task FOREIGN KEY(task_id) REFERENCES dbo.tasks(id),
        CONSTRAINT fk_evidence_deliverable FOREIGN KEY(deliverable_id) REFERENCES dbo.deliverables(id),
        CONSTRAINT fk_evidence_meeting FOREIGN KEY(meeting_id) REFERENCES dbo.meetings(id),
        CONSTRAINT fk_evidence_report FOREIGN KEY(progress_report_id) REFERENCES dbo.progress_reports(id),
        CONSTRAINT fk_evidence_file FOREIGN KEY(file_id) REFERENCES dbo.files(id),
        CONSTRAINT uq_evidence_source UNIQUE(project_id,source_type,source_id,major_id),
        CONSTRAINT ck_evidence_status CHECK(verification_status IN ('PENDING','UNKNOWN')),
        CONSTRAINT ck_evidence_source CHECK(
            (CASE WHEN task_id IS NULL THEN 0 ELSE 1 END + CASE WHEN deliverable_id IS NULL THEN 0 ELSE 1 END +
             CASE WHEN meeting_id IS NULL THEN 0 ELSE 1 END + CASE WHEN progress_report_id IS NULL THEN 0 ELSE 1 END +
             CASE WHEN file_id IS NULL THEN 0 ELSE 1 END)=1 AND
            ((source_type='TASK' AND task_id IS NOT NULL AND source_id=task_id) OR
             (source_type='DELIVERABLE' AND deliverable_id IS NOT NULL AND source_id=deliverable_id) OR
             (source_type='MEETING' AND meeting_id IS NOT NULL AND source_id=meeting_id) OR
             (source_type='PROGRESS_REPORT' AND progress_report_id IS NOT NULL AND source_id=progress_report_id) OR
             (source_type='FILE' AND file_id IS NOT NULL AND source_id=file_id)))
    );
    CREATE INDEX ix_evidence_project_time ON dbo.project_evidence(project_id,submitted_at DESC,id DESC);
    CREATE INDEX ix_evidence_project_major ON dbo.project_evidence(project_id,major_id,verification_status);
END;
-- Legacy tasks/files are not assigned guessed majors or verification outcomes.
COMMIT;

GO
-- Apply after db/schema.sql on a new database, or to an existing AI-PMS database.
-- Review and apply before deploying the Rubrics API; does not switch databases.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.rubric_versions', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.rubric_versions (
            rubric_id BIGINT NOT NULL,
            root_rubric_id BIGINT NOT NULL,
            version_number INT NOT NULL,
            status VARCHAR(20) NOT NULL,
            concurrency_token UNIQUEIDENTIFIER NOT NULL,
            CONSTRAINT pk_rubric_versions PRIMARY KEY (rubric_id),
            CONSTRAINT fk_rubric_versions_rubric FOREIGN KEY (rubric_id) REFERENCES dbo.rubrics(id),
            CONSTRAINT fk_rubric_versions_root FOREIGN KEY (root_rubric_id) REFERENCES dbo.rubrics(id),
            CONSTRAINT uq_rubric_versions_family_number UNIQUE (root_rubric_id, version_number),
            CONSTRAINT ck_rubric_versions_status CHECK (status IN ('DRAFT','PUBLISHED','RETIRED')),
            CONSTRAINT ck_rubric_versions_number CHECK (version_number > 0)
        );
    END;

    -- No reliable publication history exists for legacy rows. Protect all of them;
    -- never infer an editable draft from is_active = 0 or rename/relink evaluations.
    INSERT INTO dbo.rubric_versions (rubric_id, root_rubric_id, version_number, status, concurrency_token)
    SELECT r.id, r.id, 1, CASE WHEN r.is_active = 1 THEN 'PUBLISHED' ELSE 'RETIRED' END, NEWID()
    FROM dbo.rubrics r WITH (UPDLOCK, HOLDLOCK)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.rubric_versions v WITH (UPDLOCK, HOLDLOCK) WHERE v.rubric_id = r.id);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

GO
-- Prerequisite: schema.sql and 20260911_add_rubric_versions.sql.
-- Additive draft-evaluation foundation. Does not infer assignments for legacy evaluations.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.rubric_versions', N'U') IS NULL
        THROW 51000, 'Apply the rubric version migration first.', 1;

    IF OBJECT_ID(N'dbo.evaluation_assignments', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.evaluation_assignments (
            id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_evaluation_assignments PRIMARY KEY,
            project_id BIGINT NOT NULL,
            evaluator_id BIGINT NOT NULL,
            rubric_id BIGINT NOT NULL,
            project_period_id BIGINT NOT NULL,
            department_id BIGINT NOT NULL,
            evaluation_type NVARCHAR(30) NOT NULL,
            status NVARCHAR(20) NOT NULL,
            assigned_by BIGINT NOT NULL,
            assigned_at DATETIME2(0) NOT NULL,
            revoked_at DATETIME2(0) NULL,
            revocation_reason NVARCHAR(1000) NULL,
            concurrency_token UNIQUEIDENTIFIER NOT NULL,
            CONSTRAINT fk_evaluation_assignments_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
            CONSTRAINT fk_evaluation_assignments_evaluator FOREIGN KEY (evaluator_id) REFERENCES dbo.users(id),
            CONSTRAINT fk_evaluation_assignments_rubric FOREIGN KEY (rubric_id) REFERENCES dbo.rubrics(id),
            CONSTRAINT fk_evaluation_assignments_period FOREIGN KEY (project_period_id) REFERENCES dbo.project_periods(id),
            CONSTRAINT fk_evaluation_assignments_department FOREIGN KEY (department_id) REFERENCES dbo.departments(id),
            CONSTRAINT fk_evaluation_assignments_actor FOREIGN KEY (assigned_by) REFERENCES dbo.users(id),
            CONSTRAINT ck_evaluation_assignments_type CHECK (evaluation_type IN (N'SUPERVISOR',N'LECTURER')),
            CONSTRAINT ck_evaluation_assignments_state CHECK (
                (status = N'ACTIVE' AND revoked_at IS NULL AND revocation_reason IS NULL)
                OR (status = N'REVOKED' AND revoked_at >= assigned_at AND LEN(LTRIM(RTRIM(revocation_reason))) > 0 AND revoked_at IS NOT NULL AND revocation_reason IS NOT NULL))
        );
        CREATE UNIQUE INDEX uq_evaluation_assignments_active ON dbo.evaluation_assignments(project_id,evaluator_id,evaluation_type) WHERE status = N'ACTIVE';
        CREATE INDEX ix_evaluation_assignments_evaluator ON dbo.evaluation_assignments(evaluator_id,status,id);
    END;

    IF OBJECT_ID(N'dbo.evaluation_draft_states', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.evaluation_draft_states (
            evaluation_id BIGINT NOT NULL CONSTRAINT pk_evaluation_draft_states PRIMARY KEY,
            assignment_id BIGINT NOT NULL CONSTRAINT uq_evaluation_draft_states_assignment UNIQUE,
            concurrency_token UNIQUEIDENTIFIER NOT NULL,
            calculation_rule VARCHAR(50) NOT NULL,
            CONSTRAINT fk_evaluation_draft_states_evaluation FOREIGN KEY (evaluation_id) REFERENCES dbo.evaluations(id),
            CONSTRAINT fk_evaluation_draft_states_assignment FOREIGN KEY (assignment_id) REFERENCES dbo.evaluation_assignments(id),
            CONSTRAINT ck_evaluation_draft_states_rule CHECK (calculation_rule = 'WEIGHTED_10_AWAY_FROM_ZERO_2DP_V1')
        );
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

GO
-- BE-16 slice 1: draft selections only, not official locked submissions.
-- Prerequisite: canonical schema.sql. Additive and rerunnable; no legacy backfill.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.projects', N'U') IS NULL OR OBJECT_ID(N'dbo.deliverable_versions', N'U') IS NULL
        THROW 51000, 'Apply the canonical schema before final-submission drafts.', 1;

    IF OBJECT_ID(N'dbo.final_submission_drafts', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.final_submission_drafts (
            id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_final_submission_drafts PRIMARY KEY,
            project_id BIGINT NOT NULL CONSTRAINT uq_final_submission_drafts_project UNIQUE,
            project_period_id BIGINT NOT NULL,
            notes NVARCHAR(MAX) NULL,
            created_by BIGINT NOT NULL,
            updated_by BIGINT NOT NULL,
            created_at DATETIME2(0) NOT NULL,
            updated_at DATETIME2(0) NOT NULL,
            concurrency_token UNIQUEIDENTIFIER NOT NULL,
            CONSTRAINT fk_final_submission_drafts_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
            CONSTRAINT fk_final_submission_drafts_period FOREIGN KEY (project_period_id) REFERENCES dbo.project_periods(id),
            CONSTRAINT fk_final_submission_drafts_creator FOREIGN KEY (created_by) REFERENCES dbo.users(id),
            CONSTRAINT fk_final_submission_drafts_editor FOREIGN KEY (updated_by) REFERENCES dbo.users(id),
            CONSTRAINT ck_final_submission_drafts_notes CHECK (notes IS NULL OR DATALENGTH(notes) <= 20000)
        );
    END;
    IF OBJECT_ID(N'dbo.final_submission_draft_items', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.final_submission_draft_items (
            draft_id BIGINT NOT NULL,
            deliverable_version_id BIGINT NOT NULL,
            CONSTRAINT pk_final_submission_draft_items PRIMARY KEY (draft_id, deliverable_version_id),
            CONSTRAINT fk_final_submission_draft_items_draft FOREIGN KEY (draft_id) REFERENCES dbo.final_submission_drafts(id),
            CONSTRAINT fk_final_submission_draft_items_version FOREIGN KEY (deliverable_version_id) REFERENCES dbo.deliverable_versions(id)
        );
        CREATE INDEX ix_final_submission_draft_items_version ON dbo.final_submission_draft_items(deliverable_version_id);
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

GO
-- BE-16 slice 2. Additive, rerunnable; no backfill of legacy FINAL_SUBMISSION projects.
-- Apply before deploying the new API. Does not publish grades or archive projects.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.final_submission_drafts', N'U') IS NULL
        THROW 51000, 'Apply final-submission drafts first.', 1;
    IF OBJECT_ID(N'dbo.final_submission_requirements', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.final_submission_requirements (
            project_id BIGINT NOT NULL CONSTRAINT pk_final_submission_requirements PRIMARY KEY,
            concurrency_token UNIQUEIDENTIFIER NOT NULL,
            updated_by BIGINT NOT NULL,
            updated_at DATETIME2(0) NOT NULL,
            CONSTRAINT fk_final_requirements_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
            CONSTRAINT fk_final_requirements_editor FOREIGN KEY (updated_by) REFERENCES dbo.users(id)
        );
    END;
    IF OBJECT_ID(N'dbo.final_submission_requirement_items', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.final_submission_requirement_items (
            project_id BIGINT NOT NULL,
            deliverable_id BIGINT NOT NULL,
            CONSTRAINT pk_final_submission_requirement_items PRIMARY KEY (project_id, deliverable_id),
            CONSTRAINT fk_final_requirement_items_policy FOREIGN KEY (project_id) REFERENCES dbo.final_submission_requirements(project_id),
            CONSTRAINT fk_final_requirement_items_deliverable FOREIGN KEY (deliverable_id) REFERENCES dbo.deliverables(id)
        );
        CREATE INDEX ix_final_requirement_items_deliverable ON dbo.final_submission_requirement_items(deliverable_id);
    END;
    IF OBJECT_ID(N'dbo.final_submissions', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.final_submissions (
            id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_final_submissions PRIMARY KEY,
            project_id BIGINT NOT NULL CONSTRAINT uq_final_submissions_project UNIQUE,
            project_period_id BIGINT NOT NULL,
            submitted_by BIGINT NOT NULL,
            submitted_at DATETIME2(0) NOT NULL,
            deadline DATETIME2(0) NOT NULL,
            notes NVARCHAR(MAX) NULL,
            draft_concurrency_token UNIQUEIDENTIFIER NOT NULL,
            requirements_concurrency_token UNIQUEIDENTIFIER NOT NULL,
            CONSTRAINT fk_final_submissions_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
            CONSTRAINT fk_final_submissions_period FOREIGN KEY (project_period_id) REFERENCES dbo.project_periods(id),
            CONSTRAINT fk_final_submissions_submitter FOREIGN KEY (submitted_by) REFERENCES dbo.users(id),
            CONSTRAINT ck_final_submissions_notes CHECK (notes IS NULL OR DATALENGTH(notes) <= 20000),
            CONSTRAINT ck_final_submissions_deadline CHECK (submitted_at < deadline)
        );
    END;
    IF OBJECT_ID(N'dbo.final_submission_items', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.final_submission_items (
            submission_id BIGINT NOT NULL,
            deliverable_version_id BIGINT NOT NULL,
            deliverable_id BIGINT NOT NULL,
            title NVARCHAR(255) NOT NULL,
            version_number INT NOT NULL,
            status_at_submission VARCHAR(20) NOT NULL,
            was_required BIT NOT NULL,
            files_json NVARCHAR(MAX) NOT NULL,
            CONSTRAINT pk_final_submission_items PRIMARY KEY (submission_id, deliverable_version_id),
            CONSTRAINT uq_final_submission_items_deliverable UNIQUE (submission_id, deliverable_id),
            CONSTRAINT fk_final_submission_items_submission FOREIGN KEY (submission_id) REFERENCES dbo.final_submissions(id),
            CONSTRAINT fk_final_submission_items_version FOREIGN KEY (deliverable_version_id) REFERENCES dbo.deliverable_versions(id),
            CONSTRAINT fk_final_submission_items_deliverable FOREIGN KEY (deliverable_id) REFERENCES dbo.deliverables(id),
            CONSTRAINT ck_final_submission_items_version CHECK (version_number > 0),
            CONSTRAINT ck_final_submission_items_status CHECK (status_at_submission IN ('SUBMITTED','ACCEPTED')),
            CONSTRAINT ck_final_submission_items_files CHECK (ISJSON(files_json) = 1 AND files_json <> N'[]')
        );
        CREATE INDEX ix_final_submission_items_version ON dbo.final_submission_items(deliverable_version_id);
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

GO
-- BE-09: immutable evaluation evidence/score snapshot. Apply after BE-16 locked packages.
-- Additive and rerunnable; does not finalize or backfill historical grades.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.evaluation_draft_states', N'U') IS NULL
        OR OBJECT_ID(N'dbo.final_submissions', N'U') IS NULL
        THROW 51000, 'Apply evaluation drafts and locked final submissions first.', 1;
    IF OBJECT_ID(N'dbo.evaluation_finalizations', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.evaluation_finalizations (
            evaluation_id BIGINT NOT NULL CONSTRAINT pk_evaluation_finalizations PRIMARY KEY,
            final_submission_id BIGINT NOT NULL,
            finalized_by BIGINT NOT NULL,
            finalized_at DATETIME2(0) NOT NULL,
            snapshot_json NVARCHAR(MAX) NOT NULL,
            CONSTRAINT fk_evaluation_finalizations_evaluation FOREIGN KEY (evaluation_id) REFERENCES dbo.evaluations(id),
            CONSTRAINT fk_evaluation_finalizations_submission FOREIGN KEY (final_submission_id) REFERENCES dbo.final_submissions(id),
            CONSTRAINT fk_evaluation_finalizations_actor FOREIGN KEY (finalized_by) REFERENCES dbo.users(id),
            CONSTRAINT ck_evaluation_finalizations_snapshot CHECK (ISJSON(snapshot_json) = 1)
        );
        CREATE INDEX ix_evaluation_finalizations_submission ON dbo.evaluation_finalizations(final_submission_id);
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

GO
-- BE-16 result policy and publication; additive/rerunnable, no historical backfill.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.evaluation_finalizations', N'U') IS NULL
        THROW 51000, 'Apply evaluation finalizations first.', 1;
    IF OBJECT_ID(N'dbo.project_result_policies', N'U') IS NULL
        CREATE TABLE dbo.project_result_policies (
            project_id BIGINT NOT NULL CONSTRAINT pk_project_result_policies PRIMARY KEY,
            pass_threshold DECIMAL(4,2) NOT NULL,
            concurrency_token UNIQUEIDENTIFIER NOT NULL,
            updated_by BIGINT NOT NULL,
            updated_at DATETIME2(0) NOT NULL,
            CONSTRAINT fk_result_policy_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
            CONSTRAINT fk_result_policy_actor FOREIGN KEY (updated_by) REFERENCES dbo.users(id),
            CONSTRAINT ck_result_policy_threshold CHECK (pass_threshold BETWEEN 0 AND 10)
        );
    IF OBJECT_ID(N'dbo.project_result_policy_items', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.project_result_policy_items (
            project_id BIGINT NOT NULL, assignment_id BIGINT NOT NULL, weight_percent DECIMAL(5,2) NOT NULL,
            CONSTRAINT pk_project_result_policy_items PRIMARY KEY (project_id, assignment_id),
            CONSTRAINT fk_result_policy_items_policy FOREIGN KEY (project_id) REFERENCES dbo.project_result_policies(project_id),
            CONSTRAINT fk_result_policy_items_assignment FOREIGN KEY (assignment_id) REFERENCES dbo.evaluation_assignments(id),
            CONSTRAINT ck_result_policy_items_weight CHECK (weight_percent > 0 AND weight_percent <= 100)
        );
        CREATE INDEX ix_result_policy_items_assignment ON dbo.project_result_policy_items(assignment_id);
    END;
    IF OBJECT_ID(N'dbo.project_results', N'U') IS NULL
        CREATE TABLE dbo.project_results (
            id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_results PRIMARY KEY,
            project_id BIGINT NOT NULL CONSTRAINT uq_project_results_project UNIQUE,
            final_submission_id BIGINT NOT NULL, published_by BIGINT NOT NULL, published_at DATETIME2(0) NOT NULL,
            snapshot_json NVARCHAR(MAX) NOT NULL,
            CONSTRAINT fk_project_results_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
            CONSTRAINT fk_project_results_submission FOREIGN KEY (final_submission_id) REFERENCES dbo.final_submissions(id),
            CONSTRAINT fk_project_results_actor FOREIGN KEY (published_by) REFERENCES dbo.users(id),
            CONSTRAINT ck_project_results_snapshot CHECK (ISJSON(snapshot_json) = 1)
        );
    IF OBJECT_ID(N'dbo.project_result_evaluations', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.project_result_evaluations (
            result_id BIGINT NOT NULL, evaluation_id BIGINT NOT NULL,
            CONSTRAINT pk_project_result_evaluations PRIMARY KEY (result_id, evaluation_id),
            CONSTRAINT fk_result_evaluations_result FOREIGN KEY (result_id) REFERENCES dbo.project_results(id),
            CONSTRAINT fk_result_evaluations_finalization FOREIGN KEY (evaluation_id) REFERENCES dbo.evaluation_finalizations(evaluation_id)
        );
        CREATE INDEX ix_project_result_evaluations_evaluation ON dbo.project_result_evaluations(evaluation_id);
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

GO
/* Baseline PR4. Additive and rerunnable. Historical scope is never inferred. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.period_policy_versions',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.period_policy_versions (
  id BIGINT IDENTITY PRIMARY KEY, project_period_id BIGINT NOT NULL REFERENCES dbo.project_periods(id),
  version INT NOT NULL, status NVARCHAR(20) NOT NULL CHECK(status IN ('DRAFT','PUBLISHED','LOCKED')),
  effective_from DATETIME2(7) NOT NULL, effective_to DATETIME2(7) NOT NULL,
  snapshot_json NVARCHAR(MAX) NOT NULL CHECK(ISJSON(snapshot_json)=1), concurrency_token UNIQUEIDENTIFIER NOT NULL,
  created_by BIGINT NULL REFERENCES dbo.users(id), created_at DATETIME2(7) NOT NULL,
  CONSTRAINT uq_period_policy_version UNIQUE(project_period_id,version), CHECK(effective_from < effective_to)
 );
 CREATE TABLE dbo.period_policy_usages (
  id BIGINT IDENTITY PRIMARY KEY, policy_version_id BIGINT NOT NULL REFERENCES dbo.period_policy_versions(id),
  entity_type NVARCHAR(40) NOT NULL, entity_id BIGINT NOT NULL, created_at DATETIME2(7) NOT NULL,
  CONSTRAINT uq_period_policy_usage UNIQUE(entity_type,entity_id)
 );
 CREATE INDEX ix_policy_usage_version ON dbo.period_policy_usages(policy_version_id);
END;
IF OBJECT_ID(N'dbo.evaluation_schemes',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.evaluation_schemes (
  id BIGINT IDENTITY PRIMARY KEY, root_id BIGINT NULL REFERENCES dbo.evaluation_schemes(id), version INT NOT NULL,
  project_id BIGINT NOT NULL REFERENCES dbo.projects(id), project_period_id BIGINT NOT NULL REFERENCES dbo.project_periods(id),
  name NVARCHAR(200) NOT NULL, status NVARCHAR(20) NOT NULL CHECK(status IN ('DRAFT','PUBLISHED','RETIRED')),
  pass_threshold DECIMAL(5,2) NOT NULL CHECK(pass_threshold BETWEEN 0 AND 10), concurrency_token UNIQUEIDENTIFIER NOT NULL,
  policy_version_id BIGINT NULL REFERENCES dbo.period_policy_versions(id), students_json NVARCHAR(MAX) NOT NULL,
  registration_snapshot_json NVARCHAR(MAX) NOT NULL,
  calculation_rule NVARCHAR(100) NOT NULL,
  created_by BIGINT NOT NULL REFERENCES dbo.users(id), created_at DATETIME2(7) NOT NULL,
  published_by BIGINT NULL REFERENCES dbo.users(id), published_at DATETIME2(7) NULL,
  CONSTRAINT uq_evaluation_scheme_version UNIQUE(project_id,version)
 );
 CREATE UNIQUE INDEX uq_evaluation_scheme_published ON dbo.evaluation_schemes(project_id) WHERE status='PUBLISHED';
 CREATE TABLE dbo.evaluation_scheme_components (
  id BIGINT IDENTITY PRIMARY KEY, scheme_id BIGINT NOT NULL REFERENCES dbo.evaluation_schemes(id) ON DELETE CASCADE,
  name NVARCHAR(200) NOT NULL, scope NVARCHAR(20) NOT NULL CHECK(scope IN ('COMMON','MAJOR_SPECIFIC','INDIVIDUAL')),
  major_id BIGINT NULL REFERENCES dbo.majors(id), rubric_id BIGINT NOT NULL REFERENCES dbo.rubrics(id),
  project_weight_percent DECIMAL(9,4) NOT NULL CHECK(project_weight_percent BETWEEN 0 AND 100),
  student_weight_percent DECIMAL(9,4) NOT NULL CHECK(student_weight_percent BETWEEN 0 AND 100),
  required_evaluators INT NOT NULL CHECK(required_evaluators BETWEEN 1 AND 20),
  CHECK((scope='COMMON' AND major_id IS NULL) OR (scope IN ('MAJOR_SPECIFIC','INDIVIDUAL') AND major_id IS NOT NULL)),
  CHECK(scope<>'INDIVIDUAL' OR project_weight_percent=0)
 );
 CREATE TABLE dbo.student_results (
  id BIGINT IDENTITY PRIMARY KEY, project_id BIGINT NOT NULL REFERENCES dbo.projects(id),
  student_id BIGINT NOT NULL REFERENCES dbo.users(id), major_id BIGINT NOT NULL REFERENCES dbo.majors(id),
  scheme_id BIGINT NOT NULL REFERENCES dbo.evaluation_schemes(id), total_score DECIMAL(5,2) NOT NULL CHECK(total_score BETWEEN 0 AND 10),
  pass_threshold DECIMAL(5,2) NOT NULL, outcome NVARCHAR(20) NOT NULL CHECK(outcome IN ('PASSED','FAILED')),
  calculation_rule NVARCHAR(100) NOT NULL, published_by BIGINT NOT NULL REFERENCES dbo.users(id),
  published_at DATETIME2(7) NOT NULL, snapshot_json NVARCHAR(MAX) NOT NULL CHECK(ISJSON(snapshot_json)=1),
  CONSTRAINT uq_student_result UNIQUE(project_id,student_id)
 );
 CREATE TABLE dbo.student_result_evaluations (
  result_id BIGINT NOT NULL REFERENCES dbo.student_results(id), evaluation_id BIGINT NOT NULL REFERENCES dbo.evaluations(id),
  PRIMARY KEY(result_id,evaluation_id)
 );
END;
IF COL_LENGTH('dbo.evaluation_assignments','scope') IS NULL
BEGIN
 ALTER TABLE dbo.evaluation_assignments ADD scope NVARCHAR(20) NOT NULL CONSTRAINT df_evaluation_scope DEFAULT('UNKNOWN'),
  major_id BIGINT NULL REFERENCES dbo.majors(id), student_id BIGINT NULL REFERENCES dbo.users(id),
  component_id BIGINT NULL REFERENCES dbo.evaluation_scheme_components(id),
  policy_version_id BIGINT NULL REFERENCES dbo.period_policy_versions(id), scope_snapshot_json NVARCHAR(MAX) NULL;
END;
EXEC(N'IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(''dbo.evaluation_assignments'') AND name=''uq_evaluation_assignments_active'')
 DROP INDEX uq_evaluation_assignments_active ON dbo.evaluation_assignments;
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(''dbo.evaluation_assignments'') AND name=''uq_scoped_evaluation_assignment'')
 CREATE UNIQUE INDEX uq_scoped_evaluation_assignment ON dbo.evaluation_assignments(project_id,component_id,student_id,evaluator_id) WHERE status=''ACTIVE'' AND component_id IS NOT NULL;
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(''dbo.evaluation_assignments'') AND name=''uq_legacy_evaluation_assignment'')
 CREATE UNIQUE INDEX uq_legacy_evaluation_assignment ON dbo.evaluation_assignments(project_id,evaluator_id,evaluation_type) WHERE status=''ACTIVE'' AND component_id IS NULL;');
IF OBJECT_ID('dbo.ck_evaluation_assignment_scope','C') IS NULL
 EXEC(N'ALTER TABLE dbo.evaluation_assignments ADD CONSTRAINT ck_evaluation_assignment_scope CHECK(
 (scope=''UNKNOWN'' AND component_id IS NULL AND major_id IS NULL AND student_id IS NULL) OR
 (component_id IS NOT NULL AND policy_version_id IS NOT NULL AND scope_snapshot_json IS NOT NULL AND (
 (scope=''COMMON'' AND major_id IS NULL AND student_id IS NULL) OR
 (scope=''MAJOR_SPECIFIC'' AND major_id IS NOT NULL AND student_id IS NULL) OR
 (scope=''INDIVIDUAL'' AND major_id IS NOT NULL AND student_id IS NOT NULL))));');
IF COL_LENGTH('dbo.evaluation_schemes','calculation_rule') IS NULL
    ALTER TABLE dbo.evaluation_schemes ADD calculation_rule NVARCHAR(100) NOT NULL
        CONSTRAINT df_evaluation_scheme_calculation_rule DEFAULT('COMPONENT_EQUAL_EVALUATOR_MEAN_WEIGHTED_10_AWAY_2DP_V1');
COMMIT;

GO
-- BE-03A: Reporting Cycles (progress_report_periods)
CREATE TABLE dbo.progress_report_periods (
    id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_progress_report_periods PRIMARY KEY,
    project_id BIGINT NOT NULL CONSTRAINT fk_progress_report_periods_project REFERENCES dbo.projects(id),
    project_period_id BIGINT NOT NULL CONSTRAINT fk_progress_report_periods_period REFERENCES dbo.project_periods(id),
    report_type NVARCHAR(20) NOT NULL CONSTRAINT ck_progress_report_periods_type CHECK (report_type IN (N'WEEKLY', N'MONTHLY')),
    period_start DATETIME2(0) NOT NULL,
    period_end DATETIME2(0) NOT NULL,
    deadline DATETIME2(0) NOT NULL,
    late_policy NVARCHAR(20) NOT NULL CONSTRAINT df_progress_report_periods_late_policy DEFAULT (N'BLOCK') CONSTRAINT ck_progress_report_periods_policy CHECK (late_policy IN (N'BLOCK', N'FLAG')),
    concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_progress_report_periods_token DEFAULT (NEWSEQUENTIALID()),
    created_by BIGINT NOT NULL CONSTRAINT fk_progress_report_periods_creator REFERENCES dbo.users(id),
    created_at DATETIME2(0) NOT NULL CONSTRAINT df_progress_report_periods_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at DATETIME2(0) NOT NULL CONSTRAINT df_progress_report_periods_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT ck_progress_report_periods_range CHECK (period_end > period_start)
);
GO
CREATE INDEX ix_progress_report_periods_lookup ON dbo.progress_report_periods(project_id, report_type, period_start, period_end);
GO

-- BE-03A: New columns on progress_reports
ALTER TABLE dbo.progress_reports ADD progress_report_period_id BIGINT NULL CONSTRAINT fk_progress_reports_period_id REFERENCES dbo.progress_report_periods(id);
ALTER TABLE dbo.progress_reports ADD is_late BIT NULL;
ALTER TABLE dbo.progress_reports ADD in_progress_work NVARCHAR(MAX) NULL;
ALTER TABLE dbo.progress_reports ADD blockers NVARCHAR(MAX) NULL;
ALTER TABLE dbo.progress_reports ADD risks NVARCHAR(MAX) NULL;
ALTER TABLE dbo.progress_reports ADD next_actions NVARCHAR(MAX) NULL;
GO
CREATE UNIQUE INDEX uq_progress_reports_period_id ON dbo.progress_reports(progress_report_period_id) WHERE progress_report_period_id IS NOT NULL;
GO

-- BE-03A: New columns on meetings
ALTER TABLE dbo.meetings ADD minutes NVARCHAR(MAX) NULL;
ALTER TABLE dbo.meetings ADD decisions NVARCHAR(MAX) NULL;
ALTER TABLE dbo.meetings ADD blockers NVARCHAR(MAX) NULL;
GO

-- BE-03A: Generic Project Action Items
CREATE TABLE dbo.project_action_items (
    id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_action_items PRIMARY KEY,
    project_id BIGINT NOT NULL CONSTRAINT fk_project_action_items_project REFERENCES dbo.projects(id),
    source_type NVARCHAR(30) NOT NULL,
    meeting_id BIGINT NULL CONSTRAINT fk_project_action_items_meeting REFERENCES dbo.meetings(id),
    progress_report_id BIGINT NULL CONSTRAINT fk_project_action_items_report REFERENCES dbo.progress_reports(id),
    title NVARCHAR(500) NOT NULL,
    description NVARCHAR(MAX) NULL,
    owner_id BIGINT NULL CONSTRAINT fk_project_action_items_owner REFERENCES dbo.users(id),
    task_id BIGINT NULL CONSTRAINT fk_project_action_items_task REFERENCES dbo.tasks(id),
    milestone_id BIGINT NULL CONSTRAINT fk_project_action_items_milestone REFERENCES dbo.milestones(id),
    due_at DATETIME2(0) NULL,
    status NVARCHAR(20) NOT NULL CONSTRAINT df_project_action_items_status DEFAULT (N'TODO'),
    concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_project_action_items_token DEFAULT (NEWSEQUENTIALID()),
    created_by BIGINT NOT NULL CONSTRAINT fk_project_action_items_creator REFERENCES dbo.users(id),
    created_at DATETIME2(0) NOT NULL CONSTRAINT df_project_action_items_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at DATETIME2(0) NOT NULL CONSTRAINT df_project_action_items_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT ck_project_action_items_source CHECK (
        (source_type = N'MEETING' AND meeting_id IS NOT NULL AND progress_report_id IS NULL) OR
        (source_type = N'PROGRESS_REPORT' AND progress_report_id IS NOT NULL AND meeting_id IS NULL)
    ),
    CONSTRAINT ck_project_action_items_status CHECK (status IN (N'TODO', N'IN_PROGRESS', N'BLOCKED', N'DONE', N'CANCELLED'))
);
GO
CREATE INDEX ix_project_action_items_project ON dbo.project_action_items(project_id, status, due_at);
CREATE INDEX ix_project_action_items_owner ON dbo.project_action_items(owner_id, status);
CREATE INDEX ix_project_action_items_meeting ON dbo.project_action_items(meeting_id) WHERE meeting_id IS NOT NULL;
CREATE INDEX ix_project_action_items_report ON dbo.project_action_items(progress_report_id) WHERE progress_report_id IS NOT NULL;
GO

GO
/* Password recovery outbox. Additive and safe to rerun. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.users', N'password_recovery_invalid_before') IS NULL
    ALTER TABLE dbo.users ADD password_recovery_invalid_before DATETIME2(7) NULL;
IF OBJECT_ID(N'dbo.password_recovery_requests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.password_recovery_requests (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_password_recovery_requests PRIMARY KEY,
        email_hash VARCHAR(64) NOT NULL,
        protected_payload NVARCHAR(MAX) NULL,
        status VARCHAR(20) NOT NULL,
        created_at DATETIME2(7) NOT NULL,
        expires_at DATETIME2(7) NOT NULL,
        next_attempt_at DATETIME2(7) NOT NULL,
        attempt_count INT NOT NULL CONSTRAINT df_password_recovery_attempts DEFAULT(0),
        lease_token UNIQUEIDENTIFIER NULL,
        lease_until DATETIME2(7) NULL,
        reset_token_id BIGINT NULL CONSTRAINT fk_password_recovery_token REFERENCES dbo.password_reset_tokens(id),
        completed_at DATETIME2(7) NULL,
        error_code VARCHAR(40) NULL,
        CONSTRAINT ck_password_recovery_status CHECK(status IN ('PENDING','SENDING','RETRY','SENT','SKIPPED','SUPERSEDED','EXPIRED','FAILED')),
        CONSTRAINT ck_password_recovery_attempts CHECK(attempt_count >= 0),
        CONSTRAINT ck_password_recovery_expiry CHECK(expires_at > created_at)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.password_recovery_requests') AND name=N'ix_password_recovery_claim')
    CREATE INDEX ix_password_recovery_claim ON dbo.password_recovery_requests(status,next_attempt_at,lease_until);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.password_recovery_requests') AND name=N'ix_password_recovery_email')
    CREATE INDEX ix_password_recovery_email ON dbo.password_recovery_requests(email_hash,id DESC);
COMMIT TRANSACTION;

SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.users', 'row_version') IS NULL
    ALTER TABLE dbo.users ADD row_version rowversion NOT NULL;
COMMIT;
GO

GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.chat_conversations','U') IS NULL
BEGIN
    CREATE TABLE dbo.chat_conversations (
        id bigint IDENTITY PRIMARY KEY,
        kind nvarchar(10) NOT NULL,
        team_id bigint NULL REFERENCES dbo.teams(id),
        project_id bigint NULL REFERENCES dbo.projects(id),
        first_user_id bigint NULL REFERENCES dbo.users(id),
        second_user_id bigint NULL REFERENCES dbo.users(id),
        status nvarchar(12) NOT NULL DEFAULT 'OPEN',
        sequence bigint NOT NULL DEFAULT 0,
        version bigint NOT NULL DEFAULT 0,
        created_at datetime2(7) NOT NULL,
        updated_at datetime2(7) NOT NULL,
        concurrency_token uniqueidentifier NOT NULL DEFAULT NEWID(),
        CONSTRAINT ck_chat_conversation_shape CHECK (
            (kind='DIRECT' AND team_id IS NULL AND project_id IS NULL AND first_user_id IS NOT NULL AND second_user_id IS NOT NULL AND first_user_id<second_user_id) OR
            (kind='TEAM' AND team_id IS NOT NULL AND project_id IS NULL AND first_user_id IS NULL AND second_user_id IS NULL) OR
            (kind='PROJECT' AND project_id IS NOT NULL AND team_id IS NULL AND first_user_id IS NULL AND second_user_id IS NULL)),
        CONSTRAINT ck_chat_conversation_state CHECK(status IN ('OPEN','READ_ONLY') AND sequence>=0 AND version>=0)
    );
    CREATE UNIQUE INDEX ux_chat_direct ON dbo.chat_conversations(first_user_id,second_user_id) WHERE kind='DIRECT';
    CREATE UNIQUE INDEX ux_chat_team ON dbo.chat_conversations(team_id) WHERE kind='TEAM';
    CREATE UNIQUE INDEX ux_chat_project ON dbo.chat_conversations(project_id) WHERE kind='PROJECT';
    CREATE INDEX ix_chat_inbox ON dbo.chat_conversations(updated_at DESC,id DESC);
END;
IF OBJECT_ID('dbo.chat_membership_intervals','U') IS NULL
BEGIN
    CREATE TABLE dbo.chat_membership_intervals (
        id bigint IDENTITY PRIMARY KEY,
        conversation_id bigint NOT NULL REFERENCES dbo.chat_conversations(id),
        user_id bigint NOT NULL REFERENCES dbo.users(id),
        from_sequence bigint NOT NULL,
        left_sequence bigint NULL,
        source_key nvarchar(64) NOT NULL,
        retained bit NOT NULL DEFAULT 0,
        joined_at datetime2(7) NOT NULL,
        left_at datetime2(7) NULL,
        CONSTRAINT ck_chat_interval CHECK(from_sequence>0 AND (left_sequence IS NULL OR left_sequence>=from_sequence-1))
    );
    CREATE UNIQUE INDEX ux_chat_open_member ON dbo.chat_membership_intervals(conversation_id,user_id) WHERE left_at IS NULL;
    CREATE INDEX ix_chat_member_lookup ON dbo.chat_membership_intervals(user_id,conversation_id,left_at) INCLUDE(from_sequence,retained);
END;
IF OBJECT_ID('dbo.chat_messages','U') IS NULL
BEGIN
    CREATE TABLE dbo.chat_messages (
        id bigint IDENTITY PRIMARY KEY,
        conversation_id bigint NOT NULL REFERENCES dbo.chat_conversations(id),
        sequence bigint NOT NULL,
        sender_id bigint NOT NULL REFERENCES dbo.users(id),
        client_message_id uniqueidentifier NOT NULL,
        request_hash varchar(64) NOT NULL,
        reply_to_message_id bigint NULL,
        body nvarchar(4000) NULL,
        created_at datetime2(7) NOT NULL,
        edited_at datetime2(7) NULL,
        recalled_at datetime2(7) NULL,
        concurrency_token uniqueidentifier NOT NULL,
        CONSTRAINT uq_chat_message_parent UNIQUE(conversation_id,id),
        CONSTRAINT fk_chat_reply FOREIGN KEY(conversation_id,reply_to_message_id) REFERENCES dbo.chat_messages(conversation_id,id),
        CONSTRAINT ck_chat_message_body CHECK((recalled_at IS NULL AND body IS NOT NULL AND LEN(body)>0) OR (recalled_at IS NOT NULL AND body IS NULL)),
        CONSTRAINT ck_chat_message_sequence CHECK(sequence>0)
    );
    CREATE UNIQUE INDEX ux_chat_message_order ON dbo.chat_messages(conversation_id,sequence);
    CREATE UNIQUE INDEX ux_chat_message_retry ON dbo.chat_messages(conversation_id,sender_id,client_message_id);
END;
IF OBJECT_ID('dbo.chat_member_state','U') IS NULL
BEGIN
    CREATE TABLE dbo.chat_member_state (
        conversation_id bigint NOT NULL REFERENCES dbo.chat_conversations(id),
        user_id bigint NOT NULL REFERENCES dbo.users(id),
        last_read_sequence bigint NOT NULL DEFAULT 0 CHECK(last_read_sequence>=0),
        updated_at datetime2(7) NOT NULL,
        PRIMARY KEY(conversation_id,user_id)
    );
END;
IF OBJECT_ID('dbo.chat_outbox','U') IS NULL
BEGIN
    CREATE TABLE dbo.chat_outbox (
        id bigint IDENTITY PRIMARY KEY,
        event_id uniqueidentifier NOT NULL UNIQUE,
        conversation_id bigint NOT NULL REFERENCES dbo.chat_conversations(id),
        version bigint NOT NULL,
        event_type nvarchar(32) NOT NULL,
        status nvarchar(12) NOT NULL DEFAULT 'PENDING',
        attempt_count int NOT NULL DEFAULT 0,
        next_attempt_at datetime2(7) NOT NULL,
        lease_token uniqueidentifier NULL,
        lease_until datetime2(7) NULL,
        created_at datetime2(7) NOT NULL,
        completed_at datetime2(7) NULL,
        error_code nvarchar(64) NULL,
        CONSTRAINT ck_chat_outbox_status CHECK(status IN ('PENDING','PROCESSING','SUCCEEDED','FAILED')),
        CONSTRAINT ck_chat_outbox_attempt CHECK(attempt_count>=0)
    );
    CREATE INDEX ix_chat_outbox_claim ON dbo.chat_outbox(status,next_attempt_at,lease_until);
END;
COMMIT;
GO
-- This view grants chat participation, never platform-wide Admin access.
CREATE OR ALTER VIEW dbo.chat_scope_members AS
SELECT CAST('TEAM' AS nvarchar(10)) AS kind, tm.team_id AS scope_id, tm.user_id,
    CAST(CONCAT('T:',tm.id,':',CONVERT(varchar(33),tm.joined_at,126)) AS nvarchar(128)) AS source_key
FROM dbo.team_members tm JOIN dbo.users u ON u.id=tm.user_id
JOIN dbo.departments d ON d.id=u.department_id JOIN dbo.organizations o ON o.id=d.organization_id
WHERE tm.left_at IS NULL AND u.status='ACTIVE' AND d.is_active=1 AND o.is_active=1
AND EXISTS(SELECT 1 FROM dbo.user_roles ur JOIN dbo.roles r ON r.id=ur.role_id WHERE ur.user_id=u.id AND r.code='STUDENT')
UNION ALL
SELECT 'PROJECT',p.id,tm.user_id,CAST(CONCAT('T:',tm.id,':',CONVERT(varchar(33),tm.joined_at,126)) AS nvarchar(128))
FROM dbo.projects p JOIN dbo.team_members tm ON tm.team_id=p.team_id JOIN dbo.users u ON u.id=tm.user_id
JOIN dbo.departments d ON d.id=u.department_id JOIN dbo.organizations o ON o.id=d.organization_id
WHERE tm.left_at IS NULL AND u.status='ACTIVE' AND d.is_active=1 AND o.is_active=1
AND EXISTS(SELECT 1 FROM dbo.user_roles ur JOIN dbo.roles r ON r.id=ur.role_id WHERE ur.user_id=u.id AND r.code='STUDENT')
UNION ALL
SELECT 'PROJECT',a.project_id,sp.user_id,CAST(CONCAT('S:',a.id,':',CONVERT(varchar(33),a.assigned_at,126)) AS nvarchar(128))
FROM dbo.supervisor_assignments a JOIN dbo.supervisor_profiles sp ON sp.id=a.supervisor_profile_id
JOIN dbo.users u ON u.id=sp.user_id JOIN dbo.departments d ON d.id=u.department_id JOIN dbo.organizations o ON o.id=d.organization_id
WHERE a.ended_at IS NULL AND u.status='ACTIVE' AND d.is_active=1 AND o.is_active=1 AND a.assignment_type IN ('PRIMARY','DISCIPLINE_MENTOR')
AND EXISTS(SELECT 1 FROM dbo.user_roles ur JOIN dbo.roles r ON r.id=ur.role_id WHERE ur.user_id=u.id AND r.code='LECTURER');
GO
-- Close an interval at the source mutation, even if no chat request observes the gap.
CREATE OR ALTER TRIGGER dbo.tr_chat_team_members ON dbo.team_members AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    UPDATE cm SET left_at=SYSUTCDATETIME(),left_sequence=c.sequence
    FROM dbo.chat_membership_intervals cm JOIN dbo.chat_conversations c ON c.id=cm.conversation_id
    WHERE cm.left_at IS NULL AND cm.retained=0 AND EXISTS (
        SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.id=d.id
        WHERE d.user_id=cm.user_id AND (i.id IS NULL OR i.left_at IS NOT NULL OR i.joined_at<>d.joined_at OR i.user_id<>d.user_id OR i.team_id<>d.team_id)
        AND (c.kind='DIRECT' OR c.team_id=d.team_id OR EXISTS(SELECT 1 FROM dbo.projects p WHERE p.id=c.project_id AND p.team_id=d.team_id)));
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_chat_supervisors ON dbo.supervisor_assignments AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    UPDATE cm SET left_at=SYSUTCDATETIME(),left_sequence=c.sequence
    FROM dbo.chat_membership_intervals cm JOIN dbo.chat_conversations c ON c.id=cm.conversation_id
    WHERE cm.left_at IS NULL AND cm.retained=0 AND EXISTS (
        SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.id=d.id JOIN dbo.supervisor_profiles sp ON sp.id=d.supervisor_profile_id
        WHERE sp.user_id=cm.user_id AND (i.id IS NULL OR i.ended_at IS NOT NULL OR i.supervisor_profile_id<>d.supervisor_profile_id OR i.project_id<>d.project_id)
        AND (c.kind='DIRECT' OR c.project_id=d.project_id));
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_chat_users ON dbo.users AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT (UPDATE(status) OR UPDATE(department_id)) RETURN;
    UPDATE cm SET left_at=SYSUTCDATETIME(),left_sequence=c.sequence
    FROM dbo.chat_membership_intervals cm JOIN dbo.chat_conversations c ON c.id=cm.conversation_id
    JOIN inserted i ON i.id=cm.user_id JOIN deleted d ON d.id=i.id
    WHERE cm.left_at IS NULL AND (i.status<>d.status OR ISNULL(i.department_id,0)<>ISNULL(d.department_id,0));
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_chat_roles ON dbo.user_roles AFTER DELETE,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    UPDATE cm SET left_at=SYSUTCDATETIME(),left_sequence=c.sequence
    FROM dbo.chat_membership_intervals cm JOIN dbo.chat_conversations c ON c.id=cm.conversation_id
    WHERE cm.left_at IS NULL AND EXISTS(SELECT 1 FROM deleted d WHERE d.user_id=cm.user_id);
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_chat_project_close ON dbo.projects AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(status) RETURN;
    UPDATE cm SET retained=1 FROM dbo.chat_membership_intervals cm
    JOIN dbo.chat_conversations c ON c.id=cm.conversation_id JOIN inserted i ON i.id=c.project_id JOIN deleted d ON d.id=i.id
    WHERE cm.left_at IS NULL AND c.kind='PROJECT' AND c.status='OPEN'
        AND i.status IN ('COMPLETED','ARCHIVED','CANCELLED') AND i.status<>d.status
        AND EXISTS(SELECT 1 FROM dbo.chat_scope_members s WHERE s.kind='PROJECT' AND s.scope_id=i.id AND s.user_id=cm.user_id);
    UPDATE c SET status='READ_ONLY',updated_at=SYSUTCDATETIME(),version=version+1
    FROM dbo.chat_conversations c JOIN inserted i ON i.id=c.project_id
    WHERE c.kind='PROJECT' AND c.status='OPEN' AND i.status IN ('COMPLETED','ARCHIVED','CANCELLED');
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_chat_team_close ON dbo.teams AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(status) RETURN;
    UPDATE cm SET retained=1 FROM dbo.chat_membership_intervals cm
    JOIN dbo.chat_conversations c ON c.id=cm.conversation_id JOIN inserted i ON i.id=c.team_id JOIN deleted d ON d.id=i.id
    WHERE cm.left_at IS NULL AND c.kind='TEAM' AND c.status='OPEN'
        AND i.status IN ('DISBANDED','CLOSED','ARCHIVED') AND i.status<>d.status
        AND EXISTS(SELECT 1 FROM dbo.chat_scope_members s WHERE s.kind='TEAM' AND s.scope_id=i.id AND s.user_id=cm.user_id);
    UPDATE c SET status='READ_ONLY',updated_at=SYSUTCDATETIME(),version=version+1
    FROM dbo.chat_conversations c JOIN inserted i ON i.id=c.team_id
    WHERE c.kind='TEAM' AND c.status='OPEN' AND i.status IN ('DISBANDED','CLOSED','ARCHIVED');
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_chat_department ON dbo.departments AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(is_active) RETURN;
    UPDATE cm SET left_at=SYSUTCDATETIME(),left_sequence=c.sequence
    FROM dbo.chat_membership_intervals cm JOIN dbo.chat_conversations c ON c.id=cm.conversation_id JOIN dbo.users u ON u.id=cm.user_id
    JOIN inserted i ON i.id=u.department_id JOIN deleted d ON d.id=i.id
    WHERE cm.left_at IS NULL AND i.is_active=0 AND d.is_active=1;
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_chat_organization ON dbo.organizations AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(is_active) RETURN;
    UPDATE cm SET left_at=SYSUTCDATETIME(),left_sequence=c.sequence
    FROM dbo.chat_membership_intervals cm JOIN dbo.chat_conversations c ON c.id=cm.conversation_id JOIN dbo.users u ON u.id=cm.user_id
    JOIN dbo.departments dept ON dept.id=u.department_id JOIN inserted i ON i.id=dept.organization_id JOIN deleted d ON d.id=i.id
    WHERE cm.left_at IS NULL AND i.is_active=0 AND d.is_active=1;
END;
GO
