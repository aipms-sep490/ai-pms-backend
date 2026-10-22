SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.review_gate_evidence', 'link_url') IS NULL
    ALTER TABLE dbo.review_gate_evidence ADD link_url varchar(2048) NULL;

IF COL_LENGTH('dbo.supervisor_feedback', 'gate_code') IS NULL
    ALTER TABLE dbo.supervisor_feedback ADD gate_code varchar(20) NULL;

COMMIT;
