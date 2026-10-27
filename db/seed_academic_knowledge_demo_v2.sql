/* Additive demo knowledge refresh for the academic assistant.
   Safe to rerun: the same version is inserted at most once per document. */
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @actor bigint = (
    SELECT TOP (1) u.id
    FROM dbo.users u
    JOIN dbo.user_roles ur ON ur.user_id = u.id
    JOIN dbo.roles r ON r.id = ur.role_id
    WHERE r.code = 'ADMIN' AND u.status = 'ACTIVE'
    ORDER BY u.id
);
IF @actor IS NULL THROW 51000, 'An active admin is required for the academic knowledge demo seed.', 1;

DECLARE @semester bigint = (
    SELECT TOP (1) id
    FROM dbo.academic_semesters
    WHERE status = 'ACTIVE'
      AND start_date <= CONVERT(date, SYSUTCDATETIME())
      AND end_date >= CONVERT(date, SYSUTCDATETIME())
    ORDER BY start_date DESC, id DESC
);
IF @semester IS NULL THROW 51000, 'An active semester is required for the demo seed.', 1;

DECLARE @version nvarchar(64) = N'DEMO-2026.2';
DECLARE @docs TABLE (title nvarchar(255) NOT NULL, content nvarchar(max) NOT NULL);
INSERT @docs(title, content) VALUES
(N'[DEMO] Hướng dẫn rubric và công bố kết quả',
 N'Tài liệu minh họa dùng để kiểm thử trợ lý, không phải quy định học vụ đã được phê duyệt.

Rubric IT – StudySpace gồm các tiêu chí: (1) Kiến trúc và triển khai: thiết kế module, API, cơ sở dữ liệu, triển khai staging; (2) Chất lượng mã nguồn: cấu trúc code, quy ước, xử lý lỗi, bảo mật; (3) Kiểm thử và bảo mật: unit/integration test, phân quyền, kiểm tra dữ liệu đầu vào và xử lý lỗi.
Rubric Marketing – StudySpace gồm: (1) Phân tích thị trường: phân khúc, đối thủ, nhu cầu; (2) Mức độ phù hợp nhu cầu: chân dung người dùng, bằng chứng khảo sát, insight; (3) Kế hoạch triển khai: thông điệp, kênh, KPI và lộ trình.
Rubric Design – StudySpace gồm: (1) Nghiên cứu người dùng: phương pháp, phát hiện và vấn đề ưu tiên; (2) Luồng trải nghiệm: user flow, kiến trúc thông tin, khả năng tiếp cận; (3) Chất lượng giao diện: hệ thống visual, tính nhất quán, responsive và prototype.
Rubric là bộ tiêu chí. Scheme là phương án gắn rubric với cấu phần và trọng số của project. Bản demo có thể dùng công thức sinh viên 50% COMMON + 30% MAJOR_SPECIFIC + 20% INDIVIDUAL; công thức project 50% COMMON + 50% MAJOR aggregate. Chỉ rubric/scheme đã publish và được policy cho phép mới dùng để công bố chính thức. AI chỉ giải thích, không quyết định điểm.'),
(N'[DEMO] Checklist biểu mẫu và hạn học vụ',
 N'Tài liệu minh họa dùng để kiểm thử trợ lý, không phải lịch học vụ đã được phê duyệt.

Đối với kỳ demo DST-20261024, mốc tham khảo: hoàn tất đăng ký và chốt scope trước 2026-10-10; chốt giảng viên hướng dẫn trước 2026-10-15; cập nhật báo cáo tiến độ và minh chứng trước 2026-11-10; khóa phiên bản minh chứng trước 2026-11-20; nộp bộ bàn giao/COLD trước 2026-11-25; evaluator hoàn tất chấm trước 2026-11-28; chỉ công bố kết quả sau khi đủ policy và assignment.
Biểu mẫu/hạng mục cần kiểm tra gồm: thông tin nhóm và scope ngành; working agreement; kế hoạch task/sprint; báo cáo tiến độ; minh chứng COMMON; minh chứng MAJOR_SPECIFIC theo từng ngành; minh chứng INDIVIDUAL theo từng sinh viên; checklist COLD và bộ báo cáo cuối kỳ; rubric và evaluation scheme đã publish.
Trước khi gửi, kiểm tra đúng target, đúng ngành/cá nhân, file có phiên bản, quyền truy cập còn hiệu lực và trạng thái khóa minh chứng. Nếu hệ thống báo thiếu điều kiện hoặc mốc đã qua, đây chỉ là dữ liệu DEMO; cần đối chiếu thông báo chính thức của Khoa.' );

DECLARE @title nvarchar(255), @content nvarchar(max), @doc bigint, @oldVersion bigint, @newVersion bigint;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT title, content FROM @docs;
OPEN c; FETCH NEXT FROM c INTO @title, @content;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @doc = id FROM dbo.academic_knowledge_documents
    WHERE title = @title AND department_id IS NULL AND academic_semester_id = @semester;

    IF @doc IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM dbo.academic_knowledge_document_versions
        WHERE document_id = @doc AND version = @version
    )
    BEGIN
        UPDATE dbo.academic_knowledge_document_versions
        SET status = 'RETIRED'
        WHERE document_id = @doc AND status = 'PUBLISHED';

        INSERT dbo.academic_knowledge_document_versions
            (document_id, version, status, content, source_file_name, source_checksum,
             effective_from, created_by, published_by, created_at, published_at)
        VALUES
            (@doc, @version, 'PUBLISHED', @content, N'demo-academic-guidance-v2.txt',
             CONVERT(varchar(128), HASHBYTES('SHA2_256', CONVERT(varbinary(max), @content)), 2),
             SYSUTCDATETIME(), @actor, @actor, SYSUTCDATETIME(), SYSUTCDATETIME());
        SET @newVersion = SCOPE_IDENTITY();

        INSERT dbo.academic_knowledge_publications(document_id, document_version_id, actor_id, action, occurred_at)
        VALUES (@doc, @newVersion, @actor, 'PUBLISHED', SYSUTCDATETIME());
        INSERT dbo.academic_knowledge_chunks(document_version_id, ordinal, heading, page_number, content, search_text)
        VALUES (@newVersion, 0, N'Nội dung chính', NULL, @content, LOWER(@content));
        INSERT dbo.academic_knowledge_index_jobs(document_version_id, status, created_at, completed_at)
        VALUES (@newVersion, 'COMPLETED', SYSUTCDATETIME(), SYSUTCDATETIME());
        UPDATE dbo.academic_knowledge_documents
        SET status = 'PUBLISHED', current_version = @version, updated_at = SYSUTCDATETIME()
        WHERE id = @doc;
        INSERT dbo.audit_logs(actor_user_id, action, entity_type, entity_id, outcome, details_json, occurred_at)
        VALUES (@actor, 'ACADEMIC_KNOWLEDGE_DEMO_REFRESHED', 'KNOWLEDGE_DOCUMENT', CONVERT(nvarchar(40), @doc),
                'SUCCESS', N'{"demo":true,"academicApproval":false,"version":"DEMO-2026.2"}', SYSUTCDATETIME());
    END;
    FETCH NEXT FROM c INTO @title, @content;
END;
CLOSE c; DEALLOCATE c;
COMMIT;
GO
