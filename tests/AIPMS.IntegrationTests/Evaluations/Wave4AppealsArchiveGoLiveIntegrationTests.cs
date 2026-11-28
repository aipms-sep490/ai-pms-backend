using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Appeals;
using AIPMS.Application.Features.Chat;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class Wave4AppealsArchiveGoLiveIntegrationTests(EvaluationDraftDatabaseFixture database)
    : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed class Wave4ConfigurableFactory(
        EvaluationDraftDatabaseFixture database,
        bool appealsEnabled = true,
        bool peerEvalEnabled = false,
        bool requireTokens = false,
        bool realtimeEnabled = true,
        string allowedOrigin = "http://127.0.0.1:5230") : EvaluationFactory(database)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Appeals:Enabled"] = appealsEnabled.ToString(),
                ["Chat:Enabled"] = "true",
                ["PeerEvaluation:Enabled"] = peerEvalEnabled.ToString(),
                ["ExecutionConcurrency:RequireTokens"] = requireTokens.ToString(),
                ["ExecutionConcurrency:LogMissingTokens"] = "true",
                ["NotificationRealtime:Enabled"] = realtimeEnabled.ToString(),
                ["Cors:AllowedOrigins:0"] = allowedOrigin,
            }));
        }
    }

    private static async Task<T> ReadJson<T>(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, JsonOpts)!;
    }

    private static async Task<string?> ReadProblemCode(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString()
            : doc.RootElement.TryGetProperty("errorCode", out var ec) ? ec.GetString()
            : null;
    }

    [Fact]
    public async Task Feature_flags_appeals_enabled_and_peer_evaluation_disabled_enforce_q_x_51_boundary()
    {
        var s = await database.Seed(lockedSubmission: true, confirmedCommittee: true);
        using var app = new Wave4ConfigurableFactory(database, appealsEnabled: true, peerEvalEnabled: false);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);

        // Appeals:Enabled = true -> 200 OK
        var appealRes = await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/result/appeal-windows");
        Assert.Equal(HttpStatusCode.OK, appealRes.StatusCode);

        // PeerEvaluation:Enabled = false -> 404 NotFound (Q-X-51)
        var peerRes = await student.GetAsync($"/api/v1/projects/{s.ProjectId}/peer-evaluations/rounds");
        Assert.Equal(HttpStatusCode.NotFound, peerRes.StatusCode);
    }

    [Fact]
    public async Task Mobile_safe_notifications_hub_allows_empty_origin_blocks_bad_browser_origin_and_supports_delivery_ops()
    {
        var s = await database.Seed(lockedSubmission: false);
        using var app = new Wave4ConfigurableFactory(database, realtimeEnabled: true, allowedOrigin: "http://localhost:5173");
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer, roles: ["LECTURER"]);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin, roles: ["ADMIN"]);

        // 1. Mobile native request WITHOUT Origin + valid Bearer reaches SignalR negotiate (200)
        using var mobileNeg = new HttpRequestMessage(HttpMethod.Post, "/hubs/notifications/negotiate?negotiateVersion=1");
        mobileNeg.Headers.Authorization = student.DefaultRequestHeaders.Authorization;
        var mobileRes = await app.CreateClient().SendAsync(mobileNeg);
        Assert.Equal(HttpStatusCode.OK, mobileRes.StatusCode);

        // 2. Bad browser Origin returns 403 Forbidden
        using var badOriginReq = new HttpRequestMessage(HttpMethod.Post, "/hubs/notifications/negotiate?negotiateVersion=1");
        badOriginReq.Headers.TryAddWithoutValidation("Origin", "https://evil.example.invalid");
        badOriginReq.Headers.Authorization = student.DefaultRequestHeaders.Authorization;
        var badOriginRes = await app.CreateClient().SendAsync(badOriginReq);
        Assert.Equal(HttpStatusCode.Forbidden, badOriginRes.StatusCode);

        // 3. Unauthenticated request without token returns 401 Unauthorized
        var anonClient = app.CreateClient();
        var anonRes = await anonClient.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonRes.StatusCode);

        // 4. TK-W4-12 Email delivery ops: Admin & DepartmentStaff read/retry; Student & Lecturer forbidden (403)
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/v1/notifications/email-deliveries/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lecturer.GetAsync("/api/v1/notifications/email-deliveries/summary")).StatusCode);

        var staffSummary = await ReadJson<EmailDeliverySummaryDto>(await staff.GetAsync("/api/v1/notifications/email-deliveries/summary"));
        Assert.NotNull(staffSummary);
        var summary = await ReadJson<EmailDeliverySummaryDto>(await admin.GetAsync("/api/v1/notifications/email-deliveries/summary"));
        Assert.NotNull(summary);
        var retry = await ReadJson<RetryFailedEmailsResultDto>(await admin.PostAsync("/api/v1/notifications/email-deliveries/retry-failed", null));
        Assert.True(retry.RequeuedCount >= 0);

        // 5. Disabled realtime flag returns 503 ServiceUnavailable
        using var disabledHubApp = new Wave4ConfigurableFactory(database, realtimeEnabled: false);
        using var disabledClient = disabledHubApp.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);
        var disabledRes = await disabledClient.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabledRes.StatusCode);
    }

    [Fact]
    public async Task Require_tokens_true_rejects_missing_and_stale_concurrency_tokens_with_409()
    {
        var s = await database.Seed(lockedSubmission: false);
        long gateId;
        await using (var db = database.CreateContext())
        {
            var tpl = new ReviewGateTemplate
            {
                ProjectPeriodId = s.PeriodId,
                Code = "CONC_" + Guid.NewGuid().ToString("N")[..8],
                Name = "Concurrency Check Gate",
                Mode = "SINGLE_MAJOR",
                Major = "IT",
                Version = 1,
                Status = "PUBLISHED",
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = EvaluationDraftDatabaseFixture.Now,
                UpdatedAt = EvaluationDraftDatabaseFixture.Now,
            };
            db.ReviewGateTemplates.Add(tpl);
            await db.SaveChangesAsync();

            var gate = new ProjectGateInstance
            {
                ProjectId = s.ProjectId,
                TemplateId = tpl.Id,
                TemplateVersion = 1,
                Code = "G1",
                Sequence = 1,
                Title = "G1",
                Objective = "Check token",
                Status = "IN_PROGRESS",
                CreatedAt = EvaluationDraftDatabaseFixture.Now,
            };
            db.ProjectGateInstances.Add(gate);
            await db.SaveChangesAsync();
            gateId = gate.Id;
        }

        using var app = new Wave4ConfigurableFactory(database, requireTokens: true);
        using var primary = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        // Fetch primary supervisor for this project
        long primaryUserId;
        await using (var db = database.CreateContext())
        {
            primaryUserId = await db.SupervisorAssignments
                .Where(a => a.ProjectId == s.ProjectId && a.IsPrimary && a.EndedAt == null)
                .Select(a => a.SupervisorProfile.UserId)
                .SingleAsync();
        }
        using var supervisor = app.CreateAuthenticatedClient(primaryUserId);

        // First write creates the row (new row creation requires no pre-existing token)
        var initial = await ReadJson<GateIndividualProcessScoreItemDto>(await supervisor.PutAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/gates/{gateId}/individual-process-scores/{s.Scope.Users.Student}",
            new UpsertIndividualProcessScoreRequest(7.5m, "Initial draft")));

        // Under RequireTokens = true: updating existing row WITHOUT ConcurrencyToken -> 409 STALE_CONCURRENCY_TOKEN
        var missingTokenRes = await supervisor.PutAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/gates/{gateId}/individual-process-scores/{s.Scope.Users.Student}",
            new UpsertIndividualProcessScoreRequest(8.0m, "Missing token", ConcurrencyToken: null));
        Assert.Equal(HttpStatusCode.Conflict, missingTokenRes.StatusCode);
        Assert.Equal("STALE_CONCURRENCY_TOKEN", await ReadProblemCode(missingTokenRes));

        // Stale token -> 409 STALE_CONCURRENCY_TOKEN
        var staleRes = await supervisor.PutAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/gates/{gateId}/individual-process-scores/{s.Scope.Users.Student}",
            new UpsertIndividualProcessScoreRequest(8.0m, "Stale token", ConcurrencyToken: Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Conflict, staleRes.StatusCode);
        Assert.Equal("STALE_CONCURRENCY_TOKEN", await ReadProblemCode(staleRes));

        // Valid token succeeds
        var updated = await ReadJson<GateIndividualProcessScoreItemDto>(await supervisor.PutAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/gates/{gateId}/individual-process-scores/{s.Scope.Users.Student}",
            new UpsertIndividualProcessScoreRequest(8.0m, "Fresh token", ConcurrencyToken: initial.ConcurrencyToken)));
        Assert.Equal(8.0m, updated.Score);
    }

    [Fact]
    public async Task Department_daily_digest_emits_at_most_once_per_day_and_emergency_plus_result_xlsx_exports_respect_scope_and_privacy()
    {
        var s = await database.Seed(lockedSubmission: true, confirmedCommittee: true);
        long majorId, outsideStudentId, semesterId;
        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.Include(p => p.Team).Include(p => p.ProjectMajors).SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            semesterId = project.Team.AcademicSemesterId;
            majorId = project.ProjectMajors.First().MajorId;
            var studentUser = await db.Users.SingleAsync(u => u.Id == s.Scope.Users.Student);
            studentUser.Phone = "0901234567";
            outsideStudentId = s.Scope.Users.OutsideStaff;
            await db.SaveChangesAsync();
        }

        using var app = new Wave4ConfigurableFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var outsider = app.CreateAuthenticatedClient(outsideStudentId, roles: ["DEPARTMENT_STAFF"]);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);

        // 1. DepartmentDailyDigestService skips when 0 pending items, then emits once per UTC day and deduplicates 2nd run
        using (var scope = app.Services.CreateScope())
        {
            var digest = scope.ServiceProvider.GetRequiredService<IDepartmentDailyDigestService>();
            var zeroDay = new DateTime(2026, 12, 17, 10, 0, 0, DateTimeKind.Utc);
            Assert.Equal(0, await digest.SendDigestAsync(zeroDay));
        }

        await using (var pendingDb = database.CreateContext())
        {
            var gateTpl = new ReviewGateTemplate
            {
                ProjectPeriodId = s.PeriodId,
                Code = "DIG_" + Guid.NewGuid().ToString("N")[..8],
                Name = "Digest Trigger Gate",
                Mode = "SINGLE_MAJOR",
                Major = "IT",
                Version = 1,
                Status = "PUBLISHED",
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = EvaluationDraftDatabaseFixture.Now,
                UpdatedAt = EvaluationDraftDatabaseFixture.Now,
            };
            pendingDb.ReviewGateTemplates.Add(gateTpl);
            await pendingDb.SaveChangesAsync();

            pendingDb.ProjectGateInstances.Add(new ProjectGateInstance
            {
                ProjectId = s.ProjectId,
                TemplateId = gateTpl.Id,
                TemplateVersion = 1,
                Code = "G1",
                Sequence = 1,
                Title = "G1 Pending",
                Objective = "Digest count",
                Status = "IN_REVIEW",
                CreatedAt = EvaluationDraftDatabaseFixture.Now,
            });
            await pendingDb.SaveChangesAsync();
        }

        using (var scope = app.Services.CreateScope())
        {
            var digest = scope.ServiceProvider.GetRequiredService<IDepartmentDailyDigestService>();
            var now = new DateTime(2026, 12, 18, 10, 0, 0, DateTimeKind.Utc);
            var firstCount = await digest.SendDigestAsync(now);
            Assert.True(firstCount >= 1);
            var secondCount = await digest.SendDigestAsync(now.AddHours(2));
            Assert.Equal(0, secondCount);
        }

        // Restore project status to FINAL_SUBMISSION before configuring official EvaluationScheme
        await using (var fsDb = database.CreateContext())
        {
            var proj = await fsDb.Projects.SingleAsync(p => p.Id == s.ProjectId);
            proj.Status = "FINAL_SUBMISSION";
            await fsDb.SaveChangesAsync();
        }

        // 2. Emergency contacts export (TK-W4-12): Staff 200 XLSX; Student 403
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/v1/departments/me/emergency-contacts/export?semesterId={semesterId}")).StatusCode);
        var emergRes = await staff.GetAsync($"/api/v1/departments/me/emergency-contacts/export?semesterId={semesterId}");
        Assert.Equal(HttpStatusCode.OK, emergRes.StatusCode);
        var emergBytes = await emergRes.Content.ReadAsByteArrayAsync();
        using (var zip = new ZipArchive(new MemoryStream(emergBytes)))
        {
            Assert.NotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
        }

        // 3. Registrar / Dept 2-sheet XLSX export (TK-W4-13): publish result and verify 2 sheets + Student blocked (403)
        var schemeDraft = await ReadJson<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(
                s.ProjectId, s.PeriodId, "Registrar Export Scheme", 5.0m,
                [
                    new("Common", "COMMON", null, s.RubricId, 100m, 80m, 1, Stage: "ON_GOING", TargetScope: "PROJECT"),
                    new("Process", "INDIVIDUAL", majorId, s.RubricId, 0m, 20m, 1, Stage: "ON_GOING", TargetScope: "STUDENT")
                ],
                Policy: new(false, "APPROVED", "QD-EXPORT-2026", CalculationVersion: "LEGACY_V1"))));
        var scheme = await ReadJson<EvaluationSchemeDto>(await staff.PostAsJsonAsync(
            $"/api/v1/evaluation-schemes/{schemeDraft.Id}/publish", new SchemeTokenRequest(schemeDraft.ConcurrencyToken)));

        using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        foreach (var comp in scheme.Components)
        {
            var assign = await ReadJson<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(
                $"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
                new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "COMMITTEE", comp.Scope, comp.MajorId,
                    comp.Scope == "INDIVIDUAL" ? s.Scope.Users.Student : null, comp.Id)));
            var draft = await ReadJson<EvaluationDraftDto>(await evaluator.PostAsync($"/api/v1/evaluation-assignments/{assign.Id}/evaluation", null));
            draft = await ReadJson<EvaluationDraftDto>(await evaluator.PutAsJsonAsync($"/api/v1/evaluations/{draft.Id}/draft",
                new SaveEvaluationDraftRequest(draft.ConcurrencyToken, "For XLSX export", [new(s.Criteria[0], 9.0m, null), new(s.Criteria[1], 18.0m, null)])));
            await ReadJson<EvaluationDraftDto>(await evaluator.PostAsJsonAsync($"/api/v1/evaluations/{draft.Id}/finalize", new { draft.ConcurrencyToken }));
        }

        var prev = await ReadJson<ProjectResultPreviewDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/result/preview"));
        await ReadJson<BatchPublishResultDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/results/publish-batch", new BatchPublishResultRequest(prev.ConfirmationToken)));

        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/v1/departments/me/results/export?semesterId={semesterId}&format=xlsx")).StatusCode);
        var expRes = await staff.GetAsync($"/api/v1/departments/me/results/export?semesterId={semesterId}&format=xlsx");
        Assert.Equal(HttpStatusCode.OK, expRes.StatusCode);
        var expBytes = await expRes.Content.ReadAsByteArrayAsync();
        using (var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(expBytes)))
        {
            Assert.True(wb.TryGetWorksheet("Doi soat ket qua", out var ws));
            Assert.Equal("Mã đồ án", ws.Cell(1, 2).GetString());
            Assert.Equal("Điểm tổng kết", ws.Cell(1, 16).GetString());
            Assert.True(ws.RowsUsed().Count() >= 2);
        }

        // Cross-dept outsider sees 0 rows for s.ProjectId (isolation check)
        var outExpRes = await outsider.GetAsync($"/api/v1/departments/me/results/export?semesterId={semesterId}&format=csv");
        Assert.Equal(HttpStatusCode.OK, outExpRes.StatusCode);
        var outCsv = await outExpRes.Content.ReadAsStringAsync();
        Assert.DoesNotContain(s.ProjectId.ToString(), outCsv);
    }

    [Fact]
    public async Task Archive_blockers_lead_only_unarchive_7_section_dossier_and_direct_chat_readonly_trigger()
    {
        var s = await database.Seed(lockedSubmission: true, confirmedCommittee: true);
        long primaryUserId;
        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            primaryUserId = await db.SupervisorAssignments
                .Where(a => a.ProjectId == s.ProjectId && a.IsPrimary && a.EndedAt == null)
                .Select(a => a.SupervisorProfile.UserId)
                .SingleAsync();
            await db.SaveChangesAsync();
        }

        using var app = new Wave4ConfigurableFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var outsideStaff = app.CreateAuthenticatedClient(s.Scope.Users.OutsideStaff, roles: ["DEPARTMENT_STAFF"]);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin, roles: ["ADMIN"]);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);
        using var primary = app.CreateAuthenticatedClient(primaryUserId, roles: ["LECTURER"]);

        // 1. Archiving an ACTIVE (non-COMPLETED) project must fail with 409 ARCHIVE_NOT_ALLOWED
        var activeProj = await ReadJson<ProjectDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}"));
        var prematureArchive = await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/archive",
            new ArchiveProjectRequest(activeProj.ConcurrencyToken, "Too early"));
        Assert.Equal(HttpStatusCode.Conflict, prematureArchive.StatusCode);
        Assert.Equal("ARCHIVE_NOT_ALLOWED", await ReadProblemCode(prematureArchive));

        // 2. Open DIRECT chat between supervisor & student while ACTIVE -> succeeds
        var directChat = await ReadJson<ChatConversationDto>(await primary.PostAsJsonAsync("/api/v1/chat/conversations/direct",
            new ChatDirectRequest(s.Scope.Users.Student.ToString())));
        Assert.Equal("OPEN", directChat.Status);
        var beforeArchiveSend = await primary.PostAsJsonAsync($"/api/v1/chat/conversations/{directChat.Id}/messages",
            new ChatSendRequest(Guid.NewGuid(), "Check-in before archive"));
        Assert.Equal(HttpStatusCode.OK, beforeArchiveSend.StatusCode);

        // Transition to FINAL_SUBMISSION and finalize a valid published result so archive succeeds
        await using (var fsDb = database.CreateContext())
        {
            var project = await fsDb.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "FINAL_SUBMISSION";
            await fsDb.SaveChangesAsync();
        }
        var majorId = (await ReadJson<ProjectDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}"))).Majors.First().MajorId;
        var schemeDraft = await ReadJson<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(
                s.ProjectId, s.PeriodId, "Archive Dossier Scheme", 5.0m,
                [
                    new("Common", "COMMON", null, s.RubricId, 100m, 80m, 1, Stage: "ON_GOING", TargetScope: "PROJECT"),
                    new("Process", "INDIVIDUAL", majorId, s.RubricId, 0m, 20m, 1, Stage: "ON_GOING", TargetScope: "STUDENT")
                ],
                Policy: new(false, "APPROVED", "QD-ARC-2026", CalculationVersion: "LEGACY_V1"))));
        var scheme = await ReadJson<EvaluationSchemeDto>(await staff.PostAsJsonAsync(
            $"/api/v1/evaluation-schemes/{schemeDraft.Id}/publish", new SchemeTokenRequest(schemeDraft.ConcurrencyToken)));
        using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        foreach (var comp in scheme.Components)
        {
            var assign = await ReadJson<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(
                $"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
                new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "COMMITTEE", comp.Scope, comp.MajorId,
                    comp.Scope == "INDIVIDUAL" ? s.Scope.Users.Student : null, comp.Id)));
            var draft = await ReadJson<EvaluationDraftDto>(await evaluator.PostAsync($"/api/v1/evaluation-assignments/{assign.Id}/evaluation", null));
            draft = await ReadJson<EvaluationDraftDto>(await evaluator.PutAsJsonAsync($"/api/v1/evaluations/{draft.Id}/draft",
                new SaveEvaluationDraftRequest(draft.ConcurrencyToken, "Ready for archive", [new(s.Criteria[0], 8.5m, null), new(s.Criteria[1], 17.0m, null)])));
            await ReadJson<EvaluationDraftDto>(await evaluator.PostAsJsonAsync($"/api/v1/evaluations/{draft.Id}/finalize", new { draft.ConcurrencyToken }));
        }
        var prev = await ReadJson<ProjectResultPreviewDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/result/preview"));
        await ReadJson<BatchPublishResultDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/results/publish-batch", new BatchPublishResultRequest(prev.ConfirmationToken)));

        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "COMPLETED";
            await db.SaveChangesAsync();
        }

        // 3. Archive succeeds; non-lead/admin unarchive fails (403); Lead reads 7-section archive-dossier; DIRECT chat becomes READ_ONLY (409 CHAT_READ_ONLY)
        var completedProj = await ReadJson<ProjectDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}"));
        var archived = await ReadJson<ProjectDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/archive",
            new ArchiveProjectRequest(completedProj.ConcurrencyToken, "Completed full semester requirements")));
        Assert.Equal("ARCHIVED", archived.Status);

        var chatDetail = await ReadJson<ChatConversationDto>(await primary.GetAsync($"/api/v1/chat/conversations/{directChat.Id}"));
        Assert.Equal("READ_ONLY", chatDetail.Status);
        var sendBlocked = await primary.PostAsJsonAsync($"/api/v1/chat/conversations/{directChat.Id}/messages",
            new ChatSendRequest(Guid.NewGuid(), "Should fail after archive"));
        Assert.Equal(HttpStatusCode.Conflict, sendBlocked.StatusCode);
        Assert.Equal("CHAT_READ_ONLY", await ReadProblemCode(sendBlocked));

        var dossier = await ReadJson<ProjectArchiveDossierDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/archive-dossier"));
        Assert.Equal("ARCHIVED", dossier.Status);
        Assert.NotEmpty(dossier.Members);
        Assert.NotEmpty(dossier.Supervisors);
        Assert.NotNull(dossier.Results.ProjectResult);
        Assert.NotEmpty(dossier.Results.StudentResults);
        Assert.NotNull(dossier.FinalSubmission);
        Assert.NotNull(dossier.Appeals);
        Assert.NotEmpty(dossier.StatusHistory);
        // Primary supervisor also allowed to read archived dossier
        Assert.Equal(HttpStatusCode.OK, (await primary.GetAsync($"/api/v1/projects/{s.ProjectId}/archive-dossier")).StatusCode);
        // Outside staff blocked (403)
        Assert.Equal(HttpStatusCode.Forbidden, (await outsideStaff.GetAsync($"/api/v1/projects/{s.ProjectId}/archive-dossier")).StatusCode);

        // Admin & outsideStaff blocked from unarchiving (403)
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/unarchive",
            new UnarchiveProjectRequest(archived.ConcurrencyToken, "Admin cannot unarchive academic dossier"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsideStaff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/unarchive",
            new UnarchiveProjectRequest(archived.ConcurrencyToken, "Cross department staff cannot unarchive"))).StatusCode);

        // Lead Staff unarchives cleanly back to COMPLETED and writes AuditLog
        var unarchived = await ReadJson<ProjectDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/unarchive",
            new UnarchiveProjectRequest(archived.ConcurrencyToken, "Hậu kiểm bổ sung theo quyết định hội đồng Khoa")));
        Assert.Equal("COMPLETED", unarchived.Status);

        await using (var verifyDb = database.CreateContext())
        {
            Assert.True(await verifyDb.AuditLogs.AnyAsync(a => a.Action == "PROJECT_UNARCHIVED" && a.EntityId == s.ProjectId.ToString()));
        }
    }
}
