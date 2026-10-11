SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.cold_report_templates','U') IS NULL
BEGIN
 CREATE TABLE dbo.cold_report_templates (
  id bigint IDENTITY PRIMARY KEY, major_id bigint NOT NULL, project_period_id bigint NOT NULL,
  version int NOT NULL, name nvarchar(200) NOT NULL, reports_json nvarchar(max) NOT NULL,
  created_by bigint NOT NULL, created_at datetime2(0) NOT NULL,
  CONSTRAINT fk_cold_template_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id),
  CONSTRAINT fk_cold_template_period FOREIGN KEY(project_period_id) REFERENCES dbo.project_periods(id),
  CONSTRAINT fk_cold_template_creator FOREIGN KEY(created_by) REFERENCES dbo.users(id),
  CONSTRAINT ck_cold_template_json CHECK(ISJSON(reports_json)=1),
  CONSTRAINT ck_cold_template_version CHECK(version>0),
  CONSTRAINT uq_cold_template_version UNIQUE(major_id,project_period_id,version));
END;
IF OBJECT_ID('dbo.cold_packages','U') IS NULL
BEGIN
 CREATE TABLE dbo.cold_packages (
  id bigint IDENTITY PRIMARY KEY, project_id bigint NOT NULL, project_period_id bigint NOT NULL,
  deadline datetime2(0) NOT NULL, status varchar(20) NOT NULL,
  registration_snapshot_json nvarchar(max) NOT NULL,
  concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_cold_package_token DEFAULT NEWID(),
  created_by bigint NOT NULL, created_at datetime2(0) NOT NULL, locked_by bigint NULL, locked_at datetime2(0) NULL,
  CONSTRAINT uq_cold_package_project UNIQUE(project_id),
  CONSTRAINT fk_cold_package_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id),
  CONSTRAINT fk_cold_package_period FOREIGN KEY(project_period_id) REFERENCES dbo.project_periods(id),
  CONSTRAINT fk_cold_package_creator FOREIGN KEY(created_by) REFERENCES dbo.users(id),
  CONSTRAINT fk_cold_package_locker FOREIGN KEY(locked_by) REFERENCES dbo.users(id),
  CONSTRAINT ck_cold_package_json CHECK(ISJSON(registration_snapshot_json)=1),
  CONSTRAINT ck_cold_package_status CHECK((status='OPEN' AND locked_by IS NULL AND locked_at IS NULL)
   OR (status='LOCKED' AND locked_by IS NOT NULL AND locked_at IS NOT NULL)));
END;
IF OBJECT_ID('dbo.cold_checklist_items','U') IS NULL
BEGIN
 CREATE TABLE dbo.cold_checklist_items (
  id bigint IDENTITY PRIMARY KEY, package_id bigint NOT NULL, template_id bigint NOT NULL,
  template_version int NOT NULL, report_type varchar(80) NOT NULL, name nvarchar(200) NOT NULL,
  required bit NOT NULL, scope varchar(20) NOT NULL, major_id bigint NOT NULL, student_id bigint NULL,
  CONSTRAINT fk_cold_item_package FOREIGN KEY(package_id) REFERENCES dbo.cold_packages(id),
  CONSTRAINT fk_cold_item_template FOREIGN KEY(template_id) REFERENCES dbo.cold_report_templates(id),
  CONSTRAINT fk_cold_item_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id),
  CONSTRAINT fk_cold_item_student FOREIGN KEY(student_id) REFERENCES dbo.users(id),
  CONSTRAINT ck_cold_item_target CHECK((scope='MAJOR_SPECIFIC' AND student_id IS NULL) OR (scope='INDIVIDUAL' AND student_id IS NOT NULL)),
  CONSTRAINT uq_cold_item_target UNIQUE(package_id,report_type,major_id,student_id));
END;
IF OBJECT_ID('dbo.cold_submissions','U') IS NULL
BEGIN
 CREATE TABLE dbo.cold_submissions (
  id bigint IDENTITY PRIMARY KEY, checklist_item_id bigint NOT NULL, version int NOT NULL,
  status varchar(20) NOT NULL, note nvarchar(2000) NULL,
  concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_cold_submission_token DEFAULT NEWID(),
  created_by bigint NOT NULL, created_at datetime2(0) NOT NULL, submitted_at datetime2(0) NULL, locked_at datetime2(0) NULL,
  CONSTRAINT fk_cold_submission_item FOREIGN KEY(checklist_item_id) REFERENCES dbo.cold_checklist_items(id),
  CONSTRAINT fk_cold_submission_creator FOREIGN KEY(created_by) REFERENCES dbo.users(id),
  CONSTRAINT uq_cold_submission_version UNIQUE(checklist_item_id,version),
  CONSTRAINT ck_cold_submission_version CHECK(version>0),
  CONSTRAINT ck_cold_submission_status CHECK((status='DRAFT' AND submitted_at IS NULL AND locked_at IS NULL)
   OR (status='SUBMITTED' AND submitted_at IS NOT NULL AND locked_at IS NULL)
   OR (status='LOCKED' AND submitted_at IS NOT NULL AND locked_at IS NOT NULL)));
 CREATE UNIQUE INDEX ux_cold_submission_draft ON dbo.cold_submissions(checklist_item_id) WHERE status='DRAFT';
END;
IF OBJECT_ID('dbo.cold_submission_files','U') IS NULL
BEGIN
 CREATE TABLE dbo.cold_submission_files (
  submission_id bigint NOT NULL, file_id bigint NOT NULL, file_name nvarchar(255) NOT NULL,
  content_type varchar(150) NOT NULL, size_bytes bigint NOT NULL, sha256 varchar(64) NOT NULL, storage_key nvarchar(500) NOT NULL,
  CONSTRAINT pk_cold_submission_file PRIMARY KEY(submission_id,file_id),
  CONSTRAINT uq_cold_submission_file UNIQUE(file_id),
  CONSTRAINT fk_cold_file_submission FOREIGN KEY(submission_id) REFERENCES dbo.cold_submissions(id),
  CONSTRAINT fk_cold_file_file FOREIGN KEY(file_id) REFERENCES dbo.files(id),
  CONSTRAINT ck_cold_file_size CHECK(size_bytes>0 AND size_bytes<=10485760));
END;
COMMIT;
GO
