[CmdletBinding()]
param(
    [string]$ConnectionString = $env:AIPMS_TEST_SQL_CONNECTION,
    [Parameter(Mandatory = $true)][string]$DatabaseName
)

$ErrorActionPreference = 'Stop'
if ($DatabaseName -cnotmatch '^AI_PMS_E2E_[a-f0-9]{32}$') {
    throw 'Only an owned AI_PMS_E2E_<guid> database may be prepared.'
}
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'Set AIPMS_TEST_SQL_CONNECTION privately before running this script.'
}

$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($ConnectionString)
$builder['Initial Catalog'] = $DatabaseName
$builder['Pooling'] = $false
$connection = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)

function Invoke-Sql([System.Data.SqlClient.SqlConnection]$Connection, [string]$Sql) {
    $command = $Connection.CreateCommand()
    try {
        $command.CommandText = $Sql
        $command.CommandTimeout = 120
        [void]$command.ExecuteNonQuery()
    }
    finally { $command.Dispose() }
}

try {
    $connection.Open()
    $ownerCommand = $connection.CreateCommand()
    try {
        $ownerCommand.CommandText = "SELECT CONVERT(nvarchar(100), value) FROM sys.extended_properties WHERE class=0 AND name=N'AIPMS_E2E_OWNER'"
        if ($ownerCommand.ExecuteScalar() -ne 'remediation-v1') {
            throw 'The database ownership marker is missing; refusing mutation.'
        }
    }
    finally { $ownerCommand.Dispose() }

    Invoke-Sql $connection @'
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
DECLARE @now datetime2(0)=SYSUTCDATETIME();
DECLARE @org bigint=(SELECT id FROM dbo.organizations WHERE code=N'E2E');
DECLARE @semester bigint=(SELECT id FROM dbo.academic_semesters WHERE organization_id=@org AND code=N'E2E-2026');
DECLARE @evalPeriod bigint=(SELECT id FROM dbo.project_periods WHERE academic_semester_id=@semester AND code=N'E2E-EVAL');
DECLARE @singleProject bigint=(SELECT id FROM dbo.projects WHERE code=N'E2E-SINGLE-P');
DECLARE @interProject bigint=(SELECT id FROM dbo.projects WHERE code=N'E2E-INTER-P');
DECLARE @singleTeam bigint=(SELECT id FROM dbo.teams WHERE code=N'E2E-SINGLE');
DECLARE @interTeam bigint=(SELECT id FROM dbo.teams WHERE code=N'E2E-INTER');
DECLARE @singleMajor bigint=(SELECT id FROM dbo.majors WHERE code=N'E2E-SE');
DECLARE @interMajor bigint=(SELECT id FROM dbo.majors WHERE code=N'E2E-DS');
DECLARE @leadDept bigint=(SELECT id FROM dbo.departments WHERE code=N'E2E-CS');
DECLARE @otherDept bigint=(SELECT id FROM dbo.departments WHERE code=N'E2E-DS');
DECLARE @singleLeader bigint=(SELECT id FROM dbo.users WHERE email=N'single.leader@e2e.invalid');
DECLARE @singleMember bigint=(SELECT id FROM dbo.users WHERE email=N'single.member@e2e.invalid');
DECLARE @singleThird bigint=(SELECT id FROM dbo.users WHERE email=N'single.third@e2e.invalid');
DECLARE @interLeader bigint=(SELECT id FROM dbo.users WHERE email=N'leader@e2e.invalid');
DECLARE @interMember bigint=(SELECT id FROM dbo.users WHERE email=N'member@e2e.invalid');
DECLARE @interThird bigint=(SELECT id FROM dbo.users WHERE email=N'inter.third@e2e.invalid');
DECLARE @staffCs bigint=(SELECT id FROM dbo.users WHERE email=N'staff.cs@e2e.invalid');
DECLARE @staffDs bigint=(SELECT id FROM dbo.users WHERE email=N'staff.ds@e2e.invalid');
DECLARE @supervisorProfile bigint=(SELECT id FROM dbo.supervisor_profiles WHERE user_id=(SELECT id FROM dbo.users WHERE email=N'supervisor@e2e.invalid'));
DECLARE @mentorProfile bigint=(SELECT id FROM dbo.supervisor_profiles WHERE user_id=(SELECT id FROM dbo.users WHERE email=N'mentor.ds@e2e.invalid'));
IF @singleProject IS NULL OR @interProject IS NULL OR @singleTeam IS NULL OR @interTeam IS NULL THROW 51000,'Base E2E projects are missing.',1;

DECLARE @singleToken uniqueidentifier=NEWID(), @interToken uniqueidentifier=NEWID();
DECLARE @sLd nvarchar(20) = CONVERT(nvarchar(20), @leadDept);
DECLARE @sOd nvarchar(20) = CONVERT(nvarchar(20), @otherDept);
DECLARE @sSm nvarchar(20) = CONVERT(nvarchar(20), @singleMajor);
DECLARE @sIm nvarchar(20) = CONVERT(nvarchar(20), @interMajor);
DECLARE @sSl nvarchar(20) = CONVERT(nvarchar(20), @singleLeader);
DECLARE @sSmemb nvarchar(20) = CONVERT(nvarchar(20), @singleMember);
DECLARE @sSt nvarchar(20) = CONVERT(nvarchar(20), @singleThird);
DECLARE @sIl nvarchar(20) = CONVERT(nvarchar(20), @interLeader);
DECLARE @sImemb nvarchar(20) = CONVERT(nvarchar(20), @interMember);
DECLARE @sIt nvarchar(20) = CONVERT(nvarchar(20), @interThird);
DECLARE @sOrg nvarchar(20) = CONVERT(nvarchar(20), @org);

DECLARE @singleJson nvarchar(max)=N'{
  "Scope":{"ProjectMode":"SINGLE_MAJOR","PrimaryMajorId":'+@sSm+',"LeadDepartmentId":'+@sLd+',"Requirements":[{"MajorId":'+@sSm+',"MinMembers":1,"MaxMembers":5,"Responsibility":"Software engineering"}],"ConcurrencyToken":"'+CONVERT(nvarchar(36),@singleToken)+'"},
  "Policy":{"MinMembers":3,"MaxMembers":5,"MinDistinctMajors":1,"Version":"E2E-2026"},"OrganizationId":'+@sOrg+',"WindowStartAt":"2026-01-01T00:00:00Z","WindowEndAt":"2026-12-31T23:59:59Z",
  "Members":[{"UserId":'+@sSl+',"FullName":"E2E Single Leader","MajorId":'+@sSm+',"IsLeader":true},{"UserId":'+@sSmemb+',"FullName":"E2E Single Member","MajorId":'+@sSm+',"IsLeader":false},{"UserId":'+@sSt+',"FullName":"E2E Single Third","MajorId":'+@sSm+',"IsLeader":false}],
  "DepartmentIds":['+@sLd+'],"PolicyVersion":1,"AllowedProjectModes":"SINGLE_MAJOR,INTERDISCIPLINARY","AllowedProposalSources":"PUBLISHED_TOPIC,STUDENT_PROPOSAL","ProposalSource":"STUDENT_PROPOSAL","MajorDepartmentIds":{"'+@sSm+'":'+@sLd+'},"ProjectRequirements":[{"MajorId":'+@sSm+',"MinMembers":1,"MaxMembers":5,"Responsibility":"Software engineering"}],"TeamResponsibilities":[]}' ;
