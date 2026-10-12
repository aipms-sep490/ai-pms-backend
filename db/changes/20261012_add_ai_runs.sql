SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.ai_runs','U') IS NULL
BEGIN
 CREATE TABLE dbo.ai_runs(id bigint IDENTITY PRIMARY KEY, project_id bigint NOT NULL,actor_id bigint NOT NULL,report_id bigint NULL,
  kind varchar(24) NOT NULL,status varchar(24) NOT NULL,model_version varchar(64) NOT NULL,config_version varchar(64) NOT NULL,
  scope_hash varchar(64) NOT NULL,result_json nvarchar(max) NULL,evidence_json nvarchar(max) NOT NULL,error_code varchar(64) NULL,
  started_at datetime2(7) NOT NULL,completed_at datetime2(7) NULL,
  CONSTRAINT fk_ai_run_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id),
  CONSTRAINT fk_ai_run_actor FOREIGN KEY(actor_id) REFERENCES dbo.users(id),
  CONSTRAINT fk_ai_run_report FOREIGN KEY(report_id) REFERENCES dbo.progress_reports(id),
  CONSTRAINT ck_ai_run_kind CHECK(kind IN ('PROJECT_ASK','REPORT_SUMMARY')),
  CONSTRAINT ck_ai_run_status CHECK(status IN ('RUNNING','COMPLETED','FALLBACK','INSUFFICIENT_EVIDENCE','FAILED')),
  CONSTRAINT ck_ai_run_result CHECK(result_json IS NULL OR ISJSON(result_json)=1),
  CONSTRAINT ck_ai_run_evidence CHECK(ISJSON(evidence_json)=1));
 CREATE INDEX ix_ai_run_history ON dbo.ai_runs(project_id,actor_id,id DESC);
END;
COMMIT;
GO
