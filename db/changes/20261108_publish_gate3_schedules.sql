IF COL_LENGTH('dbo.gate_session_scripts', 'published_at') IS NULL
    ALTER TABLE dbo.gate_session_scripts ADD published_at datetimeoffset(0) NULL;

IF COL_LENGTH('dbo.gate_session_scripts', 'published_by') IS NULL
    ALTER TABLE dbo.gate_session_scripts ADD published_by bigint NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_gate_script_published_by'
    AND parent_object_id = OBJECT_ID('dbo.gate_session_scripts'))
    EXEC(N'ALTER TABLE dbo.gate_session_scripts ADD CONSTRAINT FK_gate_script_published_by
        FOREIGN KEY (published_by) REFERENCES dbo.users(id)');

-- Existing offline R3 scripts already reserve a room. Preserve them as legacy published schedules;
-- their original publisher was not recorded, so published_by intentionally remains NULL.
EXEC(N'UPDATE s SET published_at = s.created_at
FROM dbo.gate_session_scripts AS s
JOIN dbo.project_gate_instances AS g ON g.id = s.project_gate_instance_id
WHERE g.sequence = 3 AND s.mode = ''OFFLINE'' AND s.room_id IS NOT NULL
    AND s.published_at IS NULL');