DECLARE @interJson nvarchar(max)=N'{
  "Scope":{"ProjectMode":"INTERDISCIPLINARY","PrimaryMajorId":null,"LeadDepartmentId":'+@sLd+',"Requirements":[{"MajorId":'+@sSm+',"MinMembers":1,"MaxMembers":5,"Responsibility":"Software engineering"},{"MajorId":'+@sIm+',"MinMembers":1,"MaxMembers":5,"Responsibility":"Data science"}],"ConcurrencyToken":"'+CONVERT(nvarchar(36),@interToken)+'"},
  "Policy":{"MinMembers":3,"MaxMembers":5,"MinDistinctMajors":2,"Version":"E2E-2026"},"OrganizationId":'+@sOrg+',"WindowStartAt":"2026-01-01T00:00:00Z","WindowEndAt":"2026-12-31T23:59:59Z",
  "Members":[{"UserId":'+@sIl+',"FullName":"E2E Student Leader","MajorId":'+@sSm+',"IsLeader":true},{"UserId":'+@sImemb+',"FullName":"E2E Student Member","MajorId":'+@sIm+',"IsLeader":false},{"UserId":'+@sIt+',"FullName":"E2E Inter Third","MajorId":'+@sSm+',"IsLeader":false}],
  "DepartmentIds":['+@sLd+','+@sOd+'],"PolicyVersion":1,"AllowedProjectModes":"SINGLE_MAJOR,INTERDISCIPLINARY","AllowedProposalSources":"PUBLISHED_TOPIC,STUDENT_PROPOSAL","ProposalSource":"STUDENT_PROPOSAL","MajorDepartmentIds":{"'+@sSm+'":'+@sLd+',"'+@sIm+'":'+@sOd+'},"ProjectRequirements":[{"MajorId":'+@sSm+',"MinMembers":1,"MaxMembers":5,"Responsibility":"Software engineering"},{"MajorId":'+@sIm+',"MinMembers":1,"MaxMembers":5,"Responsibility":"Data science"}],"TeamResponsibilities":[]}' ;

INSERT dbo.project_registration_snapshots(project_id,project_period_id,lead_department_id,submitted_by,submitted_at,snapshot_json)
SELECT @singleProject,@evalPeriod,@leadDept,@singleLeader,@now,@singleJson
WHERE NOT EXISTS (SELECT 1 FROM dbo.project_registration_snapshots WHERE project_id=@singleProject);
INSERT dbo.project_registration_snapshots(project_id,project_period_id,lead_department_id,submitted_by,submitted_at,snapshot_json)
SELECT @interProject,@evalPeriod,@leadDept,@interLeader,@now,@interJson
WHERE NOT EXISTS (SELECT 1 FROM dbo.project_registration_snapshots WHERE project_id=@interProject);
DECLARE @singleSnapshot bigint=(SELECT TOP(1) id FROM dbo.project_registration_snapshots WHERE project_id=@singleProject ORDER BY id DESC);
DECLARE @interSnapshot bigint=(SELECT TOP(1) id FROM dbo.project_registration_snapshots WHERE project_id=@interProject ORDER BY id DESC);
INSERT dbo.project_department_decisions(snapshot_id,department_id,decision,decided_by,decided_at,reason)
SELECT @singleSnapshot,@leadDept,N'APPROVED',@staffCs,@now,N'Demo approval' WHERE NOT EXISTS (SELECT 1 FROM dbo.project_department_decisions WHERE snapshot_id=@singleSnapshot AND department_id=@leadDept);
INSERT dbo.project_department_decisions(snapshot_id,department_id,decision,decided_by,decided_at,reason)
SELECT @interSnapshot,@leadDept,N'APPROVED',@staffCs,@now,N'Demo lead approval' WHERE NOT EXISTS (SELECT 1 FROM dbo.project_department_decisions WHERE snapshot_id=@interSnapshot AND department_id=@leadDept);
INSERT dbo.project_department_decisions(snapshot_id,department_id,decision,decided_by,decided_at,reason)
SELECT @interSnapshot,@otherDept,N'APPROVED',@staffDs,@now,N'Demo participating approval' WHERE NOT EXISTS (SELECT 1 FROM dbo.project_department_decisions WHERE snapshot_id=@interSnapshot AND department_id=@otherDept);

UPDATE dbo.teams SET status=N'LOCKED',updated_at=@now WHERE id IN (@singleTeam,@interTeam) AND status=N'FORMING';
UPDATE dbo.projects SET status=N'ACTIVE',submitted_at=COALESCE(submitted_at,@now),approved_at=COALESCE(approved_at,@now),updated_at=@now
WHERE id IN (@singleProject,@interProject) AND status=N'DRAFT';
INSERT dbo.project_status_history(project_id,old_status,new_status,changed_by,reason,changed_at)
SELECT @singleProject,N'DRAFT',N'ACTIVE',@staffCs,N'Prepared isolated v5 demo fixture',@now
WHERE NOT EXISTS (SELECT 1 FROM dbo.project_status_history WHERE project_id=@singleProject AND new_status=N'ACTIVE');
INSERT dbo.project_status_history(project_id,old_status,new_status,changed_by,reason,changed_at)
SELECT @interProject,N'DRAFT',N'ACTIVE',@staffCs,N'Prepared isolated v5 demo fixture',@now
WHERE NOT EXISTS (SELECT 1 FROM dbo.project_status_history WHERE project_id=@interProject AND new_status=N'ACTIVE');

DECLARE @singleRequest bigint=(SELECT TOP(1) id FROM dbo.supervisor_requests WHERE project_id=@singleProject AND supervisor_profile_id=@supervisorProfile ORDER BY id DESC);
IF @singleRequest IS NULL BEGIN INSERT dbo.supervisor_requests(project_id,supervisor_profile_id,requested_by,assignment_type,status,response_message,requested_at,responded_at,created_at,updated_at) VALUES(@singleProject,@supervisorProfile,@singleLeader,'PRIMARY',N'ACCEPTED',N'Demo accepted',@now,@now,@now,@now); SET @singleRequest=SCOPE_IDENTITY(); END;
IF NOT EXISTS (SELECT 1 FROM dbo.supervisor_assignments WHERE project_id=@singleProject AND ended_at IS NULL AND is_primary=1) INSERT dbo.supervisor_assignments(project_id,supervisor_profile_id,supervisor_request_id,is_primary,assignment_type,assigned_by,assigned_at,created_at,updated_at) VALUES(@singleProject,@supervisorProfile,@singleRequest,1,'PRIMARY',@staffCs,@now,@now,@now);
DECLARE @interRequest bigint=(SELECT TOP(1) id FROM dbo.supervisor_requests WHERE project_id=@interProject AND supervisor_profile_id=@supervisorProfile ORDER BY id DESC);
IF @interRequest IS NULL BEGIN INSERT dbo.supervisor_requests(project_id,supervisor_profile_id,requested_by,assignment_type,status,response_message,requested_at,responded_at,created_at,updated_at) VALUES(@interProject,@supervisorProfile,@interLeader,'PRIMARY',N'ACCEPTED',N'Demo accepted',@now,@now,@now,@now); SET @interRequest=SCOPE_IDENTITY(); END;
IF NOT EXISTS (SELECT 1 FROM dbo.supervisor_assignments WHERE project_id=@interProject AND ended_at IS NULL AND is_primary=1) INSERT dbo.supervisor_assignments(project_id,supervisor_profile_id,supervisor_request_id,is_primary,assignment_type,assigned_by,assigned_at,created_at,updated_at) VALUES(@interProject,@supervisorProfile,@interRequest,1,'PRIMARY',@staffCs,@now,@now,@now);
DECLARE @mentorRequest bigint=(SELECT TOP(1) id FROM dbo.supervisor_requests WHERE project_id=@interProject AND supervisor_profile_id=@mentorProfile AND assignment_type='DISCIPLINE_MENTOR' ORDER BY id DESC);
IF @mentorRequest IS NULL BEGIN INSERT dbo.supervisor_requests(project_id,supervisor_profile_id,requested_by,assignment_type,major_id,status,response_message,requested_at,responded_at,created_at,updated_at) VALUES(@interProject,@mentorProfile,@interLeader,'DISCIPLINE_MENTOR',@interMajor,N'ACCEPTED',N'Demo accepted',@now,@now,@now,@now); SET @mentorRequest=SCOPE_IDENTITY(); END;
IF NOT EXISTS (SELECT 1 FROM dbo.supervisor_assignments WHERE project_id=@interProject AND ended_at IS NULL AND assignment_type='DISCIPLINE_MENTOR') INSERT dbo.supervisor_assignments(project_id,supervisor_profile_id,supervisor_request_id,is_primary,assignment_type,major_id,assigned_by,assigned_at,created_at,updated_at) VALUES(@interProject,@mentorProfile,@mentorRequest,0,'DISCIPLINE_MENTOR',@interMajor,@staffDs,@now,@now,@now);

