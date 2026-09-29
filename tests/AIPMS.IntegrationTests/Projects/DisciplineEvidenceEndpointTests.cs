using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Disciplines.DTOs;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Projects;

public sealed class DisciplineEvidenceEndpointTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    private sealed class Storage : IFileStorage
    {
        public Task WriteAsync(string key, Stream content, CancellationToken ct) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        public Task DeleteAsync(string key, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class Factory(SupervisorDatabaseFixture database, bool failAudit = false) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                ["NotificationEmail:Enabled"] = "false", ["ScheduledNotifications:Enabled"] = "false"
            }));
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<IFileStorage>(); s.AddSingleton<IFileStorage, Storage>();
                if (failAudit) { s.RemoveAll<IAuditTrail>(); s.AddSingleton<IAuditTrail, FailingAudit>(); }
            });
        }
    }
    private sealed class FailingAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken ct = default) => throw new InvalidOperationException("Injected audit failure.");
    }
    private sealed record Scenario(SupervisorScenario Users, long Project, long Team, long Major, long OtherMajor,
        long Member, long OtherMember, long MentorAssignment, long Milestone, long Task, long Meeting, long Report, long Deliverable, long File);

    private async Task<Scenario> Seed()
    {
        var users = await database.SeedAsync(); await using var db = database.CreateContext(); var now = DateTime.UtcNow;
        var organization = await db.Departments.Where(x => x.Id == users.DepartmentId).Select(x => x.OrganizationId).SingleAsync();
        var semester = new M.AcademicSemester { OrganizationId = organization, Code = Guid.NewGuid().ToString("N"), Name = "Execution", Status = "ACTIVE",
            StartDate = DateOnly.FromDateTime(now.AddDays(-10)), EndDate = DateOnly.FromDateTime(now.AddDays(90)) };
        var otherDept = new M.Department { OrganizationId = organization, Code = Guid.NewGuid().ToString("N"), Name = "Business", IsActive = true };
        var major = new M.Major { DepartmentId = users.DepartmentId, Code = "SE", Name = "SE", IsActive = true };
        var otherMajor = new M.Major { Department = otherDept, Code = "BUS", Name = "Business", IsActive = true };
        var role = await db.Roles.SingleAsync(x => x.Code == "STUDENT");
        M.User Member(string name) => new() { Email = Guid.NewGuid() + "@test.local", FullName = name, DepartmentId = users.DepartmentId,
            Major = major, Status = "ACTIVE", AcademicProfileStatus = "VERIFIED", PasswordHash = "unused", UserRoleUsers = [new() { Role = role }] };
        var member = Member("Assigned member"); var other = Member("Unassigned member");
        db.AddRange(semester, major, otherMajor, member, other); await db.SaveChangesAsync();
        (await db.Users.FindAsync(users.Student))!.MajorId = major.Id;
        var project = new M.Project { Code = Guid.NewGuid().ToString("N"), Title = "Interdisciplinary", Status = "ACTIVE", CreatedBy = users.Student,
            ProjectMajors = [new() { Major = major }, new() { Major = otherMajor }],
            Team = new M.Team { AcademicSemesterId = semester.Id, Code = Guid.NewGuid().ToString("N"), Name = "Team", Status = "LOCKED", CreatedBy = users.Student,
                TeamMembers = [new() { AcademicSemesterId = semester.Id, UserId = users.Student, IsLeader = true },
                    new() { AcademicSemesterId = semester.Id, User = member }, new() { AcademicSemesterId = semester.Id, User = other }] } };
        db.Projects.Add(project); await db.SaveChangesAsync();
        var primaryRequest = new M.SupervisorRequest { ProjectId = project.Id, SupervisorProfileId = users.ProfileId, RequestedBy = users.Student, Status = "ACCEPTED" };
        var mentorProfile = new M.SupervisorProfile { UserId = users.NewLecturer, IsAvailable = true, MaxActiveProjects = 4 };
        db.AddRange(primaryRequest, mentorProfile); await db.SaveChangesAsync();
        var mentorRequest = new M.SupervisorRequest { ProjectId = project.Id, SupervisorProfileId = mentorProfile.Id, RequestedBy = users.Student,
            Status = "ACCEPTED", AssignmentType = "DISCIPLINE_MENTOR", MajorId = major.Id };
        db.Add(mentorRequest); await db.SaveChangesAsync();
        var mentor = new M.SupervisorAssignment { ProjectId = project.Id, SupervisorProfileId = mentorProfile.Id, SupervisorRequestId = mentorRequest.Id,
            AssignmentType = "DISCIPLINE_MENTOR", MajorId = major.Id, IsPrimary = false };
        db.SupervisorAssignments.AddRange(new() { ProjectId = project.Id, SupervisorProfileId = users.ProfileId, SupervisorRequestId = primaryRequest.Id, IsPrimary = true }, mentor);
        var milestone = new M.Milestone { ProjectId = project.Id, Title = "Milestone", Status = "PLANNED", CreatedBy = users.Student };
        var task = new M.Task { Milestone = milestone, Title = "Legacy task", Status = "TODO", CreatedBy = users.Student,
            TaskAssignees = [new() { UserId = member.Id, AssignedBy = users.Student }] };
        var meeting = new M.Meeting { ProjectId = project.Id, Title = "Meeting", Status = "SCHEDULED", CreatedBy = users.Student, StartAt = now.AddDays(1) };
        var report = new M.ProgressReport { ProjectId = project.Id, ReportType = "WEEKLY", Status = "DRAFT", SubmittedBy = users.Student,
            PeriodStart = DateOnly.FromDateTime(now.AddDays(-7)), PeriodEnd = DateOnly.FromDateTime(now), Summary = "Report" };
        var deliverable = new M.Deliverable { ProjectId = project.Id, Title = "Deliverable", Status = "OPEN", CreatedBy = users.Student };
        var file = new M.File { Task = task, UploadedBy = users.Student, OriginalFileName = "evidence.pdf", StoragePath = Guid.NewGuid().ToString("N"), MimeType = "application/pdf", FileSizeBytes = 3 };
        db.AddRange(task, meeting, report, deliverable, file); await db.SaveChangesAsync();
        return new(users, project.Id, project.TeamId, major.Id, otherMajor.Id, member.Id, other.Id, mentor.Id, milestone.Id, task.Id, meeting.Id, report.Id, deliverable.Id, file.Id);
    }

    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static string Disciplines(Scenario s) => $"/api/v1/tasks/{s.Task}/disciplines";
    private static string Evidence(Scenario s) => $"/api/v1/projects/{s.Project}/evidence";
    private static async Task<TaskDisciplinesDto> Classify(HttpClient client, Scenario s)
    {
        var before = await client.GetFromJsonAsync<TaskDisciplinesDto>(Disciplines(s));
        return await Body<TaskDisciplinesDto>(await client.PutAsJsonAsync(Disciplines(s), new ReplaceTaskDisciplinesRequest(before!.ConcurrencyToken, [new(s.Major, "PRIMARY")])));
    }

    [Theory]
    [InlineData("leader", HttpStatusCode.OK)]
    [InlineData("primary", HttpStatusCode.OK)]
    [InlineData("mentor", HttpStatusCode.OK)]
    [InlineData("member", HttpStatusCode.OK)]
    [InlineData("unassigned", HttpStatusCode.Forbidden)]
    [InlineData("staff", HttpStatusCode.Forbidden)]
    [InlineData("outside", HttpStatusCode.Forbidden)]
    public async Task Discipline_writes_enforce_persisted_scope_and_assignment(string kind, HttpStatusCode expected)
    {
        var s = await Seed(); using var factory = new Factory(database);
        var actor = kind switch { "primary" => s.Users.Lecturer, "mentor" => s.Users.NewLecturer, "member" => s.Member,
            "unassigned" => s.OtherMember, "staff" => s.Users.Staff, "outside" => s.Users.OutsideStaff, _ => s.Users.Student };
        using var client = factory.CreateAuthenticatedClient(actor, roles: "ADMIN");
        await using var db = database.CreateContext(); var task = (await db.Tasks.FindAsync(s.Task))!;
        var response = await client.PutAsJsonAsync(Disciplines(s), new ReplaceTaskDisciplinesRequest(task.ConcurrencyToken.ToString("N"), [new(s.Major, "PRIMARY")]));
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Mentor_cannot_touch_other_major_or_remove_it_and_ended_assignment_loses_access()
    {
        var s = await Seed(); using var factory = new Factory(database);
        using var leader = factory.CreateAuthenticatedClient(s.Users.Student); using var mentor = factory.CreateAuthenticatedClient(s.Users.NewLecturer, roles: "LECTURER");
        var before = (await leader.GetFromJsonAsync<TaskDisciplinesDto>(Disciplines(s)))!;
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.PutAsJsonAsync(Disciplines(s), new ReplaceTaskDisciplinesRequest(before.ConcurrencyToken, [new(s.OtherMajor, "PRIMARY")]))).StatusCode);
        var both = await Body<TaskDisciplinesDto>(await leader.PutAsJsonAsync(Disciplines(s), new ReplaceTaskDisciplinesRequest(before.ConcurrencyToken, [new(s.Major, "PRIMARY"), new(s.OtherMajor, "SUPPORTING")])));
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.PutAsJsonAsync(Disciplines(s), new ReplaceTaskDisciplinesRequest(both.ConcurrencyToken, [new(s.Major, "PRIMARY")]))).StatusCode);
        await using var db = database.CreateContext(); (await db.SupervisorAssignments.FindAsync(s.MentorAssignment))!.EndedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.GetAsync(Disciplines(s))).StatusCode);
    }

    [Fact]
    public async Task Same_task_token_has_one_winner_and_audit_failure_rolls_back()
    {
        var s = await Seed(); using var factory = new Factory(database); using var leader = factory.CreateAuthenticatedClient(s.Users.Student);
        var before = (await leader.GetFromJsonAsync<TaskDisciplinesDto>(Disciplines(s)))!;
        Assert.Equal("UNCLASSIFIED", before.Classification);
        var request = new ReplaceTaskDisciplinesRequest(before.ConcurrencyToken, [new(s.Major, "PRIMARY")]);
        var responses = await Task.WhenAll(leader.PutAsJsonAsync(Disciplines(s), request), leader.PutAsJsonAsync(Disciplines(s), request));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var saved = (await leader.GetFromJsonAsync<TaskDisciplinesDto>(Disciplines(s)))!;
        using var failing = new Factory(database, true); using var failedClient = failing.CreateAuthenticatedClient(s.Users.Student);
        Assert.Equal(HttpStatusCode.InternalServerError, (await failedClient.PutAsJsonAsync(Disciplines(s), new ReplaceTaskDisciplinesRequest(saved.ConcurrencyToken, [new(s.OtherMajor, "PRIMARY")]))).StatusCode);
        var after = (await leader.GetFromJsonAsync<TaskDisciplinesDto>(Disciplines(s)))!;
        Assert.Equal(saved.ConcurrencyToken, after.ConcurrencyToken); Assert.Equal(s.Major, Assert.Single(after.Items).MajorId);
    }

    [Theory]
    [InlineData("TASK")]
    [InlineData("DELIVERABLE")]
    [InlineData("MEETING")]
    [InlineData("PROGRESS_REPORT")]
    [InlineData("FILE")]
    public async Task Evidence_references_each_source_without_copying_and_retries_are_idempotent(string type)
    {
        var s = await Seed(); using var factory = new Factory(database); using var leader = factory.CreateAuthenticatedClient(s.Users.Student);
        await Classify(leader, s);
        var id = type switch { "TASK" => s.Task, "DELIVERABLE" => s.Deliverable, "MEETING" => s.Meeting, "PROGRESS_REPORT" => s.Report, _ => s.File };
        var request = new CreateProjectEvidenceRequest(type, id, s.Major, " Evidence ");
        var results = await Task.WhenAll(leader.PostAsJsonAsync(Evidence(s), request), leader.PostAsJsonAsync(Evidence(s), request));
        var first = await Body<ProjectEvidenceDto>(results[0]); var second = await Body<ProjectEvidenceDto>(results[1]);
        Assert.Equal(first.Id, second.Id); Assert.Equal("PENDING", first.VerificationStatus); Assert.Equal(s.Users.Student, first.SubmittedBy);
        var list = (await leader.GetFromJsonAsync<PagedResult<ProjectEvidenceDto>>(Evidence(s) + $"?sourceType={type}&majorId={s.Major}&pageSize=1"))!;
        Assert.Equal(1, list.TotalCount); Assert.Equal(first.Id, Assert.Single(list.Items).Id);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Evidence(s), request with { Notes = "Different" })).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.ProjectEvidence.CountAsync(x => x.ProjectId == s.Project));
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == "PROJECT_EVIDENCE_CREATED" && x.EntityId == first.Id.ToString()));
        Assert.Equal(1, await db.Files.CountAsync(x => x.TaskId == s.Task));
    }

    [Fact]
    public async Task Evidence_denies_cross_project_unassigned_member_and_nonactive_project()
    {
        var s = await Seed(); var foreign = await Seed(); using var factory = new Factory(database);
        using var leader = factory.CreateAuthenticatedClient(s.Users.Student); using var member = factory.CreateAuthenticatedClient(s.Member);
        using var unassigned = factory.CreateAuthenticatedClient(s.OtherMember);
        await Classify(leader, s);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.PostAsJsonAsync(Evidence(s), new CreateProjectEvidenceRequest("TASK", foreign.Task, s.Major))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unassigned.PostAsJsonAsync(Evidence(s), new CreateProjectEvidenceRequest("TASK", s.Task, s.Major))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync(Evidence(s), new CreateProjectEvidenceRequest("MEETING", s.Meeting, s.Major))).StatusCode);
        await Body<ProjectEvidenceDto>(await member.PostAsJsonAsync(Evidence(s), new CreateProjectEvidenceRequest("TASK", s.Task, s.Major)));
        await using var db = database.CreateContext(); (await db.Projects.FindAsync(s.Project))!.Status = "ARCHIVED"; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Evidence(s), new CreateProjectEvidenceRequest("FILE", s.File, s.Major))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await leader.GetAsync(Evidence(s))).StatusCode);
    }

    [Fact]
    public async Task File_evidence_preserves_source_and_download_authorization_and_audit_rollback()
    {
        var s = await Seed(); using var factory = new Factory(database); using var leader = factory.CreateAuthenticatedClient(s.Users.Student);
        using var outside = factory.CreateAuthenticatedClient(s.Users.OutsideStaff, roles: "DEPARTMENT_STAFF");
        await Classify(leader, s);
        var request = new CreateProjectEvidenceRequest("FILE", s.File, s.Major);
        using (var failing = new Factory(database, true))
        using (var client = failing.CreateAuthenticatedClient(s.Users.Student))
            Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync(Evidence(s), request)).StatusCode);
        await using (var db = database.CreateContext()) Assert.False(await db.ProjectEvidence.AnyAsync(x => x.ProjectId == s.Project));
        await Body<ProjectEvidenceDto>(await leader.PostAsJsonAsync(Evidence(s), request));
        Assert.Equal(HttpStatusCode.OK, (await leader.GetAsync($"/api/v1/files/{s.File}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync($"/api/v1/files/{s.File}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync(Evidence(s))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.DeleteAsync($"/api/v1/files/{s.File}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await leader.GetAsync($"/api/v1/files/{s.File}/download")).StatusCode);
    }

    [Fact]
    public async Task Interdisciplinary_task_creation_requires_primary_and_is_atomic_with_audit()
    {
        var s = await Seed(); using var factory = new Factory(database); using var leader = factory.CreateAuthenticatedClient(s.Users.Student);
        var request = new AIPMS.Application.Features.Tasks.Commands.CreateTaskCommand(s.Milestone, null, "New task", null, "MEDIUM", null, null, [s.Member]);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostAsJsonAsync("/api/v1/tasks", request)).StatusCode);
        request = request with { Disciplines = [new(s.Major, "PRIMARY")] };
        using (var failing = new Factory(database, true))
        using (var client = failing.CreateAuthenticatedClient(s.Users.Student))
            Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync("/api/v1/tasks", request)).StatusCode);
        await using (var db = database.CreateContext()) Assert.Equal(1, await db.Tasks.CountAsync(x => x.MilestoneId == s.Milestone));
        var created = await Body<TaskDto>(await leader.PostAsJsonAsync("/api/v1/tasks", request));
        var disciplines = (await leader.GetFromJsonAsync<TaskDisciplinesDto>($"/api/v1/tasks/{created.Id}/disciplines"))!;
        Assert.Equal(s.Major, Assert.Single(disciplines.Items).MajorId);
    }

    [Fact]
    public async Task Source_state_major_and_membership_are_rechecked_before_writes()
    {
        var s = await Seed(); using var factory = new Factory(database);
        using var leader = factory.CreateAuthenticatedClient(s.Users.Student);
        using var member = factory.CreateAuthenticatedClient(s.Member);
        await Classify(leader, s);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Evidence(s), new CreateProjectEvidenceRequest("TASK", s.Task, s.OtherMajor))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.PostAsJsonAsync(Evidence(s), new CreateProjectEvidenceRequest("TASK", long.MaxValue, null))).StatusCode);
        await using (var db = database.CreateContext())
        {
            (await db.Meetings.FindAsync(s.Meeting))!.Status = "CANCELLED";
            (await db.TeamMembers.SingleAsync(x => x.TeamId == s.Team && x.UserId == s.Member)).LeftAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Evidence(s), new CreateProjectEvidenceRequest("MEETING", s.Meeting, s.Major))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync(Evidence(s), new CreateProjectEvidenceRequest("TASK", s.Task, s.Major))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(Evidence(s))).StatusCode);
        await using (var db = database.CreateContext())
        {
            (await db.Users.FindAsync(s.Users.Student))!.Status = "INACTIVE"; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.GetAsync(Disciplines(s))).StatusCode);
    }

    [Fact]
    public async Task Evidence_pagination_is_stable_and_untrusted_fields_cannot_claim_verification()
    {
        var s = await Seed(); using var factory = new Factory(database); using var leader = factory.CreateAuthenticatedClient(s.Users.Student);
        var first = await Body<ProjectEvidenceDto>(await leader.PostAsJsonAsync(Evidence(s), new
        {
            sourceType = "TASK", sourceId = s.Task, majorId = (long?)null,
            verificationStatus = "VERIFIED", submittedBy = s.Users.Staff, submittedAt = DateTime.MinValue
        }));
        Assert.Equal("UNCLASSIFIED", first.Classification); Assert.Equal("PENDING", first.VerificationStatus);
        Assert.Equal(s.Users.Student, first.SubmittedBy); Assert.True(first.SubmittedAt > DateTime.UtcNow.AddMinutes(-5));
        var second = await Body<ProjectEvidenceDto>(await leader.PostAsJsonAsync(Evidence(s), new CreateProjectEvidenceRequest("MEETING", s.Meeting, s.Major)));
        await using (var db = database.CreateContext())
        {
            await db.ProjectEvidence.Where(x => x.ProjectId == s.Project).ExecuteUpdateAsync(set => set.SetProperty(x => x.SubmittedAt, first.SubmittedAt));
        }
        var page1 = (await leader.GetFromJsonAsync<PagedResult<ProjectEvidenceDto>>(Evidence(s) + "?page=1&pageSize=1"))!;
        var page2 = (await leader.GetFromJsonAsync<PagedResult<ProjectEvidenceDto>>(Evidence(s) + "?page=2&pageSize=1"))!;
        Assert.Equal(2, page1.TotalCount); Assert.Equal(second.Id, Assert.Single(page1.Items).Id); Assert.Equal(first.Id, Assert.Single(page2.Items).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.GetAsync(Evidence(s) + "?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.GetAsync(Evidence(s) + "?verificationStatus=VERIFIED")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(Evidence(s))).StatusCode);
    }

    [Fact]
    public async Task Single_major_creation_without_classification_preserves_existing_contract()
    {
        var s = await Seed(); await using (var db = database.CreateContext())
            await db.ProjectMajors.Where(x => x.ProjectId == s.Project && x.MajorId == s.OtherMajor).ExecuteDeleteAsync();
        using var factory = new Factory(database); using var leader = factory.CreateAuthenticatedClient(s.Users.Student);
        var request = new AIPMS.Application.Features.Tasks.Commands.CreateTaskCommand(s.Milestone, null, "Compatible task", null, "MEDIUM", null, null, []);
        var created = await Body<TaskDto>(await leader.PostAsJsonAsync("/api/v1/tasks", request));
        var disciplines = (await leader.GetFromJsonAsync<TaskDisciplinesDto>($"/api/v1/tasks/{created.Id}/disciplines"))!;
        Assert.Equal("UNCLASSIFIED", disciplines.Classification); Assert.Empty(disciplines.Items);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync($"/api/v1/tasks/{created.Id}/disciplines",
            new ReplaceTaskDisciplinesRequest(disciplines.ConcurrencyToken, [new(s.OtherMajor, "PRIMARY")]))).StatusCode);
    }

    [Fact]
    public async Task Scoped_supervisor_edits_revision_responsibilities_and_audit_failure_restores_all_tokens()
    {
        var s = await Seed(); await using (var db = database.CreateContext())
        {
            (await db.Projects.FindAsync(s.Project))!.Status = "REVISION_REQUIRED";
            (await db.Teams.FindAsync(s.Team))!.Status = "FORMING";
            db.Add(new TeamAcademicConfiguration { TeamId = s.Team, ProjectMode = "INTERDISCIPLINARY", LeadDepartmentId = s.Users.DepartmentId,
                ConcurrencyToken = Guid.NewGuid(), Requirements = [new() { TeamId = s.Team, MajorId = s.Major, MinMembers = 1, MaxMembers = 3, Responsibility = "SE" },
                    new() { TeamId = s.Team, MajorId = s.OtherMajor, MinMembers = 1, MaxMembers = 3, Responsibility = "Business" }] });
            await db.SaveChangesAsync();
        }
        using var factory = new Factory(database); using var mentor = factory.CreateAuthenticatedClient(s.Users.NewLecturer, roles: "LECTURER");
        var url = $"/api/v1/teams/{s.Team}/major-requirements/{s.Major}/responsibilities";
        var before = (await mentor.GetFromJsonAsync<ResponsibilityListDto>(url))!;
        var saved = await Body<ResponsibilityListDto>(await mentor.PutAsJsonAsync(url, new ReplaceResponsibilitiesRequest(before.ConcurrencyToken!, [new("Design", 0)])));
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.PutAsJsonAsync($"/api/v1/teams/{s.Team}/major-requirements/{s.OtherMajor}/responsibilities",
            new ReplaceResponsibilitiesRequest(saved.ConcurrencyToken!, [new("Other major", 0)]))).StatusCode);
        byte[] projectToken; Guid? version;
        await using (var db = database.CreateContext())
        {
            projectToken = (await db.Projects.FindAsync(s.Project))!.RowVersion;
            version = (await db.Set<TeamAcademicConfiguration>().FindAsync(s.Team))!.ResponsibilityVersion;
        }
        using (var failing = new Factory(database, true))
        using (var leader = failing.CreateAuthenticatedClient(s.Users.Student))
            Assert.Equal(HttpStatusCode.InternalServerError, (await leader.PutAsJsonAsync(url, new ReplaceResponsibilitiesRequest(saved.ConcurrencyToken!, [new("Must rollback", 0)]))).StatusCode);
        var after = (await mentor.GetFromJsonAsync<ResponsibilityListDto>(url))!;
        Assert.Equal(saved.ConcurrencyToken, after.ConcurrencyToken); Assert.Equal("Design", Assert.Single(after.Items).Content);
        await using (var db = database.CreateContext())
        {
            Assert.Equal(projectToken, (await db.Projects.FindAsync(s.Project))!.RowVersion);
            Assert.Equal(version, (await db.Set<TeamAcademicConfiguration>().FindAsync(s.Team))!.ResponsibilityVersion);
        }
    }

    [Fact]
    public async Task Swagger_exposes_new_routes_without_replacing_task_file_evidence()
    {
        using var factory = new Factory(database); using var client = factory.CreateClient();
        using var json = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = json.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/v1/teams/{teamId}/major-requirements/{majorId}/responsibilities").TryGetProperty("put", out _));
        Assert.True(paths.GetProperty("/api/v1/projects/{projectId}/major-requirements/{majorId}/responsibilities").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/tasks/{taskId}/disciplines").TryGetProperty("put", out _));
        Assert.True(paths.GetProperty("/api/v1/projects/{projectId}/evidence").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/v1/tasks/{taskId}/evidence").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/files/{id}/download").TryGetProperty("get", out _));
    }
}
