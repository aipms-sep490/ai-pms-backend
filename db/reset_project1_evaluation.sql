SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
BEGIN TRY
    -- 1. Delete evaluation details and finalizations
    DELETE FROM dbo.evaluation_details WHERE evaluation_id IN (SELECT id FROM dbo.evaluations WHERE project_id = 1);
    DELETE FROM dbo.evaluation_finalizations WHERE evaluation_id IN (SELECT id FROM dbo.evaluations WHERE project_id = 1);
    DELETE FROM dbo.student_result_evaluations WHERE evaluation_id IN (SELECT id FROM dbo.evaluations WHERE project_id = 1);
    DELETE FROM dbo.result_score_amendments WHERE evaluation_id IN (SELECT id FROM dbo.evaluations WHERE project_id = 1);
    DELETE FROM dbo.project_result_evaluations WHERE evaluation_id IN (SELECT id FROM dbo.evaluations WHERE project_id = 1);

    -- 2. Delete draft states
    DELETE FROM dbo.evaluation_draft_states 
    WHERE assignment_id IN (SELECT id FROM dbo.evaluation_assignments WHERE project_id = 1)
       OR evaluation_id IN (SELECT id FROM dbo.evaluations WHERE project_id = 1);

    -- 3. Delete evaluations
    DELETE FROM dbo.evaluations WHERE project_id = 1;

    -- 4. Delete result appeals & student results
    DELETE FROM dbo.result_appeal_requests 
    WHERE component_id IN (SELECT id FROM dbo.evaluation_scheme_components WHERE scheme_id IN (SELECT id FROM dbo.evaluation_schemes WHERE project_id = 1));

    DELETE FROM dbo.student_results 
    WHERE scheme_id IN (SELECT id FROM dbo.evaluation_schemes WHERE project_id = 1);

    -- 5. Delete evaluation assignments
    DELETE FROM dbo.evaluation_assignments WHERE project_id = 1;

    -- 6. Delete scheme components
    DELETE FROM dbo.evaluation_scheme_components 
    WHERE scheme_id IN (SELECT id FROM dbo.evaluation_schemes WHERE project_id = 1);

    -- 7. Delete evaluation schemes
    DELETE FROM dbo.evaluation_schemes WHERE project_id = 1;

    -- 8. Ensure Rubrics are PUBLISHED
    UPDATE dbo.rubric_versions SET status = 'PUBLISHED' WHERE rubric_id IN (1, 4, 9);

    -- 9. Ensure Project 1 status is FINAL_SUBMISSION
    UPDATE dbo.projects SET status = 'FINAL_SUBMISSION' WHERE id = 1;

    COMMIT TRANSACTION;
    PRINT 'SUCCESS: Project 1 evaluation data completely reset for demo!';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    DECLARE @errMsg nvarchar(4000) = ERROR_MESSAGE();
    RAISERROR(@errMsg, 16, 1);
END CATCH;
