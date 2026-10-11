SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.weekly_assessments','U') IS NULL
BEGIN
 CREATE TABLE dbo.weekly_assessments(
  id bigint IDENTITY PRIMARY KEY, project_id bigint NOT NULL, student_id bigint NOT NULL, major_id bigint NOT NULL,
  academic_semester_id bigint NOT NULL, week_number int NOT NULL, start_date date NOT NULL, end_date date NOT NULL,
  time_zone_id varchar(100) NOT NULL, version int NOT NULL, status varchar(20) NOT NULL,
  rubric_id bigint NOT NULL, rubric_json nvarchar(max) NOT NULL, total_score decimal(5,2) NULL,
  comments nvarchar(2000) NULL, revision_reason nvarchar(1000) NULL,
  concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_weekly_token DEFAULT NEWID(),
  created_by bigint NOT NULL, primary_assignment_id bigint NOT NULL, created_at datetime2(0) NOT NULL,
  updated_at datetime2(0) NOT NULL, submitted_at datetime2(0) NULL,
  CONSTRAINT fk_weekly_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id),
  CONSTRAINT fk_weekly_student FOREIGN KEY(student_id) REFERENCES dbo.users(id),
  CONSTRAINT fk_weekly_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id),
  CONSTRAINT fk_weekly_semester FOREIGN KEY(academic_semester_id) REFERENCES dbo.academic_semesters(id),
  CONSTRAINT fk_weekly_rubric FOREIGN KEY(rubric_id) REFERENCES dbo.rubrics(id),
  CONSTRAINT fk_weekly_primary FOREIGN KEY(primary_assignment_id) REFERENCES dbo.supervisor_assignments(id),
  CONSTRAINT fk_weekly_creator FOREIGN KEY(created_by) REFERENCES dbo.users(id),
  CONSTRAINT ck_weekly_dates CHECK(week_number BETWEEN 1 AND 105 AND end_date>=start_date AND DATEDIFF(day,start_date,end_date)<=6),
  CONSTRAINT ck_weekly_version CHECK(version>0), CONSTRAINT ck_weekly_rubric_json CHECK(ISJSON(rubric_json)=1),
  CONSTRAINT ck_weekly_total CHECK(total_score IS NULL OR total_score BETWEEN 0 AND 10),
  CONSTRAINT ck_weekly_status CHECK((status IN ('DRAFT','SUPERSEDED') AND submitted_at IS NULL) OR (status='SUBMITTED' AND submitted_at IS NOT NULL AND total_score IS NOT NULL)),
  CONSTRAINT uq_weekly_version UNIQUE(project_id,student_id,week_number,version));
 CREATE UNIQUE INDEX ux_weekly_draft ON dbo.weekly_assessments(project_id,student_id,week_number) WHERE status='DRAFT';
 CREATE INDEX ix_weekly_student ON dbo.weekly_assessments(student_id,status,project_id,week_number);
END;
IF OBJECT_ID('dbo.weekly_assessment_scores','U') IS NULL
 CREATE TABLE dbo.weekly_assessment_scores(
  assessment_id bigint NOT NULL, rubric_criterion_id bigint NOT NULL, score decimal(8,2) NULL, comments nvarchar(2000) NULL,
  CONSTRAINT pk_weekly_scores PRIMARY KEY(assessment_id,rubric_criterion_id),
  CONSTRAINT fk_weekly_score_assessment FOREIGN KEY(assessment_id) REFERENCES dbo.weekly_assessments(id),
  CONSTRAINT ck_weekly_score CHECK(score IS NULL OR score>=0));
COMMIT;
GO
