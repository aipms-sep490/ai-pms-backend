-- Aggregate-only diagnostics: no IDs, names, stored file keys or snapshot JSON leave SQL.
SELECT 'DUPLICATE_ACTIVE_PRIMARY' AS code,COUNT_BIG(*) AS affected
FROM (SELECT project_id FROM dbo.supervisor_assignments WHERE ended_at IS NULL AND assignment_type='PRIMARY' AND is_primary=1 GROUP BY project_id HAVING COUNT(*)>1) x
UNION ALL
SELECT 'DUPLICATE_ACTIVE_MENTOR',COUNT_BIG(*)
FROM (SELECT project_id,major_id FROM dbo.supervisor_assignments WHERE ended_at IS NULL AND assignment_type='DISCIPLINE_MENTOR' GROUP BY project_id,major_id HAVING COUNT(*)>1) x
UNION ALL
SELECT 'PRIMARY_EVALUATOR_CONFLICT',COUNT_BIG(*) FROM dbo.supervisor_assignments a
JOIN dbo.supervisor_profiles sp ON sp.id=a.supervisor_profile_id
WHERE a.ended_at IS NULL AND a.assignment_type='PRIMARY' AND a.is_primary=1
AND EXISTS(SELECT 1 FROM dbo.evaluation_assignments e WHERE e.project_id=a.project_id AND e.evaluator_id=sp.user_id AND e.status='ACTIVE')
UNION ALL
SELECT 'MISSING_FROZEN_REGISTRATION',COUNT_BIG(*) FROM dbo.projects p
WHERE p.status NOT IN ('DRAFT','REVISION_REQUIRED') AND NOT EXISTS(SELECT 1 FROM dbo.project_registration_snapshots s WHERE s.project_id=p.id)
UNION ALL
SELECT 'UNKNOWN_SNAPSHOT_LEAD_OR_MAJOR_MAP',COUNT_BIG(*) FROM dbo.projects p
CROSS APPLY(SELECT TOP(1) snapshot_json FROM dbo.project_registration_snapshots s WHERE s.project_id=p.id ORDER BY s.id DESC) s
WHERE p.status NOT IN ('DRAFT','REVISION_REQUIRED')
AND (TRY_CONVERT(bigint,JSON_VALUE(s.snapshot_json,'$.Scope.LeadDepartmentId')) IS NULL
 OR JSON_QUERY(s.snapshot_json,'$.MajorDepartmentIds') IS NULL)
UNION ALL
SELECT 'FINAL_FILE_MISSING_EXACT_REFERENCE',COUNT_BIG(*) FROM dbo.final_submission_items i
CROSS APPLY OPENJSON(i.files_json) f
WHERE NULLIF(JSON_VALUE(f.value,'$.StorageKey'),'') IS NULL
 OR TRY_CONVERT(bigint,JSON_VALUE(f.value,'$.Metadata.Id')) IS NULL
 OR NULLIF(JSON_VALUE(f.value,'$.Metadata.Sha256'),'') IS NULL
UNION ALL
SELECT 'FINAL_FILE_UNKNOWN_TARGET',COUNT_BIG(*) FROM dbo.final_submission_items i
CROSS APPLY OPENJSON(i.files_json) f
WHERE JSON_VALUE(f.value,'$.EvidenceScope') IS NULL
 OR JSON_VALUE(f.value,'$.EvidenceScope') NOT IN ('COMMON','MAJOR_SPECIFIC','INDIVIDUAL')
 OR (JSON_VALUE(f.value,'$.EvidenceScope') IN ('MAJOR_SPECIFIC','INDIVIDUAL') AND TRY_CONVERT(bigint,JSON_VALUE(f.value,'$.TargetMajorId')) IS NULL)
 OR (JSON_VALUE(f.value,'$.EvidenceScope')='INDIVIDUAL' AND TRY_CONVERT(bigint,JSON_VALUE(f.value,'$.TargetStudentId')) IS NULL);