DECLARE @singleAgreement bigint=(SELECT TOP(1) id FROM dbo.project_working_agreements WHERE project_id=@singleProject AND status='ACTIVE');
IF @singleAgreement IS NULL BEGIN INSERT dbo.project_working_agreements(project_id,version,status,content_json,concurrency_token,created_by,created_at,published_at) VALUES(@singleProject,1,'ACTIVE',N'{"title":"E2E Single Major Working Agreement","rules":["Weekly update","Respect review deadlines"]}',NEWID(),@singleLeader,@now,@now); SET @singleAgreement=SCOPE_IDENTITY(); END;
DECLARE @interAgreement bigint=(SELECT TOP(1) id FROM dbo.project_working_agreements WHERE project_id=@interProject AND status='ACTIVE');
IF @interAgreement IS NULL BEGIN INSERT dbo.project_working_agreements(project_id,version,status,content_json,concurrency_token,created_by,created_at,published_at) VALUES(@interProject,1,'ACTIVE',N'{"title":"E2E Interdisciplinary Working Agreement","rules":["Share scope updates","Respect department boundaries"]}',NEWID(),@interLeader,@now,@now); SET @interAgreement=SCOPE_IDENTITY(); END;
INSERT dbo.agreement_members(agreement_id,user_id,team_member_id,membership_joined_at,membership_generation,accepted_at)
SELECT @singleAgreement,m.user_id,m.id,m.joined_at,m.membership_generation,@now FROM dbo.team_members m WHERE m.team_id=@singleTeam AND NOT EXISTS (SELECT 1 FROM dbo.agreement_members a WHERE a.agreement_id=@singleAgreement AND a.user_id=m.user_id);
INSERT dbo.agreement_members(agreement_id,user_id,team_member_id,membership_joined_at,membership_generation,accepted_at)
SELECT @interAgreement,m.user_id,m.id,m.joined_at,m.membership_generation,@now FROM dbo.team_members m WHERE m.team_id=@interTeam AND NOT EXISTS (SELECT 1 FROM dbo.agreement_members a WHERE a.agreement_id=@interAgreement AND a.user_id=m.user_id);

DECLARE @singleRound bigint=(SELECT TOP(1) id FROM dbo.peer_evaluation_rounds WHERE project_id=@singleProject AND status='OPEN');
IF @singleRound IS NULL BEGIN INSERT dbo.peer_evaluation_rounds(project_id,title,status,opens_at,closes_at,concurrency_token,created_by,primary_assignment_id,created_at) SELECT @singleProject,N'E2E Single Peer Round','OPEN',DATEADD(day,-1,@now),DATEADD(day,14,@now),NEWID(),@singleLeader,id,@now FROM dbo.supervisor_assignments WHERE project_id=@singleProject AND ended_at IS NULL AND is_primary=1; SET @singleRound=SCOPE_IDENTITY(); END;
DECLARE @interRound bigint=(SELECT TOP(1) id FROM dbo.peer_evaluation_rounds WHERE project_id=@interProject AND status='OPEN');
IF @interRound IS NULL BEGIN INSERT dbo.peer_evaluation_rounds(project_id,title,status,opens_at,closes_at,concurrency_token,created_by,primary_assignment_id,created_at) SELECT @interProject,N'E2E Interdisciplinary Peer Round','OPEN',DATEADD(day,-1,@now),DATEADD(day,14,@now),NEWID(),@interLeader,id,@now FROM dbo.supervisor_assignments WHERE project_id=@interProject AND ended_at IS NULL AND is_primary=1; SET @interRound=SCOPE_IDENTITY(); END;
INSERT dbo.peer_round_members(round_id,user_id,major_id,department_id,team_member_id,membership_generation,membership_joined_at,full_name)
SELECT @singleRound,m.user_id,u.major_id,u.department_id,m.id,m.membership_generation,m.joined_at,u.full_name FROM dbo.team_members m JOIN dbo.users u ON u.id=m.user_id WHERE m.team_id=@singleTeam AND NOT EXISTS (SELECT 1 FROM dbo.peer_round_members r WHERE r.round_id=@singleRound AND r.user_id=m.user_id);
INSERT dbo.peer_round_members(round_id,user_id,major_id,department_id,team_member_id,membership_generation,membership_joined_at,full_name)
SELECT @interRound,m.user_id,u.major_id,u.department_id,m.id,m.membership_generation,m.joined_at,u.full_name FROM dbo.team_members m JOIN dbo.users u ON u.id=m.user_id WHERE m.team_id=@interTeam AND NOT EXISTS (SELECT 1 FROM dbo.peer_round_members r WHERE r.round_id=@interRound AND r.user_id=m.user_id);

DECLARE @singleMilestone bigint=(SELECT TOP(1) id FROM dbo.milestones WHERE project_id=@singleProject);
IF @singleMilestone IS NULL BEGIN INSERT dbo.milestones(project_id,title,description,start_date,due_date,status,sort_order,created_by,created_at,updated_at) VALUES(@singleProject,N'Demo execution',N'Single-major demo milestone',CONVERT(date,@now),DATEADD(day,14,CONVERT(date,@now)),N'IN_PROGRESS',1,@singleLeader,@now,@now); SET @singleMilestone=SCOPE_IDENTITY(); END;
DECLARE @interMilestone bigint=(SELECT TOP(1) id FROM dbo.milestones WHERE project_id=@interProject);
IF @interMilestone IS NULL BEGIN INSERT dbo.milestones(project_id,title,description,start_date,due_date,status,sort_order,created_by,created_at,updated_at) VALUES(@interProject,N'Demo cross-department execution',N'Interdisciplinary demo milestone',CONVERT(date,@now),DATEADD(day,14,CONVERT(date,@now)),N'IN_PROGRESS',1,@interLeader,@now,@now); SET @interMilestone=SCOPE_IDENTITY(); END;
INSERT dbo.tasks(milestone_id,title,description,status,priority,start_at,due_at,created_by,created_at,updated_at,story_points)
SELECT @singleMilestone,N'Prepare single-major presentation',N'Demo task',N'IN_PROGRESS',N'HIGH',@now,DATEADD(day,7,@now),@singleLeader,@now,@now,3 WHERE NOT EXISTS (SELECT 1 FROM dbo.tasks WHERE milestone_id=@singleMilestone AND title=N'Prepare single-major presentation');
INSERT dbo.tasks(milestone_id,title,description,status,priority,start_at,due_at,created_by,created_at,updated_at,story_points)
SELECT @interMilestone,N'Align IT and DS evidence',N'Demo cross-department task',N'IN_PROGRESS',N'HIGH',@now,DATEADD(day,7,@now),@interLeader,@now,@now,5 WHERE NOT EXISTS (SELECT 1 FROM dbo.tasks WHERE milestone_id=@interMilestone AND title=N'Align IT and DS evidence');

