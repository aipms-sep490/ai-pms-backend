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
SET QUOTED_IDENTIFIER ON;
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
    id                  BIGINT IDENTITY(1,1) NOT NULL,
    academic_semester_id BIGINT NOT NULL,
    code                NVARCHAR(50) NOT NULL,
    name                NVARCHAR(255) NOT NULL,
    period_type         NVARCHAR(50) NOT NULL,
    start_at            DATETIME2(0) NOT NULL,
    end_at              DATETIME2(0) NOT NULL,
    status              NVARCHAR(20) NOT NULL CONSTRAINT df_project_periods_status DEFAULT (N'DRAFT'),
    created_at          DATETIME2(0) NOT NULL CONSTRAINT df_project_periods_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at          DATETIME2(0) NOT NULL CONSTRAINT df_project_periods_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_project_periods PRIMARY KEY (id),
    CONSTRAINT uq_project_periods_semester_code UNIQUE (academic_semester_id, code),
    CONSTRAINT ck_project_periods_dates CHECK (end_at >= start_at),
    CONSTRAINT ck_project_periods_type CHECK (period_type IN (
        N'REGISTRATION', N'PROJECT_REVIEW', N'SUPERVISOR_SELECTION',
        N'EXECUTION', N'FINAL_SUBMISSION', N'EVALUATION'
    )),
    CONSTRAINT ck_project_periods_status CHECK (status IN (N'DRAFT', N'UPCOMING', N'ACTIVE', N'CLOSED', N'ARCHIVED')),
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

