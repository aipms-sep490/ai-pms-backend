SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.cold_checklist_items', N'U') IS NULL
    THROW 51000, 'Apply the cold submissions schema before this change.', 1;

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.cold_checklist_items')
           AND name = N'ck_cold_item_target')
    ALTER TABLE dbo.cold_checklist_items DROP CONSTRAINT ck_cold_item_target;

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.cold_checklist_items')
           AND name = N'major_id' AND is_nullable = 0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.cold_checklist_items')
               AND name = N'uq_cold_item_target')
        ALTER TABLE dbo.cold_checklist_items DROP CONSTRAINT uq_cold_item_target;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.cold_checklist_items')
               AND name = N'fk_cold_item_major')
        ALTER TABLE dbo.cold_checklist_items DROP CONSTRAINT fk_cold_item_major;
    ALTER TABLE dbo.cold_checklist_items ALTER COLUMN major_id bigint NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.cold_checklist_items')
               AND name = N'fk_cold_item_major')
    ALTER TABLE dbo.cold_checklist_items WITH CHECK ADD CONSTRAINT fk_cold_item_major
        FOREIGN KEY(major_id) REFERENCES dbo.majors(id);
ALTER TABLE dbo.cold_checklist_items WITH CHECK ADD CONSTRAINT ck_cold_item_target CHECK (
    (scope = 'COMMON' AND major_id IS NULL AND student_id IS NULL) OR
    (scope = 'MAJOR_SPECIFIC' AND major_id IS NOT NULL AND student_id IS NULL) OR
    (scope = 'INDIVIDUAL' AND major_id IS NOT NULL AND student_id IS NOT NULL));
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.cold_checklist_items')
               AND name = N'uq_cold_item_target')
    ALTER TABLE dbo.cold_checklist_items ADD CONSTRAINT uq_cold_item_target
        UNIQUE(package_id, report_type, major_id, student_id);

COMMIT;
GO
