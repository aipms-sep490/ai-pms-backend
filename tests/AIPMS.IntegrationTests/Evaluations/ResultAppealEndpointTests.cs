using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Appeals;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class PolicyEvaluationEndpointTests
{
    private sealed class AppealFactory(EvaluationDraftDatabaseFixture database, bool enabled = true, bool failAudit = false) : EvaluationFactory(database, failAudit)
    {
        private readonly AppealStorage storage = new();
        public AppealClock Clock { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Appeals:Enabled"] = enabled.ToString() }));
            builder.ConfigureServices(s => { s.RemoveAll<IFileStorage>(); s.AddSingleton<IFileStorage>(storage);
                s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(Clock); });
        }
    }
    private sealed class AppealClock : TimeProvider
    { public DateTime Now { get; set; } = EvaluationDraftDatabaseFixture.Now; public override DateTimeOffset GetUtcNow() => new(Now); }
    private sealed class AppealStorage : IFileStorage
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> objects = new();
        public async Task WriteAsync(string key, Stream content, CancellationToken ct)
        { using var buffer = new MemoryStream(); await content.CopyToAsync(buffer, ct); if (!objects.TryAdd(key, buffer.ToArray())) throw new IOException(); }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => objects.TryGetValue(key, out var value)
            ? Task.FromResult<Stream>(new MemoryStream(value, false)) : throw new FileNotFoundException();
        public Task DeleteAsync(string key, CancellationToken ct) { objects.TryRemove(key, out _); return Task.CompletedTask; }
    }
    private async Task<(EvaluationScenario S, EvaluationSchemeDto Scheme, EvaluationDraftDto Common, EvaluationDraftDto Individual)> PublishedAppeal(AppealFactory app)
    {
        var (s, major) = await Seed(); using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var scheme = await Scheme(staff, s, major);
        var common = await Evaluate(staff, evaluator, s, scheme.Components.Single(c => c.Scope == "COMMON"), 8);
        var individual = await Evaluate(staff, evaluator, s, scheme.Components.Single(c => c.Scope == "INDIVIDUAL"), 6);
        foreach (var url in new[] { $"/api/v1/projects/{s.ProjectId}/students/{s.Scope.Users.Student}/result", $"/api/v1/projects/{s.ProjectId}/result" })
        {
            var preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(url + "/preview"));
            var response = await staff.PostAsJsonAsync(url, new PublishProjectResultRequest(preview.ConfirmationToken));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        return (s, scheme, common, individual);
    }
    private static string AppealUrl(long project) => $"/api/v1/projects/{project}/result";
    private static OpenAppealWindowRequest AppealWindow(long policy, long student, int version = 1) => new(policy,
        new(EvaluationDraftDatabaseFixture.Now.AddHours(-1)), new(EvaluationDraftDatabaseFixture.Now.AddDays(2)), [new("STUDENT", student, version)]);
    [Fact]
    public async Task Appeals_real_api_lifecycle_enforces_scope_two_people_atomic_publication_and_history()
    {
        var evidence = new ApiAcceptanceEvidence("expansion-appeal-lifecycle");
        using var app = new AppealFactory(database) { AcceptanceEvidence = evidence };
        var (s, scheme, _, individual) = await PublishedAppeal(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var reviewer = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin);
        using var outsider = app.CreateAuthenticatedClient(s.Scope.Users.OtherLecturer);
        using var wrongDepartment = app.CreateAuthenticatedClient(s.Scope.Users.OutsideStaff);
        var url = AppealUrl(s.ProjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(url + "/appeal-policy", new RecordAppealPolicyRequest(0, "SYNTHETIC-APPEAL-APPROVAL"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url + "/appeal-windows", AppealWindow(999, s.Scope.Users.Student))).StatusCode);
        var policy = await Body<AppealPolicyDto>(await staff.PostAsJsonAsync(url + "/appeal-policy", new RecordAppealPolicyRequest(0, "SYNTHETIC-APPEAL-APPROVAL")));
        var window = await Body<AppealWindowDto>(await staff.PostAsJsonAsync(url + "/appeal-windows", AppealWindow(policy.Id, s.Scope.Users.Student)));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url + "/appeal-windows", AppealWindow(policy.Id, s.Scope.Users.Student))).StatusCode);
        var studentWindows = await Body<PagedResult<AppealWindowDto>>(await student.GetAsync(url + "/appeal-windows"));
        Assert.Equal(s.Scope.Users.Student, Assert.Single(Assert.Single(studentWindows.Items).Targets).StudentId);
        var input = new CreateCorrectionRequest("STUDENT_APPEAL", policy.Id, window.Id, "STUDENT", s.Scope.Users.Student,
            1, scheme.Components.Single(c => c.Scope == "INDIVIDUAL").Id, "Please check criterion transcription");
        var row = await Body<CorrectionRequestDto>(await student.PostAsJsonAsync(url + "/correction-requests", input));
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync(url + "/correction-requests", input)).StatusCode);
        var requestUrl = url + $"/correction-requests/{row.Id}";
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(requestUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(requestUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await wrongDepartment.GetAsync(requestUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(requestUrl + "/evidence")).StatusCode);
        using (var upload = new MultipartFormDataContent())
        {
            upload.Add(new StringContent(row.ConcurrencyToken), "concurrencyToken");
            var bytes = new ByteArrayContent(Encoding.UTF8.GetBytes("Synthetic appeal evidence")); bytes.Headers.ContentType = new("text/plain");
            upload.Add(bytes, "file", "appeal.txt"); row = await Body<CorrectionRequestDto>(await student.PostAsync(requestUrl + "/files", upload));
        }
        var file = Assert.Single(row.Files);
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync(file.DownloadUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(file.DownloadUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(requestUrl + "/reviewer",
            new AssignAppealReviewerRequest(row.ConcurrencyToken, s.Scope.Users.Lecturer))).StatusCode);
        long primary;
        await using (var db = database.CreateContext()) primary = await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId && a.EndedAt == null).Select(a => a.SupervisorProfile.UserId).SingleAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(requestUrl + "/reviewer", new AssignAppealReviewerRequest(row.ConcurrencyToken, primary))).StatusCode);
        row = await Body<CorrectionRequestDto>(await staff.PostAsJsonAsync(requestUrl + "/reviewer", new AssignAppealReviewerRequest(row.ConcurrencyToken, s.Scope.Users.NewLecturer)));
        Assert.Equal(HttpStatusCode.OK, (await reviewer.GetAsync(requestUrl)).StatusCode);
        var context = await Body<AppealReviewContextDto>(await reviewer.GetAsync(requestUrl + "/review"));
        Assert.Equal(individual.Id, Assert.Single(context.Evaluations).Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(requestUrl + "/review")).StatusCode);
        var review = new ReviewAppealRequest(row.ConcurrencyToken, "AMEND", "Correct first criterion", [new(individual.Id, s.Criteria[0], 10, "Transcription")]);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.PutAsJsonAsync(requestUrl + "/review", review with {
            Amendments = [new(long.MaxValue, s.Criteria[0], 10, "Foreign evaluation")] })).StatusCode);
        row = await Body<CorrectionRequestDto>(await reviewer.PutAsJsonAsync(requestUrl + "/review", review));
        Assert.Equal(HttpStatusCode.Conflict, (await reviewer.PutAsJsonAsync(requestUrl + "/review", review)).StatusCode);
        var preview = await Body<AppealPreviewDto>(await staff.GetAsync(requestUrl + "/preview"));
        Assert.Equal(s.Scope.Users.Student, Assert.Single(preview.AffectedResults).StudentId);
        var decision = new AppealDecisionRequest(row.ConcurrencyToken, "ACCEPTED", "Independent validation complete", preview.ConfirmationToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.PostAsJsonAsync(requestUrl + "/decision", decision)).StatusCode);
        using (var failing = new AppealFactory(database, failAudit: true))
        using (var failedStaff = failing.CreateAuthenticatedClient(s.Scope.Users.Staff))
            Assert.Equal(HttpStatusCode.InternalServerError, (await failedStaff.PostAsJsonAsync(requestUrl + "/decision", decision)).StatusCode);
        await using (var db = database.CreateContext()) { Assert.False(await db.Set<ResultRevision>().AnyAsync(r => r.ProjectId == s.ProjectId)); Assert.Equal("UNDER_REVIEW", (await db.Set<ResultAppealRequest>().FindAsync(row.Id))!.Status); }
        var decisions = await Task.WhenAll(staff.PostAsJsonAsync(requestUrl + "/decision", decision), staff.PostAsJsonAsync(requestUrl + "/decision", decision));
        Assert.Single(decisions.Where(r => r.IsSuccessStatusCode)); Assert.Single(decisions.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        var accepted = await Body<CorrectionRequestDto>(decisions.Single(r => r.IsSuccessStatusCode)); Assert.Equal("ACCEPTED", accepted.Status);
        var result = await Body<StudentResultDto>(await student.GetAsync($"/api/v1/projects/{s.ProjectId}/students/{s.Scope.Users.Student}/result"));
        Assert.Equal(8.12m, result.TotalScore); Assert.Equal(2, result.Version); Assert.Equal("{}", result.SnapshotJson);
        var project = await Body<ProjectResultDto>(await staff.GetAsync(url)); Assert.Equal(8m, project.TotalScore); Assert.Equal(1, project.Version);
        var history = await Body<PagedResult<AppealEventDto>>(await student.GetAsync(requestUrl + "/history")); Assert.Equal(5, history.TotalCount);
        Assert.All(history.Items, e => { Assert.Null(e.BundleId); Assert.Empty(e.Amendments); });
        var reviewerHistory = await Body<PagedResult<AppealEventDto>>(await reviewer.GetAsync(requestUrl + "/history"));
        Assert.Single(reviewerHistory.Items.Single(e => e.Action == "APPEAL_REVIEWED").Amendments);
        await using (var db = database.CreateContext())
        {
            Assert.Equal("COMPLETED", (await db.Projects.FindAsync(s.ProjectId))!.Status);
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "APPEAL_DECIDED" && a.EntityId == row.Id.ToString()));
        }
        await evidence.SaveAsync(new { resultVersion = 2, studentScore = 8.12m, originalProjectScore = 8m,
            source = "STUDENT_APPEAL", historyEvents = 5, concurrentWinners = 1, auditRollback = true, originalResultsUnchanged = true });
    }

    [Fact]
    public async Task Appeals_staff_correction_reassignment_revocation_and_archived_stale_version_are_guarded()
    {
        using var app = new AppealFactory(database);
        var (s, scheme, common, _) = await PublishedAppeal(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var reviewer = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        var url = AppealUrl(s.ProjectId);
        await using (var db = database.CreateContext()) { (await db.Projects.FindAsync(s.ProjectId))!.Status = "ARCHIVED"; await db.SaveChangesAsync(); }
        var policy = await Body<AppealPolicyDto>(await staff.PostAsJsonAsync(url + "/appeal-policy", new RecordAppealPolicyRequest(0, "SYNTHETIC-POLICY")));
        var window = await Body<AppealWindowDto>(await staff.PostAsJsonAsync(url + "/appeal-windows", AppealWindow(policy.Id, s.Scope.Users.Student)));
        var studentInput = new CreateCorrectionRequest("STUDENT_APPEAL", policy.Id, window.Id, "STUDENT", s.Scope.Users.Student, 1,
            scheme.Components.Single(c => c.Scope == "COMMON").Id, "Check common score");
        var studentRow = await Body<CorrectionRequestDto>(await student.PostAsJsonAsync(url + "/correction-requests", studentInput));
        var staffInput = studentInput with { Source = "STAFF_CORRECTION", WindowId = null, TargetType = "PROJECT", StudentId = null };
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync(url + "/correction-requests", staffInput)).StatusCode);
        var row = await Body<CorrectionRequestDto>(await staff.PostAsJsonAsync(url + "/correction-requests", staffInput));
        var path = url + $"/correction-requests/{row.Id}";
        Assert.Equal("STAFF_CORRECTION", row.Source);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(path + "/reviewer", new AssignAppealReviewerRequest(row.ConcurrencyToken, s.Scope.Users.OtherLecturer))).StatusCode);
        row = await Body<CorrectionRequestDto>(await staff.PostAsJsonAsync(path + "/reviewer", new AssignAppealReviewerRequest(row.ConcurrencyToken, s.Scope.Users.NewLecturer)));
        var evidenceFiles = await Body<AppealFileDto[]>(await reviewer.GetAsync(path + "/evidence"));
        Assert.Equal("report.txt", Assert.Single(evidenceFiles).FileName);
        using (var scope = app.Services.CreateScope())
        using (var content = new MemoryStream(Encoding.UTF8.GetBytes("locked-original")))
            await scope.ServiceProvider.GetRequiredService<IFileStorage>().WriteAsync("fixture-object", content, default);
        Assert.Equal("locked-original", await (await reviewer.GetAsync(evidenceFiles[0].DownloadUrl)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.GetAsync(path + $"/evidence/{long.MaxValue}")).StatusCode);
        await using (var db = database.CreateContext()) { (await db.Users.FindAsync(s.Scope.Users.NewLecturer))!.Status = "INACTIVE"; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.GetAsync(path + "/evidence")).StatusCode);
        await using (var db = database.CreateContext()) { (await db.Users.FindAsync(s.Scope.Users.NewLecturer))!.Status = "ACTIVE"; await db.SaveChangesAsync(); }
        var reviewInput = new ReviewAppealRequest(row.ConcurrencyToken, "AMEND", "Fix common criterion", [new(common.Id, s.Criteria[0], 10, "Transcription")]);
        row = await Body<CorrectionRequestDto>(await reviewer.PutAsJsonAsync(path + "/review", reviewInput));
        // Reassigning invalidates the old reviewer immediately, including an existing valid JWT.
        row = await Body<CorrectionRequestDto>(await staff.PostAsJsonAsync(path + "/reviewer", new AssignAppealReviewerRequest(row.ConcurrencyToken, s.Scope.Users.Staff)));
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.GetAsync(path + "/evidence")).StatusCode);
        row = await Body<CorrectionRequestDto>(await staff.PutAsJsonAsync(path + "/review", reviewInput with { ConcurrencyToken = row.ConcurrencyToken }));
        Assert.DoesNotContain("ACCEPT", row.AllowedActions);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(path + "/decision", new AppealDecisionRequest(row.ConcurrencyToken, "ACCEPTED", "Self approval denied", "unused"))).StatusCode);
        row = await Body<CorrectionRequestDto>(await staff.PostAsJsonAsync(path + "/reviewer", new AssignAppealReviewerRequest(row.ConcurrencyToken, s.Scope.Users.NewLecturer)));
        row = await Body<CorrectionRequestDto>(await reviewer.PutAsJsonAsync(path + "/review", reviewInput with { ConcurrencyToken = row.ConcurrencyToken }));
        var preview = await Body<AppealPreviewDto>(await staff.GetAsync(path + "/preview")); Assert.Equal(2, preview.AffectedResults.Count);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.GetAsync(path + "/preview")).StatusCode);
        await Body<CorrectionRequestDto>(await staff.PostAsJsonAsync(path + "/decision", new AppealDecisionRequest(row.ConcurrencyToken, "ACCEPTED", "Independent check", preview.ConfirmationToken)));
        var stalePath = url + $"/correction-requests/{studentRow.Id}";
        var stale = await Body<CorrectionRequestDto>(await staff.GetAsync(stalePath)); Assert.Contains("RESULT_REVISION_STALE", stale.Blockers);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(stalePath + "/reviewer", new AssignAppealReviewerRequest(stale.ConcurrencyToken, s.Scope.Users.NewLecturer))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync(stalePath + "/decision", new AppealDecisionRequest(stale.ConcurrencyToken, "REJECTED", "", null))).StatusCode);
        var rejected = await Body<CorrectionRequestDto>(await staff.PostAsJsonAsync(stalePath + "/decision", new AppealDecisionRequest(stale.ConcurrencyToken, "REJECTED", "Review new version explicitly", null)));
        Assert.Equal("REJECTED", rejected.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync(url + "/correction-requests", studentInput)).StatusCode);
        await using var check = database.CreateContext(); Assert.Equal("ARCHIVED", (await check.Projects.FindAsync(s.ProjectId))!.Status);
        Assert.True(await check.Set<NotificationEmailDelivery>().AnyAsync());
    }

    [Fact]
    public async Task Appeals_feature_auth_policy_window_and_target_fail_closed()
    {
        using var app = new AppealFactory(database);
        var (s, scheme, _, _) = await PublishedAppeal(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var anon = app.CreateClient();
        using var disabled = new AppealFactory(database, enabled: false);
        using var disabledStaff = disabled.CreateAuthenticatedClient(s.Scope.Users.Staff);
        var url = AppealUrl(s.ProjectId);
        Assert.Equal(HttpStatusCode.NotFound, (await disabledStaff.GetAsync(url + "/appeal-policy")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(url + "/correction-requests")).StatusCode);
        var policy = await Body<AppealPolicyDto>(await staff.PostAsJsonAsync(url + "/appeal-policy", new RecordAppealPolicyRequest(0, "SYNTHETIC-POLICY")));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url + "/appeal-policy", new RecordAppealPolicyRequest(0, "stale"))).StatusCode);
        var future = AppealWindow(policy.Id, s.Scope.Users.Student) with { OpensAt = new(EvaluationDraftDatabaseFixture.Now.AddHours(1)) };
        var window = await Body<AppealWindowDto>(await staff.PostAsJsonAsync(url + "/appeal-windows", future));
        var input = new CreateCorrectionRequest("STUDENT_APPEAL", policy.Id, window.Id, "STUDENT", s.Scope.Users.Student, 1, scheme.Components[0].Id, "Review");
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync(url + "/correction-requests", input)).StatusCode);
        await Body<AppealWindowDto>(await staff.PostAsJsonAsync(url + $"/appeal-windows/{window.Id}/close", new AppealTokenRequest(window.ConcurrencyToken)));
        window = await Body<AppealWindowDto>(await staff.PostAsJsonAsync(url + "/appeal-windows", AppealWindow(policy.Id, s.Scope.Users.Student)));
        input = input with { WindowId = window.Id };
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync(url + "/correction-requests", input with { StudentId = s.Scope.Users.OtherLecturer })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PostAsJsonAsync(url + "/correction-requests", input with { TargetType = "", StudentId = null })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await student.PostAsJsonAsync(url + "/correction-requests", input with { ComponentId = long.MaxValue })).StatusCode);
        var row = await Body<CorrectionRequestDto>(await student.PostAsJsonAsync(url + "/correction-requests", input));
        var list = await Body<PagedResult<CorrectionRequestDto>>(await student.GetAsync(url + "/correction-requests")); Assert.Equal(1, list.TotalCount);
        app.Clock.Now = EvaluationDraftDatabaseFixture.Now.AddDays(3);
        var expired = await Body<CorrectionRequestDto>(await student.GetAsync(url + $"/correction-requests/{row.Id}"));
        Assert.DoesNotContain("UPLOAD", expired.AllowedActions); Assert.Contains("APPEAL_WINDOW_CLOSED", expired.Blockers);
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync(url + "/correction-requests", input)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync(AppealUrl(s.ProjectId) + $"/correction-requests/{long.MaxValue}")).StatusCode);
    }

    [Fact]
    public async Task Appeals_interdisciplinary_staff_and_reviewer_only_read_assigned_major_and_locked_file()
    {
        var (s, major) = await Seed(); long otherStudent; long otherMajor; long otherRubric; long[] otherCriteria;
        await using (var db = database.CreateContext())
        {
            var department = (await db.Users.FindAsync(s.Scope.Users.OutsideStaff))!.DepartmentId!.Value;
            var majorRow = new M.Major { DepartmentId = department, Code = Guid.NewGuid().ToString("N"), Name = "Marketing", IsActive = true };
            var person = new M.User { DepartmentId = department, Major = majorRow, Email = Guid.NewGuid()+"@appeal.invalid", FullName = "Synthetic Marketing Student",
                PasswordHash = "unused", Status = "ACTIVE", AcademicProfileStatus = "VERIFIED", UserRoleUsers = [new() { RoleId = await db.Roles.Where(r => r.Code == "STUDENT").Select(r => r.Id).SingleAsync() }] };
            db.Add(person); await db.SaveChangesAsync(); otherStudent = person.Id; otherMajor = majorRow.Id;
            var project = (await db.Projects.FindAsync(s.ProjectId))!;
            db.ProjectMajors.Add(new() { ProjectId = s.ProjectId, MajorId = otherMajor });
            db.TeamMembers.Add(new() { TeamId = project.TeamId, AcademicSemesterId = s.Scope.SemesterId, UserId = person.Id });
            var original = await db.Rubrics.Include(r => r.RubricCriteria).SingleAsync(r => r.Id == s.RubricId);
            var rubric = new M.Rubric { DepartmentId = department, AcademicSemesterId = s.Scope.SemesterId, Code = Guid.NewGuid().ToString("N"), Name = "Marketing rubric",
                CreatedBy = s.Scope.Users.OutsideStaff, IsActive = true, RubricCriteria = original.RubricCriteria.OrderBy(c => c.SortOrder).Select(c => new M.RubricCriterion {
                    CriterionId = c.CriterionId, WeightPercent = c.WeightPercent, MaxScore = c.MaxScore, SortOrder = c.SortOrder, IsRequired = c.IsRequired }).ToArray() };
            db.Add(rubric); await db.SaveChangesAsync(); otherRubric = rubric.Id; otherCriteria = rubric.RubricCriteria.OrderBy(c => c.SortOrder).Select(c => c.Id).ToArray();
            db.Add(new RubricVersion { RubricId = rubric.Id, RootRubricId = rubric.Id, VersionNumber = 1, Status = "PUBLISHED", ConcurrencyToken = Guid.NewGuid() });
            await db.SaveChangesAsync();
            await AcademicSnapshotFixture.AddAsync(db, s.ProjectId, s.PeriodId, s.Scope.Users.DepartmentId, s.Scope.Users.Student, EvaluationDraftDatabaseFixture.Now);
            var package = await db.Set<FinalSubmission>().Include(f => f.Items).SingleAsync(f => f.ProjectId == s.ProjectId);
            var item = package.Items.Single();
            item.FilesJson = JsonSerializer.Serialize(new[] {
                new FinalSnapshotFile(new(501, "DELIVERABLE_VERSION", item.DeliverableVersionId, "it.txt", "text/plain", 2, new string('a',64), s.Scope.Users.Student, EvaluationDraftDatabaseFixture.Now), "it-key", "MAJOR_SPECIFIC", major),
                new FinalSnapshotFile(new(502, "DELIVERABLE_VERSION", item.DeliverableVersionId, "marketing.txt", "text/plain", 9, new string('b',64), otherStudent, EvaluationDraftDatabaseFixture.Now), "marketing-key", "MAJOR_SPECIFIC", otherMajor) });
            await db.SaveChangesAsync();
        }
        using var app = new AppealFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var participating = app.CreateAuthenticatedClient(s.Scope.Users.OutsideStaff);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var marketingStudent = app.CreateAuthenticatedClient(otherStudent);
        var draft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes", Input(s, major) with { Components = [
            new("Common", "COMMON", null, s.RubricId, 50, 50, 1), new("IT", "MAJOR_SPECIFIC", major, s.RubricId, 25, 50, 1),
            new("Marketing", "MAJOR_SPECIFIC", otherMajor, otherRubric, 25, 50, 1)] }));
        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{draft.Id}/publish", new SchemeTokenRequest(draft.ConcurrencyToken)));
        foreach (var c in scheme.Components)
        {
            var id = c.MajorId == otherMajor ? s.Scope.Users.OtherLecturer : s.Scope.Users.Lecturer;
            using var evaluator = app.CreateAuthenticatedClient(id);
            var assignment = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments", new AssignEvaluatorRequest(id, s.PeriodId, "LECTURER", c.Scope, c.MajorId, null, c.Id)));
            var evaluation = await Body<EvaluationDraftDto>(await evaluator.PostAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evaluation", null));
            var criteria = c.MajorId == otherMajor ? otherCriteria : s.Criteria;
            evaluation = await Body<EvaluationDraftDto>(await evaluator.PutAsJsonAsync($"/api/v1/evaluations/{evaluation.Id}/draft", new SaveEvaluationDraftRequest(evaluation.ConcurrencyToken, null, [new(criteria[0], 8, null), new(criteria[1], 16, null)])));
            await Body<EvaluationDraftDto>(await evaluator.PostAsJsonAsync($"/api/v1/evaluations/{evaluation.Id}/finalize", new { evaluation.ConcurrencyToken }));
        }
        foreach (var id in new[] { s.Scope.Users.Student, otherStudent, 0L })
        {
            var route = id == 0 ? AppealUrl(s.ProjectId) : $"/api/v1/projects/{s.ProjectId}/students/{id}/result";
            var p = await Body<ProjectResultPreviewDto>(await staff.GetAsync(route + "/preview"));
            Assert.True((await staff.PostAsJsonAsync(route, new PublishProjectResultRequest(p.ConfirmationToken))).IsSuccessStatusCode);
        }
        var url = AppealUrl(s.ProjectId);
        var policy = await Body<AppealPolicyDto>(await staff.PostAsJsonAsync(url + "/appeal-policy", new RecordAppealPolicyRequest(0, "SYNTHETIC-INTERDISCIPLINARY-POLICY")));
        var window = await Body<AppealWindowDto>(await staff.PostAsJsonAsync(url + "/appeal-windows", AppealWindow(policy.Id, s.Scope.Users.Student) with { Targets = [new("STUDENT", s.Scope.Users.Student, 1), new("STUDENT", otherStudent, 1)] }));
        async Task<CorrectionRequestDto> Request(HttpClient client, long studentId, long majorId) => await Body<CorrectionRequestDto>(await client.PostAsJsonAsync(url + "/correction-requests",
            new CreateCorrectionRequest("STUDENT_APPEAL", policy.Id, window.Id, "STUDENT", studentId, 1, scheme.Components.Single(c => c.MajorId == majorId).Id, "Review major component")));
        var it = await Request(student, s.Scope.Users.Student, major); var marketing = await Request(marketingStudent, otherStudent, otherMajor);
        Assert.Equal(HttpStatusCode.Forbidden, (await participating.GetAsync(url + $"/correction-requests/{it.Id}")).StatusCode);
        var scoped = await Body<PagedResult<CorrectionRequestDto>>(await participating.GetAsync(url + "/correction-requests?pageSize=1"));
        Assert.Equal(1, scoped.TotalCount); Assert.Equal(marketing.Id, Assert.Single(scoped.Items).Id);
        var path = url + $"/correction-requests/{marketing.Id}";
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(path + "/reviewer", new AssignAppealReviewerRequest(marketing.ConcurrencyToken, s.Scope.Users.NewLecturer))).StatusCode);
        marketing = await Body<CorrectionRequestDto>(await staff.PostAsJsonAsync(path + "/reviewer", new AssignAppealReviewerRequest(marketing.ConcurrencyToken, s.Scope.Users.OutsideStaff)));
        var context = await Body<AppealReviewContextDto>(await participating.GetAsync(path + "/review"));
        Assert.Equal(otherRubric, Assert.Single(context.Evaluations).RubricId);
        var evidence = await Body<AppealFileDto[]>(await participating.GetAsync(path + "/evidence")); Assert.Equal(502, Assert.Single(evidence).FileId);
        Assert.Equal(HttpStatusCode.NotFound, (await participating.GetAsync(path + "/evidence/501")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await participating.GetAsync(path + "/preview")).StatusCode);
    }
}
