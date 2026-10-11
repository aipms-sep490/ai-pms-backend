using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class ColdSubmissionEndpointTests
{
    [Fact]
    public async Task Individual_target_denies_another_member_of_the_same_major_and_departed_owner()
    {
        using var app = new ColdFactory(database);
        var s = await database.Seed(false); long memberId, majorId;
        await using (var db = database.CreateContext())
        {
            majorId = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();
            var member = new M.User { Email = Guid.NewGuid().ToString("N") + "@e2e.invalid", FullName = "Same major member", PasswordHash = "unused",
                Status = "ACTIVE", AcademicProfileStatus = "VERIFIED", DepartmentId = s.Scope.Users.DepartmentId, MajorId = majorId,
                UserRoleUsers = [new() { RoleId = await db.Roles.Where(r => r.Code == "STUDENT").Select(r => r.Id).SingleAsync() }] };
            db.Users.Add(member); await db.SaveChangesAsync(); memberId = member.Id;
            db.TeamMembers.Add(new() { TeamId = (await db.Projects.FindAsync(s.ProjectId))!.TeamId, UserId = memberId, AcademicSemesterId = s.Scope.SemesterId });
            var snapshot = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(p => p.ProjectId == s.ProjectId);
            var registration = JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!;
            snapshot.SnapshotJson = JsonSerializer.Serialize(registration with { Members = [.. registration.Members, new(memberId, member.FullName, majorId, false)] });
            await db.SaveChangesAsync();
        }
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff); using var owner = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var other = app.CreateAuthenticatedClient(memberId);
        var template = await Body<ColdTemplateDto>(await staff.PostAsJsonAsync("/api/v1/cold-report-templates",
            new CreateColdTemplateRequest(majorId, s.PeriodId, "Individual", [new("INDIVIDUAL", "Individual report")])));
        var checklist = await Body<ColdChecklistDto>(await staff.PostAsJsonAsync(ChecklistUrl(s.ProjectId),
            new ApplyColdChecklistRequest(s.PeriodId, new(Now.AddHours(2)), [new(template.Id, "INDIVIDUAL", s.Scope.Users.Student)])));
        var item = checklist.Items.Single();
        Assert.Empty((await Body<ColdChecklistDto>(await other.GetAsync(ChecklistUrl(s.ProjectId)))).Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync(DraftUrl(item.Id), new SaveColdDraftRequest(0))).StatusCode);
        var submitted = await SubmitReport(owner, item.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(submitted.Files.Single().DownloadUrl)).StatusCode);
        await using (var db = database.CreateContext())
        {
            var membership = await db.TeamMembers.SingleAsync(m => m.Team.Project != null && m.Team.Project.Id == s.ProjectId && m.UserId == s.Scope.Users.Student);
            membership.LeftAt = membership.JoinedAt > Now ? membership.JoinedAt : Now;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync(submitted.Files.Single().DownloadUrl)).StatusCode);
    }

    [Fact]
    public async Task Ten_MiB_and_ten_file_bounds_are_enforced_without_persisting_rejected_uploads()
    {
        using var app = new ColdFactory(database); var (s, checklist, _) = await Prepare(app);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        var draft = await Body<ColdSubmissionDto>(await student.PostAsJsonAsync(DraftUrl(checklist.Items.Single().Id), new SaveColdDraftRequest(0)));
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadResponse(student, draft, text: "%PDF-" + new string('x', 10 * 1024 * 1024))).StatusCode);
        for (var i = 0; i < 10; i++) draft = await Body<ColdSubmissionDto>(await UploadResponse(student, draft));
        Assert.Equal(10, draft.Files.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadResponse(student, draft)).StatusCode);
        Assert.Equal(10, app.Storage.Objects.Count);
    }

    [Fact]
    public async Task Interdisciplinary_checklists_are_major_specific_students_cannot_submit_others_and_ended_mentor_loses_access()
    {
        using var app = new ColdFactory(database);
        var s = await database.Seed(true);
        long firstMajor, marketingMajor, marketingStudent, mentorAssignment, marketingDepartment;
        await using (var db = database.CreateContext())
        {
            firstMajor = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();
            var department = (await db.Users.FindAsync(s.Scope.Users.OtherLecturer))!.DepartmentId!.Value;
            marketingDepartment = department;
            var major = new M.Major { DepartmentId = department, Code = Guid.NewGuid().ToString("N"), Name = "Marketing", IsActive = true };
            var student = new M.User { Email = Guid.NewGuid().ToString("N") + "@e2e.invalid", FullName = "Marketing test student",
                PasswordHash = "unused", Status = "ACTIVE", DepartmentId = department, Major = major, AcademicProfileStatus = "VERIFIED",
                UserRoleUsers = [new() { RoleId = await db.Roles.Where(r => r.Code == "STUDENT").Select(r => r.Id).SingleAsync() }] };
            db.Users.Add(student); db.ProjectMajors.Add(new() { ProjectId = s.ProjectId, Major = major });
            await db.SaveChangesAsync(); marketingMajor = major.Id; marketingStudent = student.Id;
            db.TeamMembers.Add(new() { TeamId = (await db.Projects.FindAsync(s.ProjectId))!.TeamId, UserId = student.Id, AcademicSemesterId = s.Scope.SemesterId });
            var snapshot = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(r => r.ProjectId == s.ProjectId);
            var registration = JsonSerializer.Deserialize<RegistrationEvidence>(snapshot.SnapshotJson)!;
            snapshot.SnapshotJson = JsonSerializer.Serialize(registration with {
                Scope = registration.Scope with { ProjectMode = "INTERDISCIPLINARY", PrimaryMajorId = null,
                    Requirements = [.. registration.Scope.Requirements, new(major.Id, 1, 5, "Marketing research")] },
                Members = [.. registration.Members, new(student.Id, student.FullName, major.Id, false)],
                DepartmentIds = [s.Scope.Users.DepartmentId, department],
                MajorDepartmentIds = new Dictionary<long, long> { [firstMajor] = s.Scope.Users.DepartmentId, [major.Id] = department } });
            var profile = new M.SupervisorProfile { UserId = s.Scope.Users.OtherLecturer };
            db.SupervisorProfiles.Add(profile); await db.SaveChangesAsync();
            var request = new M.SupervisorRequest { ProjectId = s.ProjectId, SupervisorProfileId = profile.Id,
                RequestedBy = s.Scope.Users.Student, Status = "ACCEPTED", RequestedAt = Now.AddDays(-1), RespondedAt = Now.AddDays(-1) };
            db.SupervisorRequests.Add(request); await db.SaveChangesAsync();
            var mentor = new M.SupervisorAssignment { ProjectId = s.ProjectId, SupervisorProfileId = profile.Id, SupervisorRequestId = request.Id,
                IsPrimary = false, AssignmentType = "DISCIPLINE_MENTOR", MajorId = major.Id, AssignedAt = Now.AddDays(-1) };
            db.SupervisorAssignments.Add(mentor); await db.SaveChangesAsync(); mentorAssignment = mentor.Id;
        }
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var marketingStaff = app.CreateAuthenticatedClient(s.Scope.Users.OutsideStaff);
        using var it = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var marketing = app.CreateAuthenticatedClient(marketingStudent);
        using var mentorClient = app.CreateAuthenticatedClient(s.Scope.Users.OtherLecturer);
        var itTemplate = await Body<ColdTemplateDto>(await staff.PostAsJsonAsync("/api/v1/cold-report-templates",
            new CreateColdTemplateRequest(firstMajor, s.PeriodId, "IT", [new("SRS", "Software requirements")])));
        var marketingTemplate = await Body<ColdTemplateDto>(await marketingStaff.PostAsJsonAsync("/api/v1/cold-report-templates",
            new CreateColdTemplateRequest(marketingMajor, s.PeriodId, "Marketing", [new("RESEARCH", "Market research")])));
        var apply = new ApplyColdChecklistRequest(s.PeriodId, new(Now.AddHours(2)), [new(itTemplate.Id, "MAJOR_SPECIFIC"),
            new(marketingTemplate.Id, "MAJOR_SPECIFIC"), new(marketingTemplate.Id, "INDIVIDUAL", marketingStudent)]);
        Assert.Equal(HttpStatusCode.Forbidden, (await marketingStaff.PostAsJsonAsync(ChecklistUrl(s.ProjectId), apply)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync(ChecklistUrl(s.ProjectId),
            apply with { Targets = [new(marketingTemplate.Id, "INDIVIDUAL", s.Scope.Users.Student)] })).StatusCode);
        var checklist = await Body<ColdChecklistDto>(await staff.PostAsJsonAsync(ChecklistUrl(s.ProjectId), apply));
        var itItem = checklist.Items.Single(i => i.MajorId == firstMajor);
        var marketingItem = checklist.Items.Single(i => i.Scope == "MAJOR_SPECIFIC" && i.MajorId == marketingMajor);
        Assert.Single((await Body<ColdChecklistDto>(await it.GetAsync(ChecklistUrl(s.ProjectId)))).Items);
        Assert.Equal(2, (await Body<ColdChecklistDto>(await marketing.GetAsync(ChecklistUrl(s.ProjectId)))).Items.Count);
        Assert.DoesNotContain((await Body<ColdChecklistDto>(await marketingStaff.GetAsync(ChecklistUrl(s.ProjectId)))).Items, i => i.MajorId == firstMajor);
        Assert.Equal(HttpStatusCode.Forbidden, (await it.PostAsJsonAsync(DraftUrl(marketingItem.Id), new SaveColdDraftRequest(0))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await marketing.PostAsJsonAsync(DraftUrl(itItem.Id), new SaveColdDraftRequest(0))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mentorClient.PostAsJsonAsync(DraftUrl(marketingItem.Id), new SaveColdDraftRequest(0))).StatusCode);
        var submission = await SubmitReport(marketing, marketingItem.Id);
        Assert.Equal(HttpStatusCode.OK, (await mentorClient.GetAsync(submission.Files.Single().DownloadUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await it.GetAsync(submission.Files.Single().DownloadUrl)).StatusCode);
        await using (var db = database.CreateContext())
        {
            (await db.SupervisorAssignments.FindAsync(mentorAssignment))!.EndedAt = Now;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await mentorClient.GetAsync(submission.Files.Single().DownloadUrl)).StatusCode);
        var nextTemplate = await Body<ColdTemplateDto>(await marketingStaff.PostAsJsonAsync("/api/v1/cold-report-templates",
            new CreateColdTemplateRequest(marketingMajor, s.PeriodId, "Marketing v2", [new("NEW_REPORT", "New requirement")])));
        Assert.Equal(2, nextTemplate.Version);
        Assert.DoesNotContain((await Body<ColdChecklistDto>(await marketing.GetAsync(ChecklistUrl(s.ProjectId)))).Items, i => i.ReportType == "NEW_REPORT");
        await SubmitReport(it, itItem.Id);
        await SubmitReport(marketing, checklist.Items.Single(i => i.Scope == "INDIVIDUAL").Id);
        await LockCold(staff, s.ProjectId);
        var rubric = await Body<RubricDto>(await marketingStaff.PostAsJsonAsync("/api/v1/rubrics",
            new CreateRubricRequest(marketingDepartment,
                s.Scope.SemesterId, Guid.NewGuid().ToString("N"), "Marketing report", null, [new("Research quality", null, 100, 10, 0, true)])));
        rubric = await Body<RubricDto>(await marketingStaff.PostAsJsonAsync($"/api/v1/rubrics/{rubric.Id}/publish", new ChangeRubricStatusRequest(rubric.ConcurrencyToken)));
        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(s.ProjectId, s.PeriodId, "Interdisciplinary COLD", 5,
                [new("Common", "COMMON", null, s.RubricId, 100, 100, 1),
                 new("IT", "MAJOR_SPECIFIC", firstMajor, s.RubricId, 0, 0, 1, Stage: "COLD"),
                 new("Marketing", "MAJOR_SPECIFIC", marketingMajor, rubric.Id, 0, 0, 1, Stage: "COLD")])));
        scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{scheme.Id}/publish", new SchemeTokenRequest(scheme.ConcurrencyToken)));
        var component = scheme.Components.Single(c => c.MajorId == marketingMajor);
        Assert.Equal(HttpStatusCode.Conflict, (await AssignCold(staff, s, component, s.Scope.Users.Lecturer)).StatusCode);
        var assignment = await Body<EvaluationAssignmentDto>(await AssignCold(marketingStaff, s, component, s.Scope.Users.OtherLecturer));
        var evidence = await Body<EvaluationAssignmentEvidenceDto>(await mentorClient.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence"));
        Assert.Equal(2, evidence.Items.Count); Assert.All(evidence.Items, i => Assert.Equal(marketingMajor, i.MajorId));
        var itFile = (await Body<ColdChecklistDto>(await it.GetAsync(ChecklistUrl(s.ProjectId)))).Items.Single().Latest!.Files.Single().Id;
        Assert.Equal(HttpStatusCode.NotFound, (await mentorClient.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evidence/files/{itFile}")).StatusCode);
        var progress = await Body<ColdProgressDto>(await marketingStaff.GetAsync($"/api/v1/projects/{s.ProjectId}/cold/progress"));
        Assert.All(progress.Grading, g => Assert.Equal(marketingMajor, g.MajorId));
        Assert.Single(progress.Grading);
    }

    [Theory]
    [InlineData("zip", "application/zip")]
    [InlineData("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public async Task Real_zip_and_docx_contents_are_accepted_and_draft_removal_preserves_scope(string extension, string mime)
    {
        using var app = new ColdFactory(database); var (s, checklist, _) = await Prepare(app);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        var draft = await Body<ColdSubmissionDto>(await student.PostAsJsonAsync(DraftUrl(checklist.Items.Single().Id), new SaveColdDraftRequest(0)));
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            foreach (var name in extension == "docx" ? new[] { "[Content_Types].xml", "word/document.xml" } : ["report.txt"])
            { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("Synthetic test document"); }
        }
        using var form = new MultipartFormDataContent(); var file = new ByteArrayContent(buffer.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue(mime); form.Add(file, "file", "report." + extension);
        form.Add(new StringContent(draft.ConcurrencyToken), "concurrencyToken");
        draft = await Body<ColdSubmissionDto>(await student.PostAsync($"/api/v1/cold-submissions/{draft.Id}/files", form));
        var oldUrl = draft.Files.Single().DownloadUrl;
        var stale = draft.ConcurrencyToken;
        draft = await Body<ColdSubmissionDto>(await student.DeleteAsync($"/api/v1/cold-submissions/{draft.Id}/files/{draft.Files.Single().Id}?concurrencyToken={draft.ConcurrencyToken}"));
        Assert.Empty(draft.Files); Assert.Empty(app.Storage.Objects);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync(oldUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync(SubmitUrl(draft.Id), new ColdTokenRequest(stale))).StatusCode);
    }
}