/* Three-department V5 fixture: IT + Marketing + Design. */
DECLARE @v5Team bigint=(SELECT id FROM dbo.teams WHERE code=N'V5-CROSS-TEAM');
DECLARE @v5Project bigint=(SELECT id FROM dbo.projects WHERE code=N'V5-INTER-3D');
DECLARE @v5ItMajor bigint=(SELECT id FROM dbo.majors WHERE code=N'V5-IT');
DECLARE @v5MktMajor bigint=(SELECT id FROM dbo.majors WHERE code=N'V5-MKT');
DECLARE @v5DesMajor bigint=(SELECT id FROM dbo.majors WHERE code=N'V5-DES');
DECLARE @v5ItDept bigint=(SELECT id FROM dbo.departments WHERE code=N'V5-IT');
DECLARE @v5MktDept bigint=(SELECT id FROM dbo.departments WHERE code=N'V5-MKT');
DECLARE @v5DesDept bigint=(SELECT id FROM dbo.departments WHERE code=N'V5-DES');
DECLARE @v5Leader bigint=(SELECT id FROM dbo.users WHERE email=N'v5.c.leader@e2e.invalid');
DECLARE @v5MktStudent bigint=(SELECT id FROM dbo.users WHERE email=N'v5.c.marketing@e2e.invalid');
DECLARE @v5DesStudent bigint=(SELECT id FROM dbo.users WHERE email=N'v5.c.design@e2e.invalid');
DECLARE @v5PrimaryProfile bigint=(SELECT id FROM dbo.supervisor_profiles WHERE user_id=(SELECT id FROM dbo.users WHERE email=N'v5.primary@e2e.invalid'));
DECLARE @v5MktMentorProfile bigint=(SELECT id FROM dbo.supervisor_profiles WHERE user_id=(SELECT id FROM dbo.users WHERE email=N'v5.mentor.marketing@e2e.invalid'));
DECLARE @v5DesMentorProfile bigint=(SELECT id FROM dbo.supervisor_profiles WHERE user_id=(SELECT id FROM dbo.users WHERE email=N'v5.mentor.design@e2e.invalid'));
IF @v5Team IS NULL
BEGIN
    INSERT dbo.teams(academic_semester_id,code,name,description,status,created_by,created_at,updated_at)
    VALUES(@semester,N'V5-CROSS-TEAM',N'V5 IT + Marketing + Design',N'Isolated three-department demo',N'LOCKED',@v5Leader,@now,@now);
    SET @v5Team=SCOPE_IDENTITY();
    INSERT dbo.team_members(team_id,academic_semester_id,user_id,is_leader,joined_at,created_at,updated_at)
    SELECT @v5Team,@semester,u.id,CASE WHEN u.id=@v5Leader THEN 1 ELSE 0 END,@now,@now,@now FROM dbo.users u WHERE u.id IN (@v5Leader,@v5MktStudent,@v5DesStudent);
    INSERT dbo.team_academic_configurations(team_id,project_mode,lead_department_id,concurrency_token,responsibility_version)
    VALUES(@v5Team,N'INTERDISCIPLINARY',@v5ItDept,NEWID(),NEWID());
    INSERT dbo.team_major_requirements(team_id,major_id,min_members,max_members,responsibility)
    VALUES(@v5Team,@v5ItMajor,1,2,N'IT implementation'),(@v5Team,@v5MktMajor,1,2,N'Marketing analysis'),(@v5Team,@v5DesMajor,1,2,N'Design prototype');
    INSERT dbo.team_major_responsibilities(team_id,major_id,content,sort_order,created_by)
    VALUES(@v5Team,@v5ItMajor,N'Build the platform',0,@v5Leader),(@v5Team,@v5MktMajor,N'Validate market fit',0,@v5Leader),(@v5Team,@v5DesMajor,N'Design the user experience',0,@v5Leader);
END;
IF @v5Project IS NULL
BEGIN
    INSERT dbo.projects(team_id,code,title,description,objectives,problem_statement,expected_output,status,created_by,created_at,updated_at)
    VALUES(@v5Team,N'V5-INTER-3D',N'AI-PMS Cross Department Demo',N'IT + Marketing + Design demonstration',N'Validate interdisciplinary workflow',N'Cross department capstone',N'Working product and evidence',N'ACTIVE',@v5Leader,@now,@now);
    SET @v5Project=SCOPE_IDENTITY();
    INSERT dbo.project_majors(project_id,major_id) VALUES(@v5Project,@v5ItMajor),(@v5Project,@v5MktMajor),(@v5Project,@v5DesMajor);
    INSERT dbo.project_status_history(project_id,old_status,new_status,changed_by,reason,changed_at) VALUES(@v5Project,NULL,N'ACTIVE',@v5Leader,N'Prepared isolated V5 three-department demo fixture',@now);
END;
DECLARE @v5ScopeToken uniqueidentifier=NEWID();
DECLARE @v5Json nvarchar(max)=N'{"Scope":{"ProjectMode":"INTERDISCIPLINARY","PrimaryMajorId":null,"LeadDepartmentId":'+CONVERT(nvarchar(20),@v5ItDept)+',"Requirements":[{"MajorId":'+CONVERT(nvarchar(20),@v5ItMajor)+',"MinMembers":1,"MaxMembers":2,"Responsibility":"IT implementation"},{"MajorId":'+CONVERT(nvarchar(20),@v5MktMajor)+',"MinMembers":1,"MaxMembers":2,"Responsibility":"Marketing analysis"},{"MajorId":'+CONVERT(nvarchar(20),@v5DesMajor)+',"MinMembers":1,"MaxMembers":2,"Responsibility":"Design prototype"}],"ConcurrencyToken":"'+CONVERT(nvarchar(36),@v5ScopeToken)+'"},"Policy":{"MinMembers":3,"MaxMembers":5,"MinDistinctMajors":3,"Version":"V5-2026"},"OrganizationId":'+CONVERT(nvarchar(20),@org)+',"WindowStartAt":"2026-01-01T00:00:00Z","WindowEndAt":"2026-12-31T23:59:59Z","Members":[{"UserId":'+CONVERT(nvarchar(20),@v5Leader)+',"FullName":"V5 C IT Leader","MajorId":'+CONVERT(nvarchar(20),@v5ItMajor)+',"IsLeader":true},{"UserId":'+CONVERT(nvarchar(20),@v5MktStudent)+',"FullName":"V5 C Marketing Member","MajorId":'+CONVERT(nvarchar(20),@v5MktMajor)+',"IsLeader":false},{"UserId":'+CONVERT(nvarchar(20),@v5DesStudent)+',"FullName":"V5 C Design Member","MajorId":'+CONVERT(nvarchar(20),@v5DesMajor)+',"IsLeader":false}],"DepartmentIds":['+CONVERT(nvarchar(20),@v5ItDept)+','+CONVERT(nvarchar(20),@v5MktDept)+','+CONVERT(nvarchar(20),@v5DesDept)+'],"PolicyVersion":1,"AllowedProjectModes":"SINGLE_MAJOR,INTERDISCIPLINARY","AllowedProposalSources":"PUBLISHED_TOPIC,STUDENT_PROPOSAL","ProposalSource":"STUDENT_PROPOSAL","MajorDepartmentIds":{"'+CONVERT(nvarchar(20),@v5ItMajor)+'":'+CONVERT(nvarchar(20),@v5ItDept)+',"'+CONVERT(nvarchar(20),@v5MktMajor)+'":'+CONVERT(nvarchar(20),@v5MktDept)+',"'+CONVERT(nvarchar(20),@v5DesMajor)+'":'+CONVERT(nvarchar(20),@v5DesDept)+'},"ProjectRequirements":[]}' ;
IF NOT EXISTS (SELECT 1 FROM dbo.project_registration_snapshots WHERE project_id=@v5Project)
    INSERT dbo.project_registration_snapshots(project_id,project_period_id,lead_department_id,submitted_by,submitted_at,snapshot_json) VALUES(@v5Project,@evalPeriod,@v5ItDept,@v5Leader,@now,@v5Json);
