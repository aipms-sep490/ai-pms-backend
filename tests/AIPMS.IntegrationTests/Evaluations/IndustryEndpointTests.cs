using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Application.Features.Industry;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class PolicyEvaluationEndpointTests
{
    private const string IndustryRoot = "/api/v1/industry/assignments";
    private const string InviteRoot = "/api/v1/admin/industry/invitations";
    private const string EnrollRoot = "/api/v1/auth/industry/google";
    private sealed class IndustryFactory(EvaluationDraftDatabaseFixture database, bool enabled = true, bool failAudit = false) : EvaluationFactory(database, failAudit)
    {
        public AppealStorage Storage { get; } = new();
        public AppealClock Clock { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder); builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Industry:Enabled"] = enabled.ToString(), ["Industry:InvitationUrl"] = "https://example.invalid/industry/enroll",
                ["GoogleAuth:Enabled"] = "true", ["GoogleAuth:ClientId"] = "test.apps.googleusercontent.com", ["GoogleAuth:AllowedOrigins:0"] = "http://localhost:5173"
            }));
            builder.ConfigureServices(s => {
                s.RemoveAll<IFileStorage>(); s.AddSingleton<IFileStorage>(Storage);
                s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(Clock);
                s.RemoveAll<IGoogleIdentityVerifier>(); s.AddSingleton<IGoogleIdentityVerifier, IndustryVerifier>();
                s.RemoveAll<Microsoft.Extensions.Options.IConfigureOptions<RateLimiterOptions>>();
                s.Configure<RateLimiterOptions>(o => o.AddPolicy("authentication", _ => RateLimitPartition.GetNoLimiter("industry-test")));
            });
        }
    }
    private sealed class IndustryVerifier : IGoogleIdentityVerifier
    { public Task<GoogleIdentity> VerifyAsync(string idToken, CancellationToken ct) => Task.FromResult(JsonSerializer.Deserialize<GoogleIdentity>(idToken)!); }
    private static HttpClient IndustryBrowser(IndustryFactory app)
    { var client = app.CreateClient(); client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173"); return client; }
    private static async Task<GoogleChallenge> IndustryChallenge(HttpClient client) => await Body<GoogleChallenge>(await client.PostAsync(EnrollRoot + "/challenge", null));
    private static IndustryEnrollmentInput Enrollment(GoogleChallenge c, ExpertInvitationDto i, string token, string? email = null) =>
        new(c.ChallengeId, JsonSerializer.Serialize(new GoogleIdentity("expert-" + i.ExpertId, email ?? i.Email, c.Nonce, false)), token);
    private async Task<string> InvitationSecret(IndustryFactory app, ExpertInvitationDto invite)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<INotificationEmailQueue>();
        // Claim through the existing delivery queue; the public API never returns enrollment secrets.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var mail = await queue.ClaimNextAsync(app.Clock.Now, default, "INDUSTRY_INVITATION"); Assert.NotNull(mail);
            if (mail.ShouldSend) await queue.MarkSentAsync(mail.RecipientId, app.Clock.Now, default, mail.AttemptCount);
            if (mail.Email == invite.Email && mail.ShouldSend)
            {
                var token = Regex.Match(mail.Body, "#invitation=([A-Za-z0-9_-]+)").Groups[1].Value;
                Assert.Equal(64, token.Length); return token;
            }
        }
        throw new InvalidOperationException("Invitation delivery missing.");
    }
    private async Task<(ExpertInvitationDto Invite, string Secret)> InviteIndustry(IndustryFactory app, HttpClient admin)
    {
        var invite = await Body<ExpertInvitationDto>(await admin.PostAsJsonAsync(InviteRoot,
            new InviteExpertRequest($"expert-{Guid.NewGuid():N}@industry.invalid", "Synthetic expert", "Synthetic advisory company")));
        return (invite, await InvitationSecret(app, invite));
    }
    private static AssignIndustryRequest IndustryAssignmentInput(long expert, long? major = null) => new(expert, major.HasValue ? "MAJOR_SPECIFIC" : "COMMON", major,
        new(EvaluationDraftDatabaseFixture.Now.AddHours(-1)), new(EvaluationDraftDatabaseFixture.Now.AddDays(7)));
    private async Task<(long FileId, long VersionId)> IndustryFile(IndustryFactory app, EvaluationScenario s, string scope, long? major = null, bool locked = false)
    {
        await using var db = database.CreateContext();
        var version = new M.DeliverableVersion { Deliverable = new M.Deliverable { ProjectId = s.ProjectId, Title = "Advisory artifact", Status = "OPEN",
            CreatedBy = s.Scope.Users.Student, EvidenceScope = scope, TargetMajorId = major }, VersionNumber = 1, Status = "SUBMITTED",
            SubmittedBy = s.Scope.Users.Student, SubmittedAt = app.Clock.Now, CreatedAt = app.Clock.Now, UpdatedAt = app.Clock.Now };
        var file = new M.File { DeliverableVersion = version, UploadedBy = s.Scope.Users.Student, OriginalFileName = "advisory.txt", MimeType = "text/plain",
            StoragePath = Guid.NewGuid().ToString("N"), ChecksumSha256 = new string('a',64), FileSizeBytes = 5, CreatedAt = app.Clock.Now, UpdatedAt = app.Clock.Now };
        db.Add(file); await db.SaveChangesAsync();
        await app.Storage.WriteAsync(file.StoragePath, new MemoryStream(Encoding.UTF8.GetBytes("exact")), default);
        if (locked)
        {
            var final = await db.Set<FinalSubmission>().Include(f => f.Items).SingleAsync(f => f.ProjectId == s.ProjectId);
            final.Items.Add(new() { DeliverableId = version.DeliverableId, DeliverableVersionId = version.Id, Title = "Advisory artifact", VersionNumber = 1,
                StatusAtSubmission = "SUBMITTED", FilesJson = JsonSerializer.Serialize(new[] { new FinalSnapshotFile(
                    new(file.Id, "DELIVERABLE_VERSION", version.Id, file.OriginalFileName, file.MimeType, 5, file.ChecksumSha256, s.Scope.Users.Student, app.Clock.Now),
                    file.StoragePath, scope, major, null) }) });
            await db.SaveChangesAsync();
        }
        return (file.Id, version.Id);
    }
    [Fact]
    public async Task Industry_invitation_google_enrollment_is_mailbox_bound_single_use_and_has_no_academic_roles()
    {
        var (s, _) = await Seed(); using var app = new IndustryFactory(database); using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        var input = new InviteExpertRequest($"expert-{Guid.NewGuid():N}@industry.invalid", "Expert", "Company");
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync(InviteRoot, input)).StatusCode);
        using (var anonymous = app.CreateClient()) Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(InviteRoot, input)).StatusCode);
        var invited = await InviteIndustry(app, admin); using var browser = IndustryBrowser(app);
        var challenge = await IndustryChallenge(browser);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync(EnrollRoot + "/enroll", Enrollment(challenge, invited.Invite, invited.Secret, "other@industry.invalid"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync(EnrollRoot + "/enroll", Enrollment(challenge, invited.Invite, new string('x',64)))).StatusCode);
        using (var otherBrowser = IndustryBrowser(app))
        {
            await IndustryChallenge(otherBrowser);
            Assert.Equal(HttpStatusCode.Unauthorized, (await otherBrowser.PostAsJsonAsync(EnrollRoot + "/enroll", Enrollment(challenge, invited.Invite, invited.Secret))).StatusCode);
        }
        var enrolled = await Body<LoginResponse>(await browser.PostAsJsonAsync(EnrollRoot + "/enroll", Enrollment(challenge, invited.Invite, invited.Secret)));
        Assert.Equal(invited.Invite.ExpertId, enrolled.User.Id); Assert.Empty(enrolled.User.Roles);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync(EnrollRoot + "/enroll", Enrollment(challenge, invited.Invite, invited.Secret))).StatusCode);
        var normalChallenge = await Body<GoogleChallenge>(await browser.PostAsJsonAsync("/api/v1/auth/google/challenge", new { purpose = "LOGIN" }));
        var normal = await Body<LoginResponse>(await browser.PostAsJsonAsync("/api/v1/auth/google/login", new { normalChallenge.ChallengeId,
            IdToken = JsonSerializer.Serialize(new GoogleIdentity("expert-" + invited.Invite.ExpertId, invited.Invite.Email, normalChallenge.Nonce, false)) }));
        Assert.Empty(normal.User.Roles);
        await using var db = database.CreateContext();
        Assert.Empty(await db.UserRoles.Where(r => r.UserId == invited.Invite.ExpertId).ToArrayAsync());
        Assert.False((await db.Users.SingleAsync(u => u.Id == invited.Invite.ExpertId)).GoogleEnrollmentPending);
        Assert.Null((await db.Set<IndustryInvitation>().SingleAsync(i => i.Id == invited.Invite.Id)).ProtectedToken);
        var studentEmail = await db.Users.Where(u => u.Id == s.Scope.Users.Student).Select(u => u.Email).SingleAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(InviteRoot, input with { Email = studentEmail })).StatusCode);
        var role = await db.Roles.Where(r => r.Code == "LECTURER").Select(r => r.Id).SingleAsync();
        using var repositoryScope = app.Services.CreateScope();
        var users = repositoryScope.ServiceProvider.GetRequiredService<AIPMS.Application.Features.AccountSecurity.Abstractions.IUserAccountRepository>();
        await Assert.ThrowsAsync<AIPMS.Application.Common.Exceptions.ConflictException>(() => users.AssignRoleAsync(invited.Invite.ExpertId, role, s.Scope.Users.Admin, app.Clock.Now));
    }
    [Fact]
    public async Task Industry_actual_http_advisory_lifecycle_exact_shared_version_revocation_and_immutable_feedback()
    {
        var (s, _) = await Seed(); var evidence = new ApiAcceptanceEvidence("expansion-industry-lifecycle");
        using var app = new IndustryFactory(database) { AcceptanceEvidence = evidence }; using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff); using var outside = app.CreateAuthenticatedClient(s.Scope.Users.OutsideStaff);
        var invited = await InviteIndustry(app, admin); using var browser = IndustryBrowser(app);
        await Body<LoginResponse>(await browser.PostAsJsonAsync(EnrollRoot + "/enroll", Enrollment(await IndustryChallenge(browser), invited.Invite, invited.Secret)));
        using var expert = app.CreateAuthenticatedClient(invited.Invite.ExpertId);
        var url = $"/api/v1/projects/{s.ProjectId}/industry-assignments";
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(url, IndustryAssignmentInput(invited.Invite.ExpertId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync(url)).StatusCode);
        var row = await Body<IndustryAssignmentDto>(await staff.PostAsJsonAsync(url, IndustryAssignmentInput(invited.Invite.ExpertId)));
        var root = IndustryRoot + "/" + row.Id;
        var detail = await expert.GetStringAsync(root); Assert.DoesNotContain("roster", detail); Assert.DoesNotContain("score", detail); Assert.DoesNotContain("storage", detail);
        Assert.Empty((await Body<PagedResult<IndustryFileDto>>(await expert.GetAsync(root + "/evidence"))).Items);
        var file = await IndustryFile(app, s, "COMMON", locked:true);
        var share = await Body<IndustryFileDto>(await staff.PostAsJsonAsync(root + "/evidence", new ShareIndustryFileRequest(row.ConcurrencyToken, file.VersionId, file.FileId)));
        Assert.Equal("exact", await expert.GetStringAsync(share.DownloadUrl));
        await using (var db = database.CreateContext())
        {
            var original = await db.Files.SingleAsync(f => f.Id == file.FileId); original.StoragePath = "replacement-key"; await db.SaveChangesAsync();
            await app.Storage.WriteAsync("replacement-key", new MemoryStream(Encoding.UTF8.GetBytes("wrong")), default);
        }
        Assert.Equal("exact", await expert.GetStringAsync(share.DownloadUrl));
        row = await Body<IndustryAssignmentDto>(await expert.GetAsync(root));
        var feedback = await Body<IndustryFeedbackDto>(await expert.PostAsJsonAsync(root + "/feedback", new IndustryFeedbackRequest(row.ConcurrencyToken, 0, "Advisory observation")));
        Assert.Equal(1, feedback.Version); Assert.True(feedback.AdvisoryOnly);
        Assert.Equal(HttpStatusCode.Conflict, (await expert.PostAsJsonAsync(root + "/feedback", new IndustryFeedbackRequest(row.ConcurrencyToken, 0, "Stale"))).StatusCode);
        row = await Body<IndustryAssignmentDto>(await expert.GetAsync(root));
        var second = await Body<IndustryFeedbackDto>(await expert.PostAsJsonAsync(root + "/feedback", new IndustryFeedbackRequest(row.ConcurrencyToken, 1, "Advisory revision")));
        Assert.Equal(1, second.SupersedesVersion);
        Assert.Equal(2, (await Body<PagedResult<IndustryFeedbackDto>>(await staff.GetAsync(root + "/feedback"))).TotalCount);
        row = await Body<IndustryAssignmentDto>(await staff.GetAsync(root));
        row = await Body<IndustryAssignmentDto>(await staff.PostAsJsonAsync(root + $"/evidence/{share.Id}/revoke", new IndustryTokenRequest(row.ConcurrencyToken)));
        Assert.Equal(HttpStatusCode.NotFound, (await expert.GetAsync(share.DownloadUrl)).StatusCode);
        await Body<IndustryAssignmentDto>(await staff.PostAsJsonAsync(root + "/revoke", new IndustryTokenRequest(row.ConcurrencyToken)));
        Assert.Equal(HttpStatusCode.Forbidden, (await expert.GetAsync(root)).StatusCode);
        Assert.Empty((await Body<PagedResult<IndustryAssignmentDto>>(await expert.GetAsync(IndustryRoot))).Items);
        await using (var db = database.CreateContext())
        {
            Assert.Empty(await db.Evaluations.Where(e => e.EvaluatorId == invited.Invite.ExpertId).ToArrayAsync());
            Assert.Empty(await db.Set<StudentResult>().Where(r => r.ProjectId == s.ProjectId).ToArrayAsync());
            Assert.Equal(2, await db.Set<IndustryFeedback>().CountAsync(f => f.AssignmentId == row.Id));
            var audits = await db.AuditLogs.Where(a => a.ActorUserId == invited.Invite.ExpertId).CountAsync();
            await evidence.SaveAsync(new { assignmentRevoked = true, shareRevoked = true, feedbackVersions = 2, officialResults = 0, expertAuditCount = audits,
                enrollment = "fake Google verifier + invitation claimed from isolated email queue; no real mailbox/provider claim" });
        }
    }
    [Fact]
    public async Task Industry_scope_idor_disabled_accounts_and_concurrent_feedback_are_guarded()
    {
        var (s, major) = await Seed(); var (foreign, _) = await Seed(); using var app = new IndustryFactory(database);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin); using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        var invited = await InviteIndustry(app, admin); using var browser = IndustryBrowser(app);
        await Body<LoginResponse>(await browser.PostAsJsonAsync(EnrollRoot + "/enroll", Enrollment(await IndustryChallenge(browser), invited.Invite, invited.Secret)));
        using var expert = app.CreateAuthenticatedClient(invited.Invite.ExpertId);
        using var outsider = app.CreateAuthenticatedClient(s.Scope.Users.OtherLecturer);
        var url = $"/api/v1/projects/{s.ProjectId}/industry-assignments";
        var row = await Body<IndustryAssignmentDto>(await staff.PostAsJsonAsync(url, IndustryAssignmentInput(invited.Invite.ExpertId, major)));
        var root = IndustryRoot + "/" + row.Id;
        var common = await IndustryFile(app, s, "COMMON"); var own = await IndustryFile(app, s, "MAJOR_SPECIFIC", major);
        var foreignFile = await IndustryFile(app, foreign, "COMMON");
        await using (var db = database.CreateContext())
        {
            var otherMajor = await db.ProjectMajors.Where(m => m.ProjectId == foreign.ProjectId).Select(m => m.MajorId).SingleAsync();
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(url, IndustryAssignmentInput(invited.Invite.ExpertId, otherMajor))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(root + "/evidence", new ShareIndustryFileRequest(row.ConcurrencyToken, common.VersionId, common.FileId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PostAsJsonAsync(root + "/evidence", new ShareIndustryFileRequest(row.ConcurrencyToken, foreignFile.VersionId, foreignFile.FileId))).StatusCode);
        var share = await Body<IndustryFileDto>(await staff.PostAsJsonAsync(root + "/evidence", new ShareIndustryFileRequest(row.ConcurrencyToken, own.VersionId, own.FileId)));
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(share.DownloadUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await expert.GetAsync($"/api/v1/projects/{s.ProjectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await expert.GetAsync($"/api/v1/files/{own.FileId}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await expert.GetAsync(IndustryRoot + "?pageSize=101")).StatusCode);
        await using (var db = database.CreateContext())
        { var u = await db.Users.SingleAsync(u => u.Id == invited.Invite.ExpertId); u.Status = "INACTIVE"; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await expert.GetAsync(share.DownloadUrl)).StatusCode);
        await using (var db = database.CreateContext())
        { var u = await db.Users.SingleAsync(u => u.Id == invited.Invite.ExpertId); u.Status = "ACTIVE"; await db.SaveChangesAsync(); }
        row = await Body<IndustryAssignmentDto>(await expert.GetAsync(root));
        var input = new IndustryFeedbackRequest(row.ConcurrencyToken, 0, "Independent advisory review");
        using (var fail = new IndustryFactory(database, failAudit:true))
        using (var failExpert = fail.CreateAuthenticatedClient(invited.Invite.ExpertId))
            Assert.Equal(HttpStatusCode.InternalServerError, (await failExpert.PostAsJsonAsync(root + "/feedback", input)).StatusCode);
        await using (var db = database.CreateContext()) Assert.Equal(0, await db.Set<IndustryFeedback>().CountAsync(f => f.AssignmentId == row.Id));
        var responses = await Task.WhenAll(expert.PostAsJsonAsync(root + "/feedback", input), expert.PostAsJsonAsync(root + "/feedback", input));
        Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.OK)); Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        await using (var db = database.CreateContext())
        { var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId); project.Status = "ARCHIVED"; await db.SaveChangesAsync(); }
        row = await Body<IndustryAssignmentDto>(await expert.GetAsync(root)); Assert.DoesNotContain("SUBMIT_FEEDBACK", row.AllowedActions);
        Assert.Equal(HttpStatusCode.Conflict, (await expert.PostAsJsonAsync(root + "/feedback", new IndustryFeedbackRequest(row.ConcurrencyToken, 1, "Archived"))).StatusCode);
        app.Clock.Now = app.Clock.Now.AddDays(8);
        Assert.Equal(HttpStatusCode.Forbidden, (await expert.GetAsync(root)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await expert.GetAsync(share.DownloadUrl)).StatusCode);
    }
    [Fact]
    public async Task Industry_feature_invitation_revocation_expiry_and_audit_rollback_fail_closed()
    {
        var (s, _) = await Seed(); using var app = new IndustryFactory(database); using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin);
        using (var off = new IndustryFactory(database, enabled:false))
        using (var offAdmin = off.CreateAuthenticatedClient(s.Scope.Users.Admin)) Assert.Equal(HttpStatusCode.NotFound, (await offAdmin.GetAsync(InviteRoot)).StatusCode);
        var email = $"rollback-{Guid.NewGuid():N}@industry.invalid";
        using (var fail = new IndustryFactory(database, failAudit:true))
        using (var failAdmin = fail.CreateAuthenticatedClient(s.Scope.Users.Admin))
            Assert.Equal(HttpStatusCode.InternalServerError, (await failAdmin.PostAsJsonAsync(InviteRoot, new InviteExpertRequest(email, "Expert", "Company"))).StatusCode);
        await using (var db = database.CreateContext()) Assert.False(await db.Users.AnyAsync(u => u.Email == email));
        var invited = await InviteIndustry(app, admin); using var browser = IndustryBrowser(app); var challenge = await IndustryChallenge(browser);
        await Body<ExpertInvitationDto>(await admin.PostAsync(InviteRoot + $"/{invited.Invite.Id}/revoke", null));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync(EnrollRoot + "/enroll", Enrollment(challenge, invited.Invite, invited.Secret))).StatusCode);
        invited = await InviteIndustry(app, admin); challenge = await IndustryChallenge(browser);
        app.Clock.Now = app.Clock.Now.AddDays(4);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync(EnrollRoot + "/enroll", Enrollment(challenge, invited.Invite, invited.Secret))).StatusCode);
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AIPMS.Application.Features.Industry.Abstractions.IIndustryService>().Cleanup(default);
        await using (var db = database.CreateContext()) Assert.Null((await db.Set<IndustryInvitation>().SingleAsync(i => i.Id == invited.Invite.Id)).ProtectedToken);
    }
    [Fact]
    public async Task Industry_reissued_invitation_and_concurrent_enrollment_have_one_winner()
    {
        var (s, _) = await Seed(); using var app = new IndustryFactory(database); using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin);
        var invited = await InviteIndustry(app, admin);
        var reissued = await Body<ExpertInvitationDto>(await admin.PostAsJsonAsync(InviteRoot, new InviteExpertRequest(invited.Invite.Email, "Expert", "Company")));
        Assert.Equal(invited.Invite.ExpertId, reissued.ExpertId); var secret = await InvitationSecret(app, reissued);
        using var one = IndustryBrowser(app); using var two = IndustryBrowser(app);
        var challenge = await IndustryChallenge(one); var second = await IndustryChallenge(two);
        Assert.Equal(HttpStatusCode.Unauthorized, (await one.PostAsJsonAsync(EnrollRoot + "/enroll", Enrollment(challenge, invited.Invite, invited.Secret))).StatusCode);
        var responses = await Task.WhenAll(one.PostAsJsonAsync(EnrollRoot + "/enroll", Enrollment(challenge, reissued, secret)),
            two.PostAsJsonAsync(EnrollRoot + "/enroll", Enrollment(second, reissued, secret)));
        Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.OK)); Assert.Single(responses.Where(r => r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Conflict));
        await using var db = database.CreateContext(); Assert.Equal(1, await db.UserExternalLogins.CountAsync(l => l.UserId == reissued.ExpertId));
    }
    [Fact]
    public async Task Industry_interdisciplinary_expert_only_receives_explicit_assigned_major_files()
    {
        var (s, major) = await Seed(); long marketing;
        await using (var db = database.CreateContext())
        {
            var department = (await db.Users.FindAsync(s.Scope.Users.OutsideStaff))!.DepartmentId!.Value;
            var other = new M.Major { DepartmentId = department, Code = Guid.NewGuid().ToString("N"), Name = "Marketing", IsActive = true };
            db.Add(other); await db.SaveChangesAsync(); marketing = other.Id;
            db.ProjectMajors.Add(new() { ProjectId = s.ProjectId, MajorId = marketing }); await db.SaveChangesAsync();
            await AcademicSnapshotFixture.AddAsync(db, s.ProjectId, s.PeriodId, s.Scope.Users.DepartmentId, s.Scope.Users.Student, EvaluationDraftDatabaseFixture.Now);
        }
        using var app = new IndustryFactory(database); using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin);
        using var lead = app.CreateAuthenticatedClient(s.Scope.Users.Staff); using var participating = app.CreateAuthenticatedClient(s.Scope.Users.OutsideStaff);
        var invited = await InviteIndustry(app, admin); using var browser = IndustryBrowser(app);
        await Body<LoginResponse>(await browser.PostAsJsonAsync(EnrollRoot + "/enroll", Enrollment(await IndustryChallenge(browser), invited.Invite, invited.Secret)));
        using var expert = app.CreateAuthenticatedClient(invited.Invite.ExpertId);
        var row = await Body<IndustryAssignmentDto>(await lead.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/industry-assignments", IndustryAssignmentInput(invited.Invite.ExpertId, marketing)));
        var root = IndustryRoot + "/" + row.Id; var it = await IndustryFile(app, s, "MAJOR_SPECIFIC", major, true);
        var mkt = await IndustryFile(app, s, "MAJOR_SPECIFIC", marketing, true);
        Assert.Equal(HttpStatusCode.Forbidden, (await participating.PostAsJsonAsync(root + "/evidence", new ShareIndustryFileRequest(row.ConcurrencyToken, mkt.VersionId, mkt.FileId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lead.PostAsJsonAsync(root + "/evidence", new ShareIndustryFileRequest(row.ConcurrencyToken, it.VersionId, it.FileId))).StatusCode);
        var share = await Body<IndustryFileDto>(await lead.PostAsJsonAsync(root + "/evidence", new ShareIndustryFileRequest(row.ConcurrencyToken, mkt.VersionId, mkt.FileId)));
        var files = await Body<PagedResult<IndustryFileDto>>(await expert.GetAsync(root + "/evidence?pageSize=1"));
        Assert.Equal(1, files.TotalCount); Assert.Equal(mkt.FileId, Assert.Single(files.Items).FileId);
        Assert.Equal("exact", await expert.GetStringAsync(share.DownloadUrl));
        Assert.Equal(HttpStatusCode.NotFound, (await expert.GetAsync(root + "/evidence/999999/download")).StatusCode);
    }
}
