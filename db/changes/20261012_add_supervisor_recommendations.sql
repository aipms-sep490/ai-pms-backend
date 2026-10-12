SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.supervisor_recommendation_runs','U') IS NULL
BEGIN
 CREATE TABLE dbo.supervisor_recommendation_runs (
  id BIGINT IDENTITY NOT NULL CONSTRAINT pk_supervisor_recommendation_runs PRIMARY KEY,
  project_id BIGINT NOT NULL CONSTRAINT fk_recommendation_project REFERENCES dbo.projects(id),
  created_by BIGINT NOT NULL CONSTRAINT fk_recommendation_actor REFERENCES dbo.users(id),
  created_at DATETIME2(0) NOT NULL, engine_version VARCHAR(30) NOT NULL,
  assignment_type VARCHAR(30) NOT NULL, major_id BIGINT NULL CONSTRAINT fk_recommendation_major REFERENCES dbo.majors(id),
  search NVARCHAR(255) NULL, page INT NOT NULL, page_size INT NOT NULL, total_count BIGINT NOT NULL,
  CONSTRAINT ck_recommendation_scope CHECK ((assignment_type='PRIMARY' AND major_id IS NULL) OR (assignment_type='DISCIPLINE_MENTOR' AND major_id IS NOT NULL)),
  CONSTRAINT ck_recommendation_paging CHECK (page BETWEEN 1 AND 100000 AND page_size BETWEEN 1 AND 100 AND total_count >= 0)
 );
 CREATE INDEX ix_recommendation_project_history ON dbo.supervisor_recommendation_runs(project_id,id DESC);
END;
IF OBJECT_ID('dbo.supervisor_recommendation_items','U') IS NULL
BEGIN
 CREATE TABLE dbo.supervisor_recommendation_items (
  run_id BIGINT NOT NULL CONSTRAINT fk_recommendation_item_run REFERENCES dbo.supervisor_recommendation_runs(id),
  position INT NOT NULL CONSTRAINT ck_recommendation_position CHECK(position BETWEEN 1 AND 100),
  supervisor_profile_id BIGINT NOT NULL CONSTRAINT fk_recommendation_profile REFERENCES dbo.supervisor_profiles(id),
  snapshot_json NVARCHAR(MAX) NOT NULL CONSTRAINT ck_recommendation_snapshot CHECK(ISJSON(snapshot_json)=1),
  CONSTRAINT pk_supervisor_recommendation_items PRIMARY KEY(run_id,position),
  CONSTRAINT uq_recommendation_profile UNIQUE(run_id,supervisor_profile_id)
 );
END;
COMMIT;
GO
