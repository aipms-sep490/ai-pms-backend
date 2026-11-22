-- PR-P0-C2 (P0.5): Isolated preview derivatives cache for DOCX/XLSX/PPTX/ZIP safe inline previews.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.file_preview_derivatives', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.file_preview_derivatives (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_file_preview_derivatives PRIMARY KEY,
        file_id bigint NULL,
        gate_evidence_id bigint NULL,
        derivative_type varchar(30) NOT NULL,
        status varchar(20) NOT NULL,
        payload_json nvarchar(max) NULL,
        error_reason nvarchar(500) NULL,
        created_at datetime2(0) NOT NULL,
        updated_at datetime2(0) NOT NULL,
        CONSTRAINT fk_file_preview_deriv_file FOREIGN KEY (file_id) REFERENCES dbo.files(id),
        CONSTRAINT fk_file_preview_deriv_gate_ev FOREIGN KEY (gate_evidence_id) REFERENCES dbo.review_gate_evidence(id),
        CONSTRAINT ck_file_preview_deriv_target CHECK (file_id IS NOT NULL OR gate_evidence_id IS NOT NULL),
        CONSTRAINT ck_file_preview_deriv_type CHECK (derivative_type IN ('OFFICE_TEXT','PPTX_SLIDES','XLSX_GRID','ZIP_TREE','IMAGE_SAFE')),
        CONSTRAINT ck_file_preview_deriv_status CHECK (status IN ('READY','PREVIEW_FAILED'))
    );
    CREATE INDEX ix_file_preview_deriv_file ON dbo.file_preview_derivatives(file_id) WHERE file_id IS NOT NULL;
    CREATE INDEX ix_file_preview_deriv_gate_ev ON dbo.file_preview_derivatives(gate_evidence_id) WHERE gate_evidence_id IS NOT NULL;
END;

COMMIT TRANSACTION;
GO
