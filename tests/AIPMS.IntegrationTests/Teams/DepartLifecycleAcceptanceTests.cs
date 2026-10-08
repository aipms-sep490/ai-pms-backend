using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.StudentQualifications.DTOs;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Teams;

public sealed partial class InterdisciplinaryWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Depart_D08_qualification_to_archive_uses_real_api_transitions(bool interdisciplinary)
    {
        var s = await SeedAsync();
        var now = TeamDatabaseFixture.Now;
        long adminId, primaryId, replacementId, mentorId, evaluatorId, primaryProfile, replacementProfile, mentorProfile, finalPeriod, evaluationPeriod, invalidCertificateId;
        await using (var setup = database.CreateContext())
        {
            var roles = await setup.Roles.Where(r => r.Code == "ADMIN" || r.Code == "LECTURER").ToDictionaryAsync(r => r.Code);
            foreach (var code in new[] { "ADMIN", "LECTURER" })
                if (!roles.ContainsKey(code)) { var role = new Role { Code = code, Name = code, IsSystemRole = true }; setup.Add(role); roles.Add(code, role); }
            User Actor(string role, long department) => new() { Email = Guid.NewGuid().ToString("N") + "@depart.invalid",
                FullName = role, PasswordHash = Guid.NewGuid().ToString("N"), Status = "ACTIVE", DepartmentId = department,
                UserRoleUsers = [new() { Role = roles[role] }] };
            var admin = Actor("ADMIN", s.LeadDepartment);
            var primary = Actor("LECTURER", s.LeadDepartment);
            var replacementUser = Actor("LECTURER", s.LeadDepartment);
            var mentor = Actor("LECTURER", s.OtherDepartment);
            var evaluator = Actor("LECTURER", s.LeadDepartment);
            setup.AddRange(admin, primary, replacementUser, mentor, evaluator);
            var invalidCertificate = new AIPMS.Infrastructure.Persistence.Generated.Models.File { UploadedBy = s.Team.Students[0],
                OriginalFileName = "not-a-certificate.txt", MimeType = "text/plain", FileSizeBytes = 10, StoragePath = Guid.NewGuid().ToString("N") };
            setup.Add(invalidCertificate);
            await setup.SaveChangesAsync();
            invalidCertificateId = invalidCertificate.Id;
            (adminId, primaryId, replacementId, mentorId, evaluatorId) = (admin.Id, primary.Id, replacementUser.Id, mentor.Id, evaluator.Id);
            SupervisorProfile Profile(User user) => new() { UserId = user.Id, IsAvailable = true, MaxActiveProjects = 5 };
            var p = Profile(primary); var r = Profile(replacementUser); var m = Profile(mentor);
            m.SupervisorExpertises.Add(new() { ExpertiseName = "IS" });
            setup.AddRange(p, r, m);
            var template = new MilestoneTemplateVersion {
                MilestoneTemplate = new() { Name = "DEPART lifecycle", Status = "ACTIVE", CreatedBy = adminId },
                VersionNumber = 1, Status = "PUBLISHED", CreatedBy = adminId, LockedAt = now,
                Items = [new() { Title = "Delivery", StartOffsetDays = 0, DueOffsetDays = 7, SortOrder = 1 }] };
            setup.Add(template);
            await setup.SaveChangesAsync();
            (primaryProfile, replacementProfile, mentorProfile) = (p.Id, r.Id, m.Id);
            (await setup.ProjectPeriods.FindAsync(s.Team.PeriodId))!.MinDistinctMajors = interdisciplinary ? 2 : 1;
            ProjectPeriod Period(string type) => new() { AcademicSemesterId = s.Team.SemesterId, Code = type, Name = type,
                PeriodType = type, Status = "ACTIVE", StartAt = now.AddDays(-1), EndAt = now.AddDays(20), MaxProjectsPerSupervisor = 5 };
            var execution = Period("EXECUTION"); execution.MilestoneTemplateId = template.MilestoneTemplateId; execution.MilestoneTemplateVersionId = template.Id;
            var final = Period("FINAL_SUBMISSION"); var evaluation = Period("EVALUATION");
            setup.AddRange(Period("SUPERVISOR_SELECTION"), execution, final, evaluation);
            await setup.SaveChangesAsync();
            (finalPeriod, evaluationPeriod) = (final.Id, evaluation.Id);
        }
        var memoryStorage = new DepartLifecycleStorage();
        using var app = new TeamTestFactory(database, s.Team, customizeServices: services =>
        {
            services.RemoveAll<IFileStorage>(); services.AddSingleton<IFileStorage>(memoryStorage);
        });
        var memberId = s.Team.Students[interdisciplinary ? 4 : 1];
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]);
        using var member = app.CreateAuthenticatedClient(memberId);
        using var staff = app.CreateAuthenticatedClient(s.LeadStaff, roles: ["DEPARTMENT_STAFF"]);
        using var otherStaff = app.CreateAuthenticatedClient(s.OtherStaff, roles: ["DEPARTMENT_STAFF"]);
        using var adminClient = app.CreateAuthenticatedClient(adminId, roles: ["ADMIN"]);
        using var primaryClient = app.CreateAuthenticatedClient(primaryId, roles: ["LECTURER"]);
        using var mentorClient = app.CreateAuthenticatedClient(mentorId, roles: ["LECTURER"]);
        using var evaluatorClient = app.CreateAuthenticatedClient(evaluatorId, roles: ["LECTURER"]);
        using var anonymous = app.CreateClient();
        var policyUrl = $"/api/v1/academic/project-periods/{s.Team.PeriodId}/qualification-policy";
        var policy = new SetProjectPeriodQualificationPolicyRequest(true, "CAPSTONE_READINESS");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(policyUrl, policy)).StatusCode);
        await Body<ProjectPeriodQualificationPolicyDto>(await adminClient.PutAsJsonAsync(policyUrl, policy));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await leader.PostAsJsonAsync("/api/v1/student-qualifications/me/evidence",
            new SubmitStudentQualificationEvidenceRequest("CAPSTONE_READINESS", "TRAINING_COMPLETED", "INVALID-MIME", invalidCertificateId,
                now.AddDays(-1), now.AddYears(1)))).StatusCode);
        foreach (var (student, reviewer) in new[] { (leader, staff), (member, interdisciplinary ? otherStaff : staff) })
        {
            var qualification = await Body<StudentQualificationDto>(await student.PostAsJsonAsync("/api/v1/student-qualifications/me/evidence",
                new SubmitStudentQualificationEvidenceRequest("CAPSTONE_READINESS", "TRAINING_COMPLETED", "DEPART-CERT", null, now.AddDays(-1), now.AddYears(1))));
            var route = $"/api/v1/student-qualifications/{qualification.Id}";
            await Body<StudentQualificationDto>(await reviewer.PostAsJsonAsync(route + "/reject", new { reason = "Resubmit evidence", expectedConcurrencyToken = qualification.ConcurrencyToken }));
            var resubmitted = await Body<StudentQualificationDto>(await student.PostAsJsonAsync("/api/v1/student-qualifications/me/evidence",
                new SubmitStudentQualificationEvidenceRequest("CAPSTONE_READINESS", "TRAINING_COMPLETED", "DEPART-CERT-REVISED", null, now.AddDays(-1), now.AddYears(1))));
            Assert.Equal(HttpStatusCode.Conflict, (await reviewer.PostAsJsonAsync(route + "/verify", new { expectedConcurrencyToken = qualification.ConcurrencyToken })).StatusCode);
            await Body<StudentQualificationDto>(await reviewer.PostAsJsonAsync(route + "/verify", new { expectedConcurrencyToken = resubmitted.ConcurrencyToken }));
        }
        var scope = interdisciplinary ? Scope(s) : new TeamAcademicScopeRequest("SINGLE_MAJOR", s.Team.SeMajorId,
            s.LeadDepartment, [new(s.Team.SeMajorId, 2, 3, "Engineering")]);
        var team = await Body<TeamDto>(await leader.PostAsJsonAsync("/api/v1/teams", new { academicSemesterId = s.Team.SemesterId,
            code = "DEPART-LIFECYCLE", name = "DEPART acceptance", academicScope = scope }));
        var invite = await Invite(leader, team.Id, memberId);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invite.Id}/accept", null));
        var majors = interdisciplinary ? new[] { s.Team.SeMajorId, s.Team.IsMajorId } : [s.Team.SeMajorId];
        var project = await Body<ProjectDto>(await leader.PostAsJsonAsync("/api/v1/projects", new CreateProjectDraftRequest(
            "DEPART lifecycle", "Description", "Objectives", "Problem", "Output", majors, "Education", ["Dotnet"], ["Capstone"])));
        project = await Transition(staff, await Transition(leader, project, "submit"), "start-review");
        var review = await Review(leader, project.Id);
        if (interdisciplinary)
        {
            review = await Decide(otherStaff, project.Id, review);
            review = await Decide(staff, project.Id, review);
        }
        project = await Transition(staff, project with { ConcurrencyToken = review.ConcurrencyToken }, "approve");
        var supervisorRequest = await Body<SupervisorRequestDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{project.Id}/supervisor-requests", new { supervisorProfileId = primaryProfile }));
        var accepted = await Body<SupervisorRequestDto>(await primaryClient.PostAsJsonAsync($"/api/v1/supervisor-requests/{supervisorRequest.Id}/accept", new { responseMessage = "Accepted" }));
        var replacement = await Body<SupervisorAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/supervisor-assignments/{accepted.AssignmentId}/replace",
            new { supervisorProfileId = replacementProfile, reason = "Capacity handover" }));
        Assert.Equal(accepted.AssignmentId, replacement.ReplacesAssignmentId);
        if (interdisciplinary)
        {
            var request = await Body<SupervisorRequestDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{project.Id}/supervisor-requests",
                new { supervisorProfileId = mentorProfile, assignmentType = "DISCIPLINE_MENTOR", majorId = s.Team.IsMajorId }));
            var assignedMentor = await Body<SupervisorRequestDto>(await mentorClient.PostAsJsonAsync($"/api/v1/supervisor-requests/{request.Id}/accept", new { responseMessage = "Accepted" }));
            var mentorUrl = $"/api/v1/supervisor-assignments/{assignedMentor.AssignmentId}";
            var leadView = await Body<SupervisorAssignmentDto>(await staff.GetAsync(mentorUrl));
            Assert.All(leadView.AllowedActions!, action => Assert.False(action.Allowed));
            var departmentView = await Body<SupervisorAssignmentDto>(await otherStaff.GetAsync(mentorUrl));
            Assert.All(departmentView.AllowedActions!, action => Assert.True(action.Allowed));
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(mentorUrl + "/end", new { reason = "Outside mentor scope" })).StatusCode);
        }
        var governance = await Body<ProjectGovernanceDto>(await staff.GetAsync($"/api/v1/projects/{project.Id}/governance"));
        Assert.Equal("ACTIVE", governance.ProjectStatus);
        Assert.Equal("FROZEN_REGISTRATION_SNAPSHOT", governance.AcademicScopeProvenance);
        var item = await Body<DeliverableDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{project.Id}/deliverables",
            new SaveDeliverableRequest(null, "Final report", null, "REPORT", now.AddDays(1))));
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("DEPART final report")); file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "report.txt"); form.Add(new StringContent("0"), "expectedLatestVersion");
        var version = await Body<DeliverableVersionDto>(await leader.PostAsync($"/api/v1/deliverables/{item.Id}/versions", form));
        var draft = await Body<FinalSubmissionDraftDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{project.Id}/final-submission-draft",
            new CreateFinalSubmissionDraftRequest(finalPeriod, "Ready", [version.Id])));
        var finalUrl = $"/api/v1/projects/{project.Id}/final-submission";
        var requirements = await Body<FinalRequirementsDto>(await staff.PutAsJsonAsync(finalUrl + "/requirements", new ConfigureFinalRequirementsRequest([item.Id], null)));
        var submit = new SubmitFinalSubmissionRequest(draft.ConcurrencyToken, requirements.ConcurrencyToken!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(finalUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync(finalUrl, submit)).StatusCode);
        await Body<FinalSubmissionDto>(await leader.PostAsJsonAsync(finalUrl, submit));
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(finalUrl, submit)).StatusCode);
        var rubric = await Body<RubricDto>(await staff.PostAsJsonAsync("/api/v1/rubrics", new CreateRubricRequest(s.LeadDepartment,
            s.Team.SemesterId, "DEPART-" + Guid.NewGuid().ToString("N"), "Assessment", null, [new("Quality", null, 100, 10, 0, true)])));
        rubric = await Body<RubricDto>(await staff.PostAsJsonAsync($"/api/v1/rubrics/{rubric.Id}/publish", new ChangeRubricStatusRequest(rubric.ConcurrencyToken)));
        var scheme = await Body<EvaluationSchemeDto>(await adminClient.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(project.Id, evaluationPeriod, "DEPART scheme", 5, [new("Common", "COMMON", null, rubric.Id, 100, 100, 1)])));
        scheme = await Body<EvaluationSchemeDto>(await adminClient.PostAsJsonAsync($"/api/v1/evaluation-schemes/{scheme.Id}/publish", new SchemeTokenRequest(scheme.ConcurrencyToken)));
        var assignment = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{project.Id}/evaluation-assignments",
            new AssignEvaluatorRequest(evaluatorId, evaluationPeriod, "LECTURER", "COMMON", null, null, scheme.Components.Single().Id)));
        var evaluationDraft = await Body<EvaluationDraftDto>(await evaluatorClient.PostAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evaluation", null));
        var scoreUrl = $"/api/v1/evaluations/{evaluationDraft.Id}";
        Assert.Equal(HttpStatusCode.Conflict, (await evaluatorClient.PutAsJsonAsync(scoreUrl + "/draft",
            new SaveEvaluationDraftRequest(evaluationDraft.ConcurrencyToken, "Too high", [new(rubric.Criteria[0].Id, 11, null)]))).StatusCode);
        evaluationDraft = await Body<EvaluationDraftDto>(await evaluatorClient.PutAsJsonAsync(scoreUrl + "/draft",
            new SaveEvaluationDraftRequest(evaluationDraft.ConcurrencyToken, "Reviewed", [new(rubric.Criteria[0].Id, 8, null)])));
        await Body<EvaluationDraftDto>(await evaluatorClient.PostAsJsonAsync(scoreUrl + "/finalize", new { evaluationDraft.ConcurrencyToken }));
        foreach (var studentId in new[] { s.Team.Students[0], memberId })
        {
            var resultUrl = $"/api/v1/projects/{project.Id}/students/{studentId}/result";
            var preview = await Body<ProjectResultPreviewDto>(await adminClient.GetAsync(resultUrl + "/preview"));
            var result = await Body<StudentResultDto>(await adminClient.PostAsJsonAsync(resultUrl, new PublishProjectResultRequest(preview.ConfirmationToken)));
            Assert.Equal(8m, result.TotalScore);
        }
        var projectResultUrl = $"/api/v1/projects/{project.Id}/result";
        var projectPreview = await Body<ProjectResultPreviewDto>(await adminClient.GetAsync(projectResultUrl + "/preview"));
        if (interdisciplinary)
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(projectResultUrl,
                new PublishProjectResultRequest(projectPreview.ConfirmationToken))).StatusCode);
        await Body<ProjectResultDto>(await adminClient.PostAsJsonAsync(projectResultUrl, new PublishProjectResultRequest(projectPreview.ConfirmationToken)));
        project = await Body<ProjectDto>(await leader.GetAsync($"/api/v1/projects/{project.Id}"));
        Assert.Equal("COMPLETED", project.Status);
        Assert.NotNull(project.CompletedAt);
        project = await Body<ProjectDto>(await adminClient.PostAsJsonAsync($"/api/v1/projects/{project.Id}/archive", new ArchiveProjectRequest(project.ConcurrencyToken, "DEPART acceptance")));
        Assert.Equal("ARCHIVED", project.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync($"/api/v1/projects/{project.Id}/deliverables",
            new SaveDeliverableRequest(null, "Late write", null, "REPORT", now.AddDays(1)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync(finalUrl + "/files/9223372036854775807/download")).StatusCode);
        foreach (var format in new[] { "csv", "xlsx", "pdf" })
            Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/v1/dashboards/portfolio/export?format={format}")).StatusCode);
        await using var check = database.CreateContext();
        Assert.Equal("ARCHIVED", (await check.Projects.FindAsync(project.Id))!.Status);
        var history = await check.ProjectStatusHistories.Where(h => h.ProjectId == project.Id).Select(h => h.NewStatus).ToListAsync();
        foreach (var state in new[] { "SUBMITTED", "UNDER_REVIEW", "APPROVED", "ACTIVE", "FINAL_SUBMISSION", "COMPLETED", "ARCHIVED" }) Assert.Contains(state, history);
        Assert.True(await check.AuditLogs.AnyAsync(a => a.Action == "PROJECT_ARCHIVED" && a.EntityId == project.Id.ToString()));
    }

    private sealed class DepartLifecycleStorage : IFileStorage
    {
        private readonly ConcurrentDictionary<string, byte[]> objects = new();
        public async Task WriteAsync(string key, Stream stream, CancellationToken ct)
        {
            using var output = new MemoryStream(); await stream.CopyToAsync(output, ct);
            if (!objects.TryAdd(key, output.ToArray())) throw new IOException("Duplicate object");
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => objects.TryGetValue(key, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes)) : throw new FileNotFoundException();
        public Task DeleteAsync(string key, CancellationToken ct) { objects.TryRemove(key, out _); return Task.CompletedTask; }
    }
}
