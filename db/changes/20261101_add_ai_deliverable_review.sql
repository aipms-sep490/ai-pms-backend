SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.ai_runs','U') IS NOT NULL
BEGIN
  IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_ai_run_kind' AND parent_object_id = OBJECT_ID('dbo.ai_runs'))
    ALTER TABLE dbo.ai_runs DROP CONSTRAINT ck_ai_run_kind;
  ALTER TABLE dbo.ai_runs WITH CHECK ADD CONSTRAINT ck_ai_run_kind CHECK(kind IN ('PROJECT_ASK','REPORT_SUMMARY','PROGRESS_ANALYSIS','DELIVERABLE_REVIEW'));
END;
IF OBJECT_ID('dbo.ai_deliverable_review_runs','U') IS NULL
BEGIN
 CREATE TABLE dbo.ai_deliverable_review_runs(id bigint NOT NULL PRIMARY KEY, project_id bigint NOT NULL, deliverable_id bigint NOT NULL, deliverable_version_id bigint NOT NULL,
   rubric_id bigint NOT NULL, rubric_version int NULL, checklist_id bigint NULL, checklist_version int NULL, evidence_snapshot nvarchar(max) NOT NULL,
   CONSTRAINT fk_ai_review_run_ai FOREIGN KEY(id) REFERENCES dbo.ai_runs(id), CONSTRAINT fk_ai_review_run_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id),
   CONSTRAINT fk_ai_review_run_version FOREIGN KEY(deliverable_version_id) REFERENCES dbo.deliverable_versions(id), CONSTRAINT ck_ai_review_snapshot CHECK(ISJSON(evidence_snapshot)=1));
 CREATE INDEX ix_ai_review_version ON dbo.ai_deliverable_review_runs(project_id, deliverable_version_id, id DESC);
END;
IF OBJECT_ID('dbo.ai_deliverable_review_criteria','U') IS NULL
BEGIN
 CREATE TABLE dbo.ai_deliverable_review_criteria(run_id bigint NOT NULL, criterion_id bigint NOT NULL, criterion_name nvarchar(500) NOT NULL, status varchar(24) NOT NULL,
   CONSTRAINT pk_ai_review_criteria PRIMARY KEY(run_id, criterion_id), CONSTRAINT fk_ai_review_criteria_run FOREIGN KEY(run_id) REFERENCES dbo.ai_deliverable_review_runs(id));
END;
IF OBJECT_ID('dbo.ai_deliverable_review_findings','U') IS NULL
BEGIN
 CREATE TABLE dbo.ai_deliverable_review_findings(id bigint IDENTITY PRIMARY KEY, run_id bigint NOT NULL, criterion_id bigint NOT NULL, kind varchar(24) NOT NULL, content nvarchar(2000) NOT NULL,
   CONSTRAINT fk_ai_review_finding_criterion FOREIGN KEY(run_id, criterion_id) REFERENCES dbo.ai_deliverable_review_criteria(run_id, criterion_id));
END;
IF OBJECT_ID('dbo.ai_deliverable_review_citations','U') IS NULL
BEGIN
 CREATE TABLE dbo.ai_deliverable_review_citations(id bigint IDENTITY PRIMARY KEY, run_id bigint NOT NULL, title nvarchar(500) NOT NULL, version varchar(64) NOT NULL, section nvarchar(500) NOT NULL,
   CONSTRAINT fk_ai_review_citation_run FOREIGN KEY(run_id) REFERENCES dbo.ai_deliverable_review_runs(id));
END;
COMMIT;