DECLARE @v5Snapshot bigint=(SELECT TOP(1) id FROM dbo.project_registration_snapshots WHERE project_id=@v5Project ORDER BY id DESC);
INSERT dbo.project_department_decisions(snapshot_id,department_id,decision,decided_by,decided_at,reason)
SELECT @v5Snapshot,d.id,N'APPROVED',CASE WHEN d.id=@v5ItDept THEN (SELECT id FROM dbo.users WHERE email=N'v5.staff.it@e2e.invalid') WHEN d.id=@v5MktDept THEN (SELECT id FROM dbo.users WHERE email=N'v5.staff.marketing@e2e.invalid') ELSE (SELECT id FROM dbo.users WHERE email=N'v5.staff.design@e2e.invalid') END,@now,N'V5 demo department approval' FROM dbo.departments d WHERE d.id IN (@v5ItDept,@v5MktDept,@v5DesDept) AND NOT EXISTS (SELECT 1 FROM dbo.project_department_decisions x WHERE x.snapshot_id=@v5Snapshot AND x.department_id=d.id);
DECLARE @v5PrimaryRequest bigint=(SELECT TOP(1) id FROM dbo.supervisor_requests WHERE project_id=@v5Project AND supervisor_profile_id=@v5PrimaryProfile ORDER BY id DESC);
IF @v5PrimaryRequest IS NULL BEGIN INSERT dbo.supervisor_requests(project_id,supervisor_profile_id,requested_by,assignment_type,status,response_message,requested_at,responded_at,created_at,updated_at) VALUES(@v5Project,@v5PrimaryProfile,@v5Leader,'PRIMARY',N'ACCEPTED',N'V5 accepted',@now,@now,@now,@now); SET @v5PrimaryRequest=SCOPE_IDENTITY(); END;
IF NOT EXISTS (SELECT 1 FROM dbo.supervisor_assignments WHERE project_id=@v5Project AND ended_at IS NULL AND is_primary=1) INSERT dbo.supervisor_assignments(project_id,supervisor_profile_id,supervisor_request_id,is_primary,assignment_type,assigned_by,assigned_at,created_at,updated_at) VALUES(@v5Project,@v5PrimaryProfile,@v5PrimaryRequest,1,'PRIMARY',(SELECT id FROM dbo.users WHERE email=N'v5.staff.it@e2e.invalid'),@now,@now,@now);
DECLARE @v5MktRequest bigint=(SELECT TOP(1) id FROM dbo.supervisor_requests WHERE project_id=@v5Project AND supervisor_profile_id=@v5MktMentorProfile ORDER BY id DESC);
IF @v5MktRequest IS NULL BEGIN INSERT dbo.supervisor_requests(project_id,supervisor_profile_id,requested_by,assignment_type,major_id,status,response_message,requested_at,responded_at,created_at,updated_at) VALUES(@v5Project,@v5MktMentorProfile,@v5Leader,'DISCIPLINE_MENTOR',@v5MktMajor,N'ACCEPTED',N'V5 accepted',@now,@now,@now,@now); SET @v5MktRequest=SCOPE_IDENTITY(); END;
IF NOT EXISTS (SELECT 1 FROM dbo.supervisor_assignments WHERE project_id=@v5Project AND ended_at IS NULL AND assignment_type='DISCIPLINE_MENTOR' AND major_id=@v5MktMajor) INSERT dbo.supervisor_assignments(project_id,supervisor_profile_id,supervisor_request_id,is_primary,assignment_type,major_id,assigned_by,assigned_at,created_at,updated_at) VALUES(@v5Project,@v5MktMentorProfile,@v5MktRequest,0,'DISCIPLINE_MENTOR',@v5MktMajor,(SELECT id FROM dbo.users WHERE email=N'v5.staff.marketing@e2e.invalid'),@now,@now,@now);
DECLARE @v5DesRequest bigint=(SELECT TOP(1) id FROM dbo.supervisor_requests WHERE project_id=@v5Project AND supervisor_profile_id=@v5DesMentorProfile ORDER BY id DESC);
IF @v5DesRequest IS NULL BEGIN INSERT dbo.supervisor_requests(project_id,supervisor_profile_id,requested_by,assignment_type,major_id,status,response_message,requested_at,responded_at,created_at,updated_at) VALUES(@v5Project,@v5DesMentorProfile,@v5Leader,'DISCIPLINE_MENTOR',@v5DesMajor,N'ACCEPTED',N'V5 accepted',@now,@now,@now,@now); SET @v5DesRequest=SCOPE_IDENTITY(); END;
IF NOT EXISTS (SELECT 1 FROM dbo.supervisor_assignments WHERE project_id=@v5Project AND ended_at IS NULL AND assignment_type='DISCIPLINE_MENTOR' AND major_id=@v5DesMajor) INSERT dbo.supervisor_assignments(project_id,supervisor_profile_id,supervisor_request_id,is_primary,assignment_type,major_id,assigned_by,assigned_at,created_at,updated_at) VALUES(@v5Project,@v5DesMentorProfile,@v5DesRequest,0,'DISCIPLINE_MENTOR',@v5DesMajor,(SELECT id FROM dbo.users WHERE email=N'v5.staff.design@e2e.invalid'),@now,@now,@now);
DECLARE @v5Agreement bigint=(SELECT TOP(1) id FROM dbo.project_working_agreements WHERE project_id=@v5Project AND status='ACTIVE');
IF @v5Agreement IS NULL BEGIN INSERT dbo.project_working_agreements(project_id,version,status,content_json,concurrency_token,created_by,created_at,published_at) VALUES(@v5Project,1,'ACTIVE',N'{"title":"V5 IT Marketing Design Agreement","rules":["Respect component ownership","Share exact evidence scope"]}',NEWID(),@v5Leader,@now,@now); SET @v5Agreement=SCOPE_IDENTITY(); END;
INSERT dbo.agreement_members(agreement_id,user_id,team_member_id,membership_joined_at,membership_generation,accepted_at)
SELECT @v5Agreement,m.user_id,m.id,m.joined_at,m.membership_generation,@now FROM dbo.team_members m WHERE m.team_id=@v5Team AND NOT EXISTS (SELECT 1 FROM dbo.agreement_members a WHERE a.agreement_id=@v5Agreement AND a.user_id=m.user_id);
DECLARE @v5Round bigint=(SELECT TOP(1) id FROM dbo.peer_evaluation_rounds WHERE project_id=@v5Project AND status='OPEN');
IF @v5Round IS NULL BEGIN INSERT dbo.peer_evaluation_rounds(project_id,title,status,opens_at,closes_at,concurrency_token,created_by,primary_assignment_id,created_at) SELECT @v5Project,N'V5 Cross Department Peer Round','OPEN',DATEADD(day,-1,@now),DATEADD(day,14,@now),NEWID(),@v5Leader,id,@now FROM dbo.supervisor_assignments WHERE project_id=@v5Project AND ended_at IS NULL AND is_primary=1; SET @v5Round=SCOPE_IDENTITY(); END;
INSERT dbo.peer_round_members(round_id,user_id,major_id,department_id,team_member_id,membership_generation,membership_joined_at,full_name)
SELECT @v5Round,m.user_id,u.major_id,u.department_id,m.id,m.membership_generation,m.joined_at,u.full_name FROM dbo.team_members m JOIN dbo.users u ON u.id=m.user_id WHERE m.team_id=@v5Team AND NOT EXISTS (SELECT 1 FROM dbo.peer_round_members r WHERE r.round_id=@v5Round AND r.user_id=m.user_id);
DECLARE @v5Milestone bigint=(SELECT TOP(1) id FROM dbo.milestones WHERE project_id=@v5Project);
IF @v5Milestone IS NULL BEGIN INSERT dbo.milestones(project_id,title,description,start_date,due_date,status,sort_order,created_by,created_at,updated_at) VALUES(@v5Project,N'Cross department integration',N'V5 IT + Marketing + Design milestone',CONVERT(date,@now),DATEADD(day,14,CONVERT(date,@now)),N'IN_PROGRESS',1,@v5Leader,@now,@now); SET @v5Milestone=SCOPE_IDENTITY(); END;
INSERT dbo.tasks(milestone_id,title,description,status,priority,start_at,due_at,created_by,created_at,updated_at,story_points)
SELECT @v5Milestone,N'Integrate three discipline evidence',N'V5 demo task',N'IN_PROGRESS',N'HIGH',@now,DATEADD(day,7,@now),@v5Leader,@now,@now,8 WHERE NOT EXISTS (SELECT 1 FROM dbo.tasks WHERE milestone_id=@v5Milestone AND title=N'Integrate three discipline evidence');
DECLARE @rubric bigint=(SELECT id FROM dbo.rubrics WHERE code=N'E2E-RUBRIC');
IF @rubric IS NULL
BEGIN
    INSERT dbo.evaluation_criteria(code,name,is_active)
    VALUES (N'E2E-CRIT-PROGRESS',N'Tiến độ và thực thi',1),
           (N'E2E-CRIT-QUALITY',N'Chất lượng bàn giao',1);
    DECLARE @crit1 bigint=(SELECT id FROM dbo.evaluation_criteria WHERE code=N'E2E-CRIT-PROGRESS');
    DECLARE @crit2 bigint=(SELECT id FROM dbo.evaluation_criteria WHERE code=N'E2E-CRIT-QUALITY');
    INSERT dbo.rubrics(department_id,academic_semester_id,code,name,description,is_active,created_by)
    VALUES (@leadDept,@semester,N'E2E-RUBRIC',N'Rubric đánh giá tuần và bảo vệ E2E',N'Barem tiêu chuẩn kiểm thử tự động',1,@staffCs);
    SET @rubric=SCOPE_IDENTITY();
    INSERT dbo.rubric_versions(rubric_id,root_rubric_id,version_number,status,concurrency_token)
    VALUES (@rubric,@rubric,1,'PUBLISHED',NEWID());
    INSERT dbo.rubric_criteria(rubric_id,criterion_id,weight_percent,max_score,sort_order,is_required)
    VALUES (@rubric,@crit1,50.00,10.00,1,1),(@rubric,@crit2,50.00,10.00,2,1);
