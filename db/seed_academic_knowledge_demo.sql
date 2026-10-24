/* Safe demo seed. It creates global, non-sensitive guidance only. */
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @actor bigint = (SELECT TOP (1) u.id FROM dbo.users u JOIN dbo.user_roles ur ON ur.user_id=u.id JOIN dbo.roles r ON r.id=ur.role_id WHERE r.code='ADMIN' AND u.status='ACTIVE' ORDER BY u.id);
IF @actor IS NULL THROW 51000, 'An active admin is required for the academic knowledge demo seed.', 1;

DECLARE @semester bigint = (SELECT TOP(1) id FROM dbo.academic_semesters WHERE status='ACTIVE' AND start_date <= CONVERT(date,SYSUTCDATETIME()) AND end_date >= CONVERT(date,SYSUTCDATETIME()) ORDER BY start_date DESC,id);
IF @semester IS NULL THROW 51000, 'An active semester is required for the demo seed.', 1;

DECLARE @docs TABLE(title nvarchar(255), document_type varchar(50), content nvarchar(max));
INSERT @docs VALUES
(N'[DEMO] Quy trình đồ án tốt nghiệp', 'DEMO', N'Giai đoạn đăng ký: sinh viên tạo nhóm, hoàn thiện hồ sơ và gửi đề tài. Khoa kiểm tra scope ngành và phê duyệt. Giai đoạn thực hiện: nhóm theo dõi task, mốc, báo cáo tiến độ và minh chứng. Giai đoạn bàn giao: sinh viên nộp đúng hạng mục, khóa phiên bản file, sau đó evaluator chấm theo scheme đã công bố.'),
(N'[DEMO] Hướng dẫn rubric và công bố kết quả', 'DEMO', N'Rubric là bộ tiêu chí đánh giá. Scheme là phương án gắn rubric với cấu phần và trọng số của một project. Chỉ rubric và scheme đã publish/được phê duyệt theo policy mới được dùng cho công bố chính thức. AI chỉ giải thích nội dung và không quyết định điểm.'),
(N'[DEMO] Checklist biểu mẫu và hạn học vụ', 'DEMO', N'Sinh viên cần kiểm tra kỳ đồ án, deadline, checklist bàn giao, minh chứng theo target COMMON/MAJOR_SPECIFIC/INDIVIDUAL và trạng thái khóa file trước khi gửi. Nếu hệ thống báo thiếu điều kiện, liên hệ Khoa phụ trách để được xác nhận.');

UPDATE @docs SET content=N'Tài liệu minh họa dùng để kiểm thử trợ lý. Không phải quy định học vụ đã được phê duyệt. ' + content;

DECLARE @title nvarchar(255), @type varchar(50), @content nvarchar(max), @doc bigint, @version bigint;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT title, document_type, content FROM @docs;
OPEN c; FETCH NEXT FROM c INTO @title,@type,@content;
WHILE @@FETCH_STATUS=0
BEGIN
 IF NOT EXISTS (SELECT 1 FROM dbo.academic_knowledge_documents WHERE title=@title AND department_id IS NULL AND academic_semester_id=@semester)
 BEGIN
   INSERT dbo.academic_knowledge_documents(title,document_type,department_id,academic_semester_id,status,current_version,created_by,created_at,updated_at) VALUES(@title,@type,NULL,@semester,'PUBLISHED',N'DEMO-2026.1',@actor,SYSUTCDATETIME(),SYSUTCDATETIME());
   SET @doc=SCOPE_IDENTITY();
   INSERT dbo.academic_knowledge_document_versions(document_id,version,status,content,source_file_name,source_checksum,effective_from,created_by,published_by,created_at,published_at) VALUES(@doc,N'DEMO-2026.1','PUBLISHED',@content,N'demo-academic-guidance.txt',CONVERT(varchar(128),HASHBYTES('SHA2_256',CONVERT(varbinary(max),@content)),2),SYSUTCDATETIME(),@actor,@actor,SYSUTCDATETIME(),SYSUTCDATETIME());
   SET @version=SCOPE_IDENTITY();
   INSERT dbo.academic_knowledge_publications(document_id,document_version_id,actor_id,action,occurred_at) VALUES(@doc,@version,@actor,'PUBLISHED',SYSUTCDATETIME());
   INSERT dbo.academic_knowledge_chunks(document_version_id,ordinal,heading,page_number,content,search_text) VALUES(@version,0,N'Nội dung chính',NULL,@content,LOWER(@content));
   INSERT dbo.academic_knowledge_index_jobs(document_version_id,status,created_at,completed_at) VALUES(@version,'COMPLETED',SYSUTCDATETIME(),SYSUTCDATETIME());
   INSERT dbo.audit_logs(actor_user_id,action,entity_type,entity_id,outcome,details_json,occurred_at)
    VALUES(@actor,'ACADEMIC_KNOWLEDGE_DEMO_SEEDED','KNOWLEDGE_DOCUMENT',CONVERT(nvarchar(40),@doc),'SUCCESS',N'{"demo":true,"academicApproval":false}',SYSUTCDATETIME());
 END;
 FETCH NEXT FROM c INTO @title,@type,@content;
END
CLOSE c; DEALLOCATE c;
COMMIT;
GO
