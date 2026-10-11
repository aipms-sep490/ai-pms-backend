using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class ColdSubmissionEndpointTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static DateTime Now => EvaluationDraftDatabaseFixture.Now;
    private static async Task<T> Body<T>(HttpResponseMessage response)
    { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>())!; }
    private static string ChecklistUrl(long projectId) => $"/api/v1/projects/{projectId}/cold/checklist";
    private static string DraftUrl(long itemId) => $"/api/v1/cold-checklist-items/{itemId}/draft";
    private static string SubmitUrl(long id) => $"/api/v1/cold-submissions/{id}/submit";
    private static string LockUrl(long projectId) => $"/api/v1/projects/{projectId}/cold/lock";
    private async Task<(EvaluationScenario Scenario, ColdChecklistDto Checklist, ColdTemplateDto Template)> Prepare(ColdFactory app, string scope = "MAJOR_SPECIFIC", bool finalPackage = false)
    {
        var s = await database.Seed(finalPackage);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        await using var db = database.CreateContext();
        var major = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();
        var template = await Body<ColdTemplateDto>(await staff.PostAsJsonAsync("/api/v1/cold-report-templates",
            new CreateColdTemplateRequest(major, s.PeriodId, "Configured major report", [new("REPORT", "Major report")])));
        var checklist = await Body<ColdChecklistDto>(await staff.PostAsJsonAsync(ChecklistUrl(s.ProjectId),
            new ApplyColdChecklistRequest(s.PeriodId, new(Now.AddHours(4)), [new(template.Id, scope, scope == "INDIVIDUAL" ? s.Scope.Users.Student : null)])));
        return (s, checklist, template);
    }
    private static Task<HttpResponseMessage> UploadResponse(HttpClient client, ColdSubmissionDto draft, string name = "report.pdf", string mime = "application/pdf", string text = "%PDF-1.7\nSynthetic report")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text)); file.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(file, "file", name); form.Add(new StringContent(draft.ConcurrencyToken), "concurrencyToken");
        return Send();
        async Task<HttpResponseMessage> Send() { using (form) return await client.PostAsync($"/api/v1/cold-submissions/{draft.Id}/files", form); }
    }
    private static async Task<ColdSubmissionDto> SubmitReport(HttpClient student, long itemId, int expected = 0)
    {
        var draft = await Body<ColdSubmissionDto>(await student.PostAsJsonAsync(DraftUrl(itemId), new SaveColdDraftRequest(expected, "Evidence")));
        draft = await Body<ColdSubmissionDto>(await UploadResponse(student, draft));
        return await Body<ColdSubmissionDto>(await student.PostAsJsonAsync(SubmitUrl(draft.Id), new ColdTokenRequest(draft.ConcurrencyToken)));
    }

    [Theory]
    [InlineData("MAJOR_SPECIFIC")]
    [InlineData("INDIVIDUAL")]
    public async Task Actual_api_draft_upload_submit_resubmit_lock_keeps_exact_version_and_private_download(string scope)
    {
        using var app = new ColdFactory(database);
        var (s, checklist, template) = await Prepare(app, scope);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var item = Assert.Single(checklist.Items);
        Assert.False(checklist.CanLock);
        var first = await SubmitReport(student, item.Id);
        Assert.Equal("SUBMITTED", first.Status);
        var second = await SubmitReport(student, item.Id, first.Version);
        Assert.Equal(2, second.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(LockUrl(s.ProjectId), new ColdTokenRequest(checklist.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(LockUrl(s.ProjectId), new ColdTokenRequest(checklist.ConcurrencyToken))).StatusCode);
        checklist = await Body<ColdChecklistDto>(await staff.GetAsync(ChecklistUrl(s.ProjectId)));
        Assert.True(checklist.CanLock);
        checklist = await Body<ColdChecklistDto>(await staff.PostAsJsonAsync(LockUrl(s.ProjectId), new ColdTokenRequest(checklist.ConcurrencyToken)));
        Assert.Equal("LOCKED", checklist.Status);
        Assert.Equal(second.Id, checklist.Items.Single().Latest!.Id);
        Assert.Equal("LOCKED", checklist.Items.Single().Latest!.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync(DraftUrl(item.Id), new SaveColdDraftRequest(2))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await UploadResponse(student, first)).StatusCode);
        var file = second.Files.Single();
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/api/v1/files/{file.Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lecturer.GetAsync(file.DownloadUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(file.DownloadUrl)).StatusCode);
        await using (var db = database.CreateContext())
        {
            var live = (await db.Files.FindAsync(file.Id))!; live.StoragePath = "replaced-live-key"; live.OriginalFileName = "replaced.pdf";
            (await db.Set<ColdReportTemplate>().FindAsync(template.Id))!.Name = "Changed live template";
            await db.SaveChangesAsync();
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "COLD_PACKAGE_LOCKED" && a.EntityId == checklist.Id.ToString()));
            Assert.Equal("SUBMITTED", (await db.Set<ColdSubmission>().FindAsync(first.Id))!.Status);
            Assert.Equal("FINAL_SUBMISSION", (await db.Projects.FindAsync(s.ProjectId))!.Status);
        }
        var downloaded = await student.GetAsync(file.DownloadUrl);
        Assert.Equal("%PDF-1.7\nSynthetic report", await downloaded.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", downloaded.Content.Headers.ContentType!.MediaType);
        Assert.Equal("Major report", (await Body<ColdChecklistDto>(await staff.GetAsync(ChecklistUrl(s.ProjectId)))).Items.Single().Name);
        var history = await Body<PagedResult<ColdSubmissionDto>>(await student.GetAsync($"/api/v1/cold-checklist-items/{item.Id}/history?pageSize=1"));
        Assert.Equal(2, history.TotalCount); Assert.Single(history.Items);
        Assert.DoesNotContain("StorageKey", JsonSerializer.Serialize(history));
        await database.Migrate(); await database.Migrate();
        Assert.Equal("LOCKED", (await Body<ColdChecklistDto>(await staff.GetAsync(ChecklistUrl(s.ProjectId)))).Status);
    }

    [Fact]
    public async Task Concurrent_submit_and_lock_keep_one_transition_and_stale_drafts_fail()
    {
        using var app = new ColdFactory(database); var (s, checklist, _) = await Prepare(app);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student); using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        var item = checklist.Items.Single();
        var drafts = await Task.WhenAll(student.PostAsJsonAsync(DraftUrl(item.Id), new SaveColdDraftRequest(0)), student.PostAsJsonAsync(DraftUrl(item.Id), new SaveColdDraftRequest(0)));
        Assert.Single(drafts, r => r.IsSuccessStatusCode); Assert.Single(drafts, r => r.StatusCode == HttpStatusCode.Conflict);
        var draft = await Body<ColdSubmissionDto>(drafts.Single(r => r.IsSuccessStatusCode));
        var stale = draft;
        draft = await Body<ColdSubmissionDto>(await UploadResponse(student, draft));
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync(SubmitUrl(draft.Id), new ColdTokenRequest(stale.ConcurrencyToken))).StatusCode);
        var submitted = await Task.WhenAll(student.PostAsJsonAsync(SubmitUrl(draft.Id), new ColdTokenRequest(draft.ConcurrencyToken)),
            student.PostAsJsonAsync(SubmitUrl(draft.Id), new ColdTokenRequest(draft.ConcurrencyToken)));
        Assert.Single(submitted, r => r.IsSuccessStatusCode); Assert.Single(submitted, r => r.StatusCode == HttpStatusCode.Conflict);
        checklist = await Body<ColdChecklistDto>(await staff.GetAsync(ChecklistUrl(s.ProjectId)));
        var locked = await Task.WhenAll(staff.PostAsJsonAsync(LockUrl(s.ProjectId), new ColdTokenRequest(checklist.ConcurrencyToken)),
            staff.PostAsJsonAsync(LockUrl(s.ProjectId), new ColdTokenRequest(checklist.ConcurrencyToken)));
        Assert.Single(locked, r => r.IsSuccessStatusCode); Assert.Single(locked, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("report.txt", "text/plain", "plain")]
    [InlineData("report.pdf", "text/plain", "%PDF-")]
    [InlineData("report.pdf", "application/pdf", "not a pdf")]
    [InlineData("../report.pdf", "application/pdf", "%PDF-")]
    public async Task Unsupported_or_forged_files_are_rejected_without_metadata(string name, string mime, string text)
    {
        using var app = new ColdFactory(database); var (s, checklist, _) = await Prepare(app);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        var draft = await Body<ColdSubmissionDto>(await student.PostAsJsonAsync(DraftUrl(checklist.Items.Single().Id), new SaveColdDraftRequest(0)));
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadResponse(student, draft, name, mime, text)).StatusCode);
        Assert.Empty(app.Storage.Objects);
        await using var db = database.CreateContext(); Assert.False(await db.Set<ColdSubmissionFile>().AnyAsync(f => f.SubmissionId == draft.Id));
    }

    [Fact]
    public async Task Deadline_missing_content_and_audit_failure_do_not_partially_submit_or_lock()
    {
        using var app = new ColdFactory(database); var (s, checklist, _) = await Prepare(app);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student); using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        var draft = await Body<ColdSubmissionDto>(await student.PostAsJsonAsync(DraftUrl(checklist.Items.Single().Id), new SaveColdDraftRequest(0)));
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync(SubmitUrl(draft.Id), new ColdTokenRequest(draft.ConcurrencyToken))).StatusCode);
        app.FailAction = "COLD_FILE_ATTACHED";
        Assert.Equal(HttpStatusCode.InternalServerError, (await UploadResponse(student, draft)).StatusCode);
        Assert.Empty(app.Storage.Objects);
        app.FailAction = null;
        draft = await Body<ColdSubmissionDto>(await UploadResponse(student, draft));
        app.FailAction = "COLD_SUBMITTED";
        Assert.Equal(HttpStatusCode.InternalServerError, (await student.PostAsJsonAsync(SubmitUrl(draft.Id), new ColdTokenRequest(draft.ConcurrencyToken))).StatusCode);
        app.FailAction = null;
        app.Clock.Now = checklist.Deadline;
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync(SubmitUrl(draft.Id), new ColdTokenRequest(draft.ConcurrencyToken))).StatusCode);
        app.Clock.Now = Now;
        draft = await Body<ColdSubmissionDto>(await student.PostAsJsonAsync(SubmitUrl(draft.Id), new ColdTokenRequest(draft.ConcurrencyToken)));
        checklist = await Body<ColdChecklistDto>(await staff.GetAsync(ChecklistUrl(s.ProjectId)));
        app.FailAction = "COLD_PACKAGE_LOCKED";
        Assert.Equal(HttpStatusCode.InternalServerError, (await staff.PostAsJsonAsync(LockUrl(s.ProjectId), new ColdTokenRequest(checklist.ConcurrencyToken))).StatusCode);
        app.FailAction = null;
        app.Storage.Objects.Clear();
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(LockUrl(s.ProjectId), new ColdTokenRequest(checklist.ConcurrencyToken))).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal("OPEN", (await db.Set<ColdPackage>().FindAsync(checklist.Id))!.Status);
        Assert.Equal("SUBMITTED", (await db.Set<ColdSubmission>().FindAsync(draft.Id))!.Status);
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "COLD_PACKAGE_LOCKED" && a.EntityId == checklist.Id.ToString()));
    }

    [Fact]
    public async Task Foreign_department_template_or_student_target_and_archived_writes_fail_closed()
    {
        using var app = new ColdFactory(database); var (s, checklist, template) = await Prepare(app, "INDIVIDUAL");
        using var outside = app.CreateAuthenticatedClient(s.Scope.Users.OutsideStaff);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(ChecklistUrl(s.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync(ChecklistUrl(s.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.PostAsJsonAsync("/api/v1/cold-report-templates",
            new CreateColdTemplateRequest(template.MajorId, s.PeriodId, "Outsider", [new("REPORT", "No")]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync(LockUrl(s.ProjectId), new ColdTokenRequest(checklist.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync($"/api/v1/cold-report-templates?majorId={template.MajorId}&projectPeriodId={s.PeriodId}&pageSize=101")).StatusCode);
        await using (var db = database.CreateContext()) { (await db.Projects.FindAsync(s.ProjectId))!.Status = "ARCHIVED"; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync(DraftUrl(checklist.Items.Single().Id), new SaveColdDraftRequest(0))).StatusCode);
        Assert.False((await Body<ColdChecklistDto>(await staff.GetAsync(ChecklistUrl(s.ProjectId)))).CanLock);
    }

    private sealed class ColdFactory(EvaluationDraftDatabaseFixture database) : EvaluationFactory(database)
    {
        public MemoryStorage Storage { get; } = new();
        public TestClock Clock { get; } = new();
        public string? FailAction { get; set; }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => {
                services.RemoveAll<IFileStorage>(); services.AddSingleton<IFileStorage>(Storage);
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Clock);
                var existing = services.Single(d => d.ServiceType == typeof(IAuditTrail)); services.Remove(existing);
                services.AddScoped<IAuditTrail>(p => new FailingAudit((IAuditTrail)ActivatorUtilities.CreateInstance(p, existing.ImplementationType!), this));
            });
        }
    }
    private sealed class TestClock : TimeProvider
    { public DateTime Now { get; set; } = ColdSubmissionEndpointTests.Now; public override DateTimeOffset GetUtcNow() => new(Now); }
    private sealed class FailingAudit(IAuditTrail inner, ColdFactory app) : IAuditTrail
    {
        public async Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
        { await inner.RecordAsync(entry, ct); if (entry.Action == app.FailAction) throw new InvalidOperationException("Injected COLD audit failure"); }
    }
    private sealed class MemoryStorage : IFileStorage
    {
        public ConcurrentDictionary<string, byte[]> Objects { get; } = new();
        public async Task WriteAsync(string key, Stream content, CancellationToken ct)
        { using var buffer = new MemoryStream(); await content.CopyToAsync(buffer, ct); if (!Objects.TryAdd(key, buffer.ToArray())) throw new IOException(); }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Objects.TryGetValue(key, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes, false)) : throw new FileNotFoundException();
        public Task DeleteAsync(string key, CancellationToken ct) { Objects.TryRemove(key, out _); return Task.CompletedTask; }
    }
}
