SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
BEGIN TRY
    -- 0. Ensure student for AI exists
    DECLARE @studentAiId bigint;
    IF NOT EXISTS (SELECT 1 FROM dbo.users WHERE email = N'member2.team3@fpt.edu.vn')
    BEGIN
        INSERT INTO dbo.users (email, password_hash, full_name, phone, student_code, status, access_failed_count, created_at, updated_at, department_id, major_id, academic_profile_status, google_enrollment_pending)
        VALUES (N'member2.team3@fpt.edu.vn', 'AQAAAAIAAYagAAAAECDlj2anj0PYyt+p+4Y/ZoHOJK1yaPX5R0QW/kB8Q+7+HZfcCfIn2WbJH4rtYP+2sg==', N'Vũ Đình Khang', '0912345678', 'SE180999', 'ACTIVE', 0, GETUTCDATE(), GETUTCDATE(), 1, 2, 'VERIFIED', 0);
        SET @studentAiId = SCOPE_IDENTITY();
        INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@studentAiId, 4);
    END
    ELSE
    BEGIN
        SELECT @studentAiId = id FROM dbo.users WHERE email = N'member2.team3@fpt.edu.vn';
    END;

    -- 1. Create Team if not exists
    DECLARE @newTeamId bigint;
    IF NOT EXISTS (SELECT 1 FROM dbo.teams WHERE code = N'SEP490_G03')
    BEGIN
        INSERT INTO dbo.teams (academic_semester_id, code, name, description, status, created_by, created_at, updated_at)
        VALUES (1, N'SEP490_G03', N'Smart E-Commerce Omnichannel Platform', 
                N'Đồ án liên ngành phát triển nền tảng thương mại điện tử đa kênh và chiến dịch Marketing tích hợp (CNTT và QTKD).', 
                'LOCKED', 51, GETUTCDATE(), GETUTCDATE());
        SET @newTeamId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        SELECT @newTeamId = id FROM dbo.teams WHERE code = N'SEP490_G03';
    END;

    -- 2. Team Members
    IF NOT EXISTS (SELECT 1 FROM dbo.team_members WHERE team_id = @newTeamId)
    BEGIN
        INSERT INTO dbo.team_members (team_id, academic_semester_id, user_id, is_leader, joined_at, created_at, updated_at, membership_generation)
        VALUES 
        (@newTeamId, 1, 51, 1, '2026-09-01 08:00:00', GETUTCDATE(), GETUTCDATE(), NEWID()),
        (@newTeamId, 1, @studentAiId, 0, '2026-09-01 08:05:00', GETUTCDATE(), GETUTCDATE(), NEWID()),
        (@newTeamId, 1, 57, 0, '2026-09-01 08:10:00', GETUTCDATE(), GETUTCDATE(), NEWID()),
        (@newTeamId, 1, 56, 0, '2026-09-01 08:15:00', GETUTCDATE(), GETUTCDATE(), NEWID());
    END;

    -- 3. Team Academic Config
    IF NOT EXISTS (SELECT 1 FROM dbo.team_academic_configurations WHERE team_id = @newTeamId)
    BEGIN
        INSERT INTO dbo.team_academic_configurations (team_id, project_mode, primary_major_id, lead_department_id, concurrency_token)
        VALUES (@newTeamId, 'INTERDISCIPLINARY', NULL, 1, NEWID());
    END;

    -- 4. Project
    DECLARE @newProjectId bigint;
    IF NOT EXISTS (SELECT 1 FROM dbo.projects WHERE code = N'SEP490-FA26-ECOM')
    BEGIN
        INSERT INTO dbo.projects (team_id, code, title, description, objectives, problem_statement, expected_output, status, registered_at, submitted_at, approved_at, created_by, created_at, updated_at, proposal_source, milestones_initialized)
        VALUES (@newTeamId, N'SEP490-FA26-ECOM', 
                N'Smart E-Commerce and Omnichannel Customer Engagement Platform', 
                N'Hệ thống thương mại điện tử đa kênh tích hợp AI đề xuất sản phẩm và chiến dịch Marketing cá nhân hóa cho doanh nghiệp bán lẻ.', 
                N'Xây dựng kiến trúc hệ thống TMĐT phân tán, mô hình AI recommendation và kế hoạch truyền thông, marketing đa kênh.', 
                N'Doanh nghiệp bán lẻ gặp khó khăn trong việc đồng bộ dữ liệu và giữ chân khách hàng trên các kênh trực tuyến.', 
                N'Bộ mã nguồn nền tảng E-Commerce, mô hình AI gợi ý sản phẩm, tài liệu kế hoạch Marketing và báo cáo thẩm định.', 
                'FINAL_SUBMISSION', '2026-09-01 08:00:00', '2026-09-05 10:00:00', '2026-09-10 14:00:00', 51, GETUTCDATE(), GETUTCDATE(), 'STUDENT_PROPOSAL', 1);
        SET @newProjectId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        SELECT @newProjectId = id FROM dbo.projects WHERE code = N'SEP490-FA26-ECOM';
    END;

    -- 5. Project Majors
    IF NOT EXISTS (SELECT 1 FROM dbo.project_majors WHERE project_id = @newProjectId)
    BEGIN
        INSERT INTO dbo.project_majors (project_id, major_id, created_at)
        VALUES 
        (@newProjectId, 1, GETUTCDATE()),
        (@newProjectId, 2, GETUTCDATE()),
        (@newProjectId, 3, GETUTCDATE()),
        (@newProjectId, 5, GETUTCDATE());
    END;

    -- 6. Supervisor
    IF NOT EXISTS (SELECT 1 FROM dbo.supervisor_assignments WHERE project_id = @newProjectId)
    BEGIN
        DECLARE @newRequestId bigint;
        INSERT INTO dbo.supervisor_requests (project_id, supervisor_profile_id, requested_by, status, request_message, response_message, requested_at, responded_at, created_at, updated_at, assignment_type)
        VALUES (@newProjectId, 15, 51, 'ACCEPTED', N'Kính gửi thầy hướng dẫn đồ án E-Commerce', N'Thầy đồng ý hướng dẫn nhóm.', '2026-09-01 09:00:00', '2026-09-02 10:00:00', GETUTCDATE(), GETUTCDATE(), 'PRIMARY');
        SET @newRequestId = SCOPE_IDENTITY();

        INSERT INTO dbo.supervisor_assignments (project_id, supervisor_profile_id, supervisor_request_id, is_primary, assigned_at, created_at, updated_at, assignment_type)
        VALUES (@newProjectId, 15, @newRequestId, 1, '2026-09-02 10:00:00', GETUTCDATE(), GETUTCDATE(), 'PRIMARY');
    END;

    -- 7. Deliverable
    DECLARE @newDeliverableId bigint;
    DECLARE @newDeliverableVersionId bigint;
    IF NOT EXISTS (SELECT 1 FROM dbo.deliverables WHERE project_id = @newProjectId)
    BEGIN
        INSERT INTO dbo.deliverables (project_id, milestone_id, title, description, deliverable_type, due_at, status, created_by, created_at, updated_at)
        VALUES (@newProjectId, NULL, N'Final Project Package - E-Commerce', N'Hồ sơ bàn giao mã nguồn, tài liệu kỹ thuật và chiến lược marketing cuối kỳ', 'FINAL_REPORT', '2026-10-31 23:59:59', 'ACCEPTED', 51, GETUTCDATE(), GETUTCDATE());
        SET @newDeliverableId = SCOPE_IDENTITY();

        INSERT INTO dbo.deliverable_versions (deliverable_id, version_number, submitted_by, status, submitted_at, created_at, updated_at)
        VALUES (@newDeliverableId, 1, 51, 'ACCEPTED', GETUTCDATE(), GETUTCDATE(), GETUTCDATE());
        SET @newDeliverableVersionId = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        SELECT @newDeliverableId = id FROM dbo.deliverables WHERE project_id = @newProjectId;
        SELECT @newDeliverableVersionId = id FROM dbo.deliverable_versions WHERE deliverable_id = @newDeliverableId;
    END;

    -- 8. Final Submission
    DECLARE @newSubmissionId bigint;
    IF NOT EXISTS (SELECT 1 FROM dbo.final_submissions WHERE project_id = @newProjectId)
    BEGIN
        INSERT INTO dbo.final_submissions (project_id, project_period_id, submitted_by, submitted_at, deadline, notes, draft_concurrency_token, requirements_concurrency_token)
        VALUES (@newProjectId, 8, 51, '2026-10-14 10:00:00', '2026-10-31 23:59:59', N'Hồ sơ bàn giao cuối kỳ đồ án E-Commerce đa ngành đã hoàn thiện và khóa.', NEWID(), NEWID());
        SET @newSubmissionId = SCOPE_IDENTITY();

        INSERT INTO dbo.final_submission_items (submission_id, deliverable_version_id, deliverable_id, title, version_number, status_at_submission, was_required, files_json)
        VALUES (@newSubmissionId, @newDeliverableVersionId, @newDeliverableId, N'Final Project Package - E-Commerce', 1, N'ACCEPTED', 1, N'[{"id":1,"fileName":"ecom_final_report.pdf","contentType":"application/pdf","sizeBytes":2048576,"reportType":"Final Report"}]');
    END;

    -- 9. Registration Snapshot
    IF NOT EXISTS (SELECT 1 FROM dbo.project_registration_snapshots WHERE project_id = @newProjectId)
    BEGIN
        DECLARE @snapshotJson nvarchar(max) = N'{"Scope":{"ProjectMode":"INTERDISCIPLINARY","PrimaryMajorId":null,"LeadDepartmentId":1,"Requirements":[{"MajorId":1,"MinMembers":1,"MaxMembers":5,"Responsibility":"Software Engineering and Architecture"},{"MajorId":2,"MinMembers":1,"MaxMembers":5,"Responsibility":"AI Recommendation"},{"MajorId":3,"MinMembers":1,"MaxMembers":5,"Responsibility":"Data Analytics and Reporting"},{"MajorId":5,"MinMembers":1,"MaxMembers":5,"Responsibility":"Digital Marketing and Growth"}],"ConcurrencyToken":"b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e"},"Policy":{"MinMembers":3,"MaxMembers":5,"MinDistinctMajors":2,"Version":"v-1-3-5-2"},"OrganizationId":1,"WindowStartAt":"2026-08-01T00:00:00","WindowEndAt":"2026-12-31T23:59:59","Members":[{"UserId":51,"FullName":"Phạm Hoàng Dũng","MajorId":1,"IsLeader":true},{"UserId":' + CAST(@studentAiId AS nvarchar(20)) + N',"FullName":"Vũ Đình Khang","MajorId":2,"IsLeader":false},{"UserId":57,"FullName":"Trần Quốc Cường","MajorId":3,"IsLeader":false},{"UserId":56,"FullName":"Đỗ Minh Hạnh","MajorId":5,"IsLeader":false}],"DepartmentIds":[1,2],"PolicyVersion":1,"AllowedProjectModes":"SINGLE_MAJOR,INTERDISCIPLINARY","AllowedProposalSources":"PUBLISHED_TOPIC,STUDENT_PROPOSAL","ProposalSource":"STUDENT_PROPOSAL","MajorDepartmentIds":{"1":1,"2":1,"3":1,"5":2}}';

        INSERT INTO dbo.project_registration_snapshots (project_id, project_period_id, lead_department_id, submitted_by, submitted_at, snapshot_json)
        VALUES (@newProjectId, 1, 1, 51, '2026-09-01 14:00:00', @snapshotJson);
    END;

    -- 10. Make sure Rubric 4 (SEP490_FA26_MKT) is PUBLISHED so that the user can immediately select it for the Marketing component!
    UPDATE dbo.rubric_versions SET status = 'PUBLISHED' WHERE rubric_id = 4;

    COMMIT TRANSACTION;
    PRINT 'SUCCESS: Created Interdisciplinary project with ID = ' + CAST(@newProjectId AS nvarchar(20));
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    DECLARE @errMsg nvarchar(4000) = ERROR_MESSAGE();
    RAISERROR(@errMsg, 16, 1);
END CATCH;