END;

    -- Evaluator profile and expertise for defense committees
    DECLARE @evaluatorUser bigint=(SELECT id FROM dbo.users WHERE email=N'evaluator@e2e.invalid');
    IF NOT EXISTS (SELECT 1 FROM dbo.supervisor_profiles WHERE user_id=@evaluatorUser)
    BEGIN
        INSERT dbo.supervisor_profiles(user_id,is_available,created_at,updated_at) VALUES(@evaluatorUser,1,@now,@now);
    END
    ELSE
    BEGIN
        UPDATE dbo.supervisor_profiles SET is_available=1 WHERE user_id=@evaluatorUser;
    END;
    DECLARE @evaluatorProfile bigint=(SELECT id FROM dbo.supervisor_profiles WHERE user_id=@evaluatorUser);
    IF NOT EXISTS (SELECT 1 FROM dbo.supervisor_expertise WHERE supervisor_profile_id=@evaluatorProfile AND expertise_name=N'E2E-SE')
    BEGIN
        INSERT dbo.supervisor_expertise(supervisor_profile_id,expertise_name,created_at,updated_at)
        VALUES (@evaluatorProfile,N'E2E-SE',@now,@now);
    END;

    -- Industry expert user & profile
    DECLARE @expertUser bigint=(SELECT id FROM dbo.users WHERE email=N'expert.advisor@e2e.invalid');
    IF @expertUser IS NULL
    BEGIN
        INSERT dbo.users(department_id,major_id,email,password_hash,full_name,employee_code,status,academic_profile_status)
        VALUES (NULL,NULL,N'expert.advisor@e2e.invalid',N'hash',N'E2E Industry Advisor',N'EXP-001',N'ACTIVE',N'VERIFIED');
        SET @expertUser=SCOPE_IDENTITY();
    END;
    IF NOT EXISTS (SELECT 1 FROM dbo.industry_experts WHERE user_id=@expertUser)
    BEGIN
        INSERT dbo.industry_experts(user_id,organization,created_at,created_by,enrolled_at)
        VALUES (@expertUser,N'Tech Innovations Corp',@now,@staffCs,@now);
    END;

    -- Extend E2E-EVAL period so deadlines can be flexibly scheduled in tests
        UPDATE dbo.project_periods SET end_at='2026-12-31 23:59:59', status=N'ACTIVE' WHERE id=@evalPeriod;

    -- Interdisciplinary project final submission package for evaluation workflows (BE-09, BE-16)
    DECLARE @interDeliverable bigint=(SELECT TOP(1) id FROM dbo.deliverables WHERE project_id=@interProject);
    IF @interDeliverable IS NULL
    BEGIN
        INSERT dbo.deliverables(project_id,milestone_id,title,description,status,created_by,created_at,updated_at)
        VALUES(@interProject,NULL,N'Inter Final Deliverable',N'Final project package',N'ACCEPTED',@interLeader,@now,@now);
        SET @interDeliverable=SCOPE_IDENTITY();
    END;

    DECLARE @interDeliverableVersion bigint=(SELECT TOP(1) id FROM dbo.deliverable_versions WHERE deliverable_id=@interDeliverable);
    IF @interDeliverableVersion IS NULL
    BEGIN
        INSERT dbo.deliverable_versions(deliverable_id,version_number,submitted_by,status,submitted_at,created_at,updated_at)
        VALUES(@interDeliverable,1,@interLeader,N'ACCEPTED',@now,@now,@now);
        SET @interDeliverableVersion=SCOPE_IDENTITY();
    END;

    DECLARE @interFinalSubmission bigint=(SELECT TOP(1) id FROM dbo.final_submissions WHERE project_id=@interProject);
    IF @interFinalSubmission IS NULL
    BEGIN
        INSERT dbo.final_submissions(project_id,project_period_id,submitted_by,submitted_at,deadline,notes,draft_concurrency_token,requirements_concurrency_token)
        VALUES(@interProject,@evalPeriod,@interLeader,@now,DATEADD(day,30,@now),N'Inter locked final submission',NEWID(),NEWID());
        SET @interFinalSubmission=SCOPE_IDENTITY();

        INSERT dbo.final_submission_items(submission_id,deliverable_version_id,deliverable_id,title,version_number,status_at_submission,was_required,files_json)
        VALUES(@interFinalSubmission,@interDeliverableVersion,@interDeliverable,N'Inter Final Deliverable',1,'ACCEPTED',1,N'[{"id":1,"fileName":"inter_final.pdf","contentType":"application/pdf","sizeBytes":102400,"reportType":"Final Report"}]');
    END;

        UPDATE dbo.projects SET status=N'FINAL_SUBMISSION',updated_at=@now WHERE id=@interProject;

    -- Dedicated project and team for legacy evaluation testing (E04)
    DECLARE @evalProject bigint=(SELECT id FROM dbo.projects WHERE code=N'E2E-EVAL-P');
    IF @evalProject IS NULL
    BEGIN
        DECLARE @roleStudentId bigint=(SELECT id FROM dbo.roles WHERE code=N'STUDENT');
        DECLARE @evalStudent1 bigint, @evalStudent2 bigint, @evalStudent3 bigint;
        INSERT dbo.users(department_id,major_id,email,password_hash,full_name,employee_code,status,academic_profile_status)
        VALUES (@leadDept,@singleMajor,N'eval.student1@e2e.invalid',N'hash',N'Eval Student 1',N'STU-EV1',N'ACTIVE',N'VERIFIED');
        SET @evalStudent1=SCOPE_IDENTITY();
        INSERT dbo.users(department_id,major_id,email,password_hash,full_name,employee_code,status,academic_profile_status)
        VALUES (@leadDept,@singleMajor,N'eval.student2@e2e.invalid',N'hash',N'Eval Student 2',N'STU-EV2',N'ACTIVE',N'VERIFIED');
        SET @evalStudent2=SCOPE_IDENTITY();
        INSERT dbo.users(department_id,major_id,email,password_hash,full_name,employee_code,status,academic_profile_status)
        VALUES (@leadDept,@singleMajor,N'eval.student3@e2e.invalid',N'hash',N'Eval Student 3',N'STU-EV3',N'ACTIVE',N'VERIFIED');
        SET @evalStudent3=SCOPE_IDENTITY();

        INSERT dbo.user_roles(user_id,role_id) VALUES(@evalStudent1,@roleStudentId),(@evalStudent2,@roleStudentId),(@evalStudent3,@roleStudentId);

        DECLARE @evalTeam bigint;
        INSERT dbo.teams(academic_semester_id,code,name,description,status,created_by,created_at,updated_at)
        VALUES(@semester,N'E2E-EVAL-TEAM',N'E2E Eval Team',N'Legacy rubric test team',N'LOCKED',@evalStudent1,@now,@now);
        SET @evalTeam=SCOPE_IDENTITY();

        INSERT dbo.team_members(team_id,academic_semester_id,user_id,is_leader,joined_at,created_at,updated_at)
        VALUES(@evalTeam,@semester,@evalStudent1,1,@now,@now,@now),
              (@evalTeam,@semester,@evalStudent2,0,@now,@now,@now),
              (@evalTeam,@semester,@evalStudent3,0,@now,@now,@now);

        INSERT dbo.team_academic_configurations(team_id,project_mode,primary_major_id,lead_department_id,concurrency_token)
        VALUES(@evalTeam,'SINGLE_MAJOR',@singleMajor,@leadDept,NEWID());

        INSERT dbo.projects(team_id,code,title,description,objectives,problem_statement,expected_output,status,created_by,created_at,updated_at)
        VALUES(@evalTeam,N'E2E-EVAL-P',N'E2E Evaluation Project',N'Legacy rubric test',N'Eval',N'Eval',N'Output',N'FINAL_SUBMISSION',@evalStudent1,@now,@now);
        SET @evalProject=SCOPE_IDENTITY();
        INSERT dbo.project_majors(project_id,major_id) VALUES(@evalProject,@singleMajor);

        DECLARE @evalToken uniqueidentifier=NEWID();
        DECLARE @sEv1 nvarchar(20)=CONVERT(nvarchar(20),@evalStudent1);
        DECLARE @sEv2 nvarchar(20)=CONVERT(nvarchar(20),@evalStudent2);
        DECLARE @sEv3 nvarchar(20)=CONVERT(nvarchar(20),@evalStudent3);
        DECLARE @evalJson nvarchar(max)=N'{
          "Scope":{"ProjectMode":"SINGLE_MAJOR","PrimaryMajorId":'+@sSm+',"LeadDepartmentId":'+@sLd+',"Requirements":[{"MajorId":'+@sSm+',"MinMembers":1,"MaxMembers":5,"Responsibility":"Software engineering"}],"ConcurrencyToken":"'+CONVERT(nvarchar(36),@evalToken)+'"},
          "Policy":{"MinMembers":3,"MaxMembers":5,"MinDistinctMajors":1,"Version":"E2E-2026"},"OrganizationId":'+@sOrg+',"WindowStartAt":"2026-01-01T00:00:00Z","WindowEndAt":"2026-12-31T23:59:59Z",
          "Members":[{"UserId":'+@sEv1+',"FullName":"Eval Student 1","MajorId":'+@sSm+',"IsLeader":true},{"UserId":'+@sEv2+',"FullName":"Eval Student 2","MajorId":'+@sSm+',"IsLeader":false},{"UserId":'+@sEv3+',"FullName":"Eval Student 3","MajorId":'+@sSm+',"IsLeader":false}],
          "DepartmentIds":['+@sLd+'],"PolicyVersion":1,"AllowedProjectModes":"SINGLE_MAJOR,INTERDISCIPLINARY","AllowedProposalSources":"PUBLISHED_TOPIC,STUDENT_PROPOSAL","ProposalSource":"STUDENT_PROPOSAL","MajorDepartmentIds":{"'+@sSm+'":'+@sLd+'},"ProjectRequirements":[{"MajorId":'+@sSm+',"MinMembers":1,"MaxMembers":5,"Responsibility":"Software engineering"}],"TeamResponsibilities":[]}';

        INSERT dbo.project_registration_snapshots(project_id,project_period_id,lead_department_id,submitted_by,submitted_at,snapshot_json)
        VALUES(@evalProject,@evalPeriod,@leadDept,@evalStudent1,@now,@evalJson);

        DECLARE @evalDeliverable bigint;
        INSERT dbo.deliverables(project_id,milestone_id,title,description,status,created_by,created_at,updated_at)
        VALUES(@evalProject,NULL,N'Eval Final Deliverable',N'Final package',N'ACCEPTED',@evalStudent1,@now,@now);
        SET @evalDeliverable=SCOPE_IDENTITY();

        DECLARE @evalDeliverableVersion bigint;
        INSERT dbo.deliverable_versions(deliverable_id,version_number,submitted_by,status,submitted_at,created_at,updated_at)
        VALUES(@evalDeliverable,1,@evalStudent1,N'ACCEPTED',@now,@now,@now);
        SET @evalDeliverableVersion=SCOPE_IDENTITY();

        DECLARE @evalFinalSubmission bigint;
        INSERT dbo.final_submissions(project_id,project_period_id,submitted_by,submitted_at,deadline,notes,draft_concurrency_token,requirements_concurrency_token)
        VALUES(@evalProject,@evalPeriod,@evalStudent1,@now,DATEADD(day,30,@now),N'Eval locked final submission',NEWID(),NEWID());
        SET @evalFinalSubmission=SCOPE_IDENTITY();

        INSERT dbo.final_submission_items(submission_id,deliverable_version_id,deliverable_id,title,version_number,status_at_submission,was_required,files_json)
        VALUES(@evalFinalSubmission,@evalDeliverableVersion,@evalDeliverable,N'Eval Final Deliverable',1,'ACCEPTED',1,N'[{"id":1,"fileName":"eval_final.pdf","contentType":"application/pdf","sizeBytes":102400,"reportType":"Final Report"}]');
    END;

    -- Dedicated project and team for hierarchical rubric evaluation testing (E10)
    DECLARE @hierProject bigint=(SELECT id FROM dbo.projects WHERE code=N'E2E-HIER-P');
    IF @hierProject IS NULL
    BEGIN
        DECLARE @roleStudentId2 bigint=(SELECT id FROM dbo.roles WHERE code=N'STUDENT');
        DECLARE @hierStudent1 bigint, @hierStudent2 bigint, @hierStudent3 bigint;
        INSERT dbo.users(department_id,major_id,email,password_hash,full_name,employee_code,status,academic_profile_status)
        VALUES (@leadDept,@singleMajor,N'hier.student1@e2e.invalid',N'hash',N'Hier Student 1',N'STU-HI1',N'ACTIVE',N'VERIFIED');
        SET @hierStudent1=SCOPE_IDENTITY();
        INSERT dbo.users(department_id,major_id,email,password_hash,full_name,employee_code,status,academic_profile_status)
        VALUES (@leadDept,@singleMajor,N'hier.student2@e2e.invalid',N'hash',N'Hier Student 2',N'STU-HI2',N'ACTIVE',N'VERIFIED');
        SET @hierStudent2=SCOPE_IDENTITY();
        INSERT dbo.users(department_id,major_id,email,password_hash,full_name,employee_code,status,academic_profile_status)
        VALUES (@leadDept,@singleMajor,N'hier.student3@e2e.invalid',N'hash',N'Hier Student 3',N'STU-HI3',N'ACTIVE',N'VERIFIED');
        SET @hierStudent3=SCOPE_IDENTITY();

        INSERT dbo.user_roles(user_id,role_id) VALUES(@hierStudent1,@roleStudentId2),(@hierStudent2,@roleStudentId2),(@hierStudent3,@roleStudentId2);

        DECLARE @hierTeam bigint;
        INSERT dbo.teams(academic_semester_id,code,name,description,status,created_by,created_at,updated_at)
        VALUES(@semester,N'E2E-HIER-TEAM',N'E2E Hier Team',N'Hierarchical rubric test team',N'LOCKED',@hierStudent1,@now,@now);
        SET @hierTeam=SCOPE_IDENTITY();

        INSERT dbo.team_members(team_id,academic_semester_id,user_id,is_leader,joined_at,created_at,updated_at)
        VALUES(@hierTeam,@semester,@hierStudent1,1,@now,@now,@now),
              (@hierTeam,@semester,@hierStudent2,0,@now,@now,@now),
              (@hierTeam,@semester,@hierStudent3,0,@now,@now,@now);

        INSERT dbo.team_academic_configurations(team_id,project_mode,primary_major_id,lead_department_id,concurrency_token)
        VALUES(@hierTeam,'SINGLE_MAJOR',@singleMajor,@leadDept,NEWID());

        INSERT dbo.projects(team_id,code,title,description,objectives,problem_statement,expected_output,status,created_by,created_at,updated_at)
        VALUES(@hierTeam,N'E2E-HIER-P',N'E2E Hierarchical Project',N'Hierarchical rubric test',N'Hierarchy',N'Hierarchy',N'Output',N'FINAL_SUBMISSION',@hierStudent1,@now,@now);
        SET @hierProject=SCOPE_IDENTITY();
        INSERT dbo.project_majors(project_id,major_id) VALUES(@hierProject,@singleMajor);

        DECLARE @hierToken uniqueidentifier=NEWID();
        DECLARE @sHi1 nvarchar(20)=CONVERT(nvarchar(20),@hierStudent1);
        DECLARE @sHi2 nvarchar(20)=CONVERT(nvarchar(20),@hierStudent2);
        DECLARE @sHi3 nvarchar(20)=CONVERT(nvarchar(20),@hierStudent3);
        DECLARE @hierJson nvarchar(max)=N'{
          "Scope":{"ProjectMode":"SINGLE_MAJOR","PrimaryMajorId":'+@sSm+',"LeadDepartmentId":'+@sLd+',"Requirements":[{"MajorId":'+@sSm+',"MinMembers":1,"MaxMembers":5,"Responsibility":"Software engineering"}],"ConcurrencyToken":"'+CONVERT(nvarchar(36),@hierToken)+'"},
          "Policy":{"MinMembers":3,"MaxMembers":5,"MinDistinctMajors":1,"Version":"E2E-2026"},"OrganizationId":'+@sOrg+',"WindowStartAt":"2026-01-01T00:00:00Z","WindowEndAt":"2026-12-31T23:59:59Z",
          "Members":[{"UserId":'+@sHi1+',"FullName":"Hier Student 1","MajorId":'+@sSm+',"IsLeader":true},{"UserId":'+@sHi2+',"FullName":"Hier Student 2","MajorId":'+@sSm+',"IsLeader":false},{"UserId":'+@sHi3+',"FullName":"Hier Student 3","MajorId":'+@sSm+',"IsLeader":false}],
          "DepartmentIds":['+@sLd+'],"PolicyVersion":1,"AllowedProjectModes":"SINGLE_MAJOR,INTERDISCIPLINARY","AllowedProposalSources":"PUBLISHED_TOPIC,STUDENT_PROPOSAL","ProposalSource":"STUDENT_PROPOSAL","MajorDepartmentIds":{"'+@sSm+'":'+@sLd+'},"ProjectRequirements":[{"MajorId":'+@sSm+',"MinMembers":1,"MaxMembers":5,"Responsibility":"Software engineering"}],"TeamResponsibilities":[]}';

        INSERT dbo.project_registration_snapshots(project_id,project_period_id,lead_department_id,submitted_by,submitted_at,snapshot_json)
        VALUES(@hierProject,@evalPeriod,@leadDept,@hierStudent1,@now,@hierJson);

        DECLARE @hierDeliverable bigint;
        INSERT dbo.deliverables(project_id,milestone_id,title,description,status,created_by,created_at,updated_at)
        VALUES(@hierProject,NULL,N'Hier Final Deliverable',N'Final package',N'ACCEPTED',@hierStudent1,@now,@now);
        SET @hierDeliverable=SCOPE_IDENTITY();

        DECLARE @hierDeliverableVersion bigint;
        INSERT dbo.deliverable_versions(deliverable_id,version_number,submitted_by,status,submitted_at,created_at,updated_at)
        VALUES(@hierDeliverable,1,@hierStudent1,N'ACCEPTED',@now,@now,@now);
        SET @hierDeliverableVersion=SCOPE_IDENTITY();

        DECLARE @hierFinalSubmission bigint;
        INSERT dbo.final_submissions(project_id,project_period_id,submitted_by,submitted_at,deadline,notes,draft_concurrency_token,requirements_concurrency_token)
        VALUES(@hierProject,@evalPeriod,@hierStudent1,@now,DATEADD(day,30,@now),N'Hier locked final submission',NEWID(),NEWID());
        SET @hierFinalSubmission=SCOPE_IDENTITY();

        INSERT dbo.final_submission_items(submission_id,deliverable_version_id,deliverable_id,title,version_number,status_at_submission,was_required,files_json)
        VALUES(@hierFinalSubmission,@hierDeliverableVersion,@hierDeliverable,N'Hier Final Deliverable',1,'ACCEPTED',1,N'[{"id":1,"fileName":"hier_final.pdf","contentType":"application/pdf","sizeBytes":102400,"reportType":"Final Report"}]');
    END;
COMMIT TRANSACTION;
'@
    Write-Output "Prepared v5 demo data in $DatabaseName."
}
finally { $connection.Dispose() }