CREATE TABLE dbo.users (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    department_id   BIGINT NULL,  -- lecturer / department staff affiliation
    major_id        BIGINT NULL,  -- student major; department is derived through majors
    email           NVARCHAR(320) NOT NULL,
    password_hash   NVARCHAR(500) NOT NULL,
    full_name       NVARCHAR(255) NOT NULL,
    phone           NVARCHAR(30) NULL,
    student_code    NVARCHAR(50) NULL,
    employee_code   NVARCHAR(50) NULL,
    title           NVARCHAR(100) NULL,
    status          NVARCHAR(20) NOT NULL CONSTRAINT df_users_status DEFAULT (N'ACTIVE'),
    last_login_at   DATETIME2(0) NULL,
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_users_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_users_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_users PRIMARY KEY (id),
    CONSTRAINT uq_users_email UNIQUE (email),
    CONSTRAINT ck_users_status CHECK (status IN (N'ACTIVE', N'INACTIVE', N'SUSPENDED')),
    CONSTRAINT fk_users_department FOREIGN KEY (department_id)
        REFERENCES dbo.departments(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_users_major FOREIGN KEY (major_id)
        REFERENCES dbo.majors(id) ON DELETE NO ACTION ON UPDATE NO ACTION
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
    assigned_at     DATETIME2(0) NOT NULL CONSTRAINT df_user_roles_assigned_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_user_roles PRIMARY KEY (id),
    CONSTRAINT uq_user_roles_user_role UNIQUE (user_id, role_id),
    CONSTRAINT fk_user_roles_user FOREIGN KEY (user_id)
        REFERENCES dbo.users(id) ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT fk_user_roles_role FOREIGN KEY (role_id)
        REFERENCES dbo.roles(id) ON DELETE NO ACTION ON UPDATE NO ACTION
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
    CONSTRAINT uq_projects_team UNIQUE (team_id),
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
    status                  NVARCHAR(20) NOT NULL CONSTRAINT df_supervisor_requests_status DEFAULT (N'PENDING'),
    request_message         NVARCHAR(2000) NULL,
    response_message        NVARCHAR(2000) NULL,
    requested_at            DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_requests_requested_at DEFAULT (SYSUTCDATETIME()),
    responded_at            DATETIME2(0) NULL,
    created_at              DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_requests_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at              DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_requests_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_supervisor_requests PRIMARY KEY (id),
    CONSTRAINT ck_supervisor_requests_status CHECK (status IN (N'PENDING', N'ACCEPTED', N'REJECTED', N'CANCELLED')),
    CONSTRAINT fk_supervisor_requests_project FOREIGN KEY (project_id)
        REFERENCES dbo.projects(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_supervisor_requests_profile FOREIGN KEY (supervisor_profile_id)
        REFERENCES dbo.supervisor_profiles(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_supervisor_requests_requested_by FOREIGN KEY (requested_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE UNIQUE INDEX ux_supervisor_requests_pending
ON dbo.supervisor_requests(project_id, supervisor_profile_id)
WHERE status = N'PENDING';
GO

CREATE TABLE dbo.supervisor_assignments (
    id                      BIGINT IDENTITY(1,1) NOT NULL,
    project_id              BIGINT NOT NULL,
    supervisor_profile_id   BIGINT NOT NULL,
    supervisor_request_id   BIGINT NOT NULL,
    is_primary              BIT NOT NULL CONSTRAINT df_supervisor_assignments_is_primary DEFAULT (0),
    assigned_at             DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_assignments_assigned_at DEFAULT (SYSUTCDATETIME()),
    ended_at                DATETIME2(0) NULL,
    created_at              DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_assignments_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at              DATETIME2(0) NOT NULL CONSTRAINT df_supervisor_assignments_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_supervisor_assignments PRIMARY KEY (id),
    CONSTRAINT uq_supervisor_assignments_request UNIQUE (supervisor_request_id),
    CONSTRAINT uq_supervisor_assignments_project_supervisor UNIQUE (project_id, supervisor_profile_id),
    CONSTRAINT ck_supervisor_assignments_dates CHECK (ended_at IS NULL OR ended_at >= assigned_at),
    CONSTRAINT fk_supervisor_assignments_project FOREIGN KEY (project_id)
        REFERENCES dbo.projects(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_supervisor_assignments_profile FOREIGN KEY (supervisor_profile_id)
        REFERENCES dbo.supervisor_profiles(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_supervisor_assignments_request FOREIGN KEY (supervisor_request_id)
        REFERENCES dbo.supervisor_requests(id) ON DELETE NO ACTION ON UPDATE NO ACTION
);
GO

CREATE UNIQUE INDEX ux_supervisor_assignments_one_primary_active
ON dbo.supervisor_assignments(project_id)
WHERE is_primary = 1 AND ended_at IS NULL;
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
    CONSTRAINT pk_meetings PRIMARY KEY (id),
    CONSTRAINT ck_meetings_dates CHECK (end_at IS NULL OR end_at >= start_at),
    CONSTRAINT ck_meetings_status CHECK (status IN (N'SCHEDULED', N'COMPLETED', N'CANCELLED')),
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
        (CASE WHEN supervisor_feedback_id IS NULL THEN 0 ELSE 1 END) <= 1
    ),
    CONSTRAINT fk_files_uploaded_by FOREIGN KEY (uploaded_by)
        REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_files_deliverable_version FOREIGN KEY (deliverable_version_id)
        REFERENCES dbo.deliverable_versions(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_files_progress_report FOREIGN KEY (progress_report_id)
        REFERENCES dbo.progress_reports(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT fk_files_meeting FOREIGN KEY (meeting_id)
        REFERENCES dbo.meetings(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
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

CREATE TABLE dbo.rubric_criteria (
    id              BIGINT IDENTITY(1,1) NOT NULL,
    rubric_id       BIGINT NOT NULL,
    criterion_id    BIGINT NOT NULL,
    weight_percent  DECIMAL(5,2) NOT NULL,
    max_score       DECIMAL(8,2) NOT NULL,
    sort_order      INT NOT NULL CONSTRAINT df_rubric_criteria_sort_order DEFAULT (0),
    is_required     BIT NOT NULL CONSTRAINT df_rubric_criteria_is_required DEFAULT (1),
    created_at      DATETIME2(0) NOT NULL CONSTRAINT df_rubric_criteria_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at      DATETIME2(0) NOT NULL CONSTRAINT df_rubric_criteria_updated_at DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT pk_rubric_criteria PRIMARY KEY (id),
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
CREATE INDEX ix_evaluations_project_type_status ON dbo.evaluations(project_id, evaluation_type, status);
CREATE INDEX ix_evaluations_evaluator_id ON dbo.evaluations(evaluator_id);
CREATE INDEX ix_evaluations_rubric_id ON dbo.evaluations(rubric_id);
CREATE INDEX ix_evaluation_details_rubric_criterion_id ON dbo.evaluation_details(rubric_criterion_id);
CREATE INDEX ix_notifications_related_entity ON dbo.notifications(related_entity_type, related_entity_id);
CREATE INDEX ix_notification_recipients_user_read ON dbo.notification_recipients(user_id, is_read, created_at DESC);
GO

PRINT N'AI-PMS schema created successfully.';


