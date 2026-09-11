using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Deliverables;

public sealed class DeliverableEndpointTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static SaveDeliverableRequest Data(string title = "Report") => new(null, title, " Description ", "REPORT", Now.AddHours(2));
    private static Task<HttpResponseMessage> Create(HttpClient client, long projectId) => client.PostAsJsonAsync($"/api/v1/projects/{projectId}/deliverables", Data());
    private static MultipartFormDataContent Form(byte[]? bytes = null, string name = "report.txt", string mime = "text/plain")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes ?? Encoding.UTF8.GetBytes("Project evidence"));
        file.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(file, "file", name);
        return form;
    }
    private static async Task<HttpResponseMessage> Submit(HttpClient client, long id, int expected = 0)
    {
        using var form = Form();
        form.Add(new StringContent(expected.ToString()), "expectedLatestVersion");
        form.Add(new StringContent(" Submitted note "), "note");
        return await client.PostAsync($"/api/v1/deliverables/{id}/versions", form);
    }
    private static async Task<HttpResponseMessage> Attach(HttpClient client, long id, string type = "REPORT")
    {
        using var form = Form();
        form.Add(new StringContent(type), "parentType");
        form.Add(new StringContent(id.ToString()), "parentId");
        return await client.PostAsync("/api/v1/files", form);
    }

    [Fact]
    public async Task Lifecycle_preserves_versions_checks_review_assignment_and_private_download()
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Accounts.Student);
        using var lecturer = app.CreateAuthenticatedClient(s.Accounts.Lecturer);
        var d = await Body<DeliverableDto>(await Create(leader, s.ProjectId));
        var updated = await Body<DeliverableDto>(await leader.PutAsJsonAsync($"/api/v1/deliverables/{d.Id}", Data("Revised title")));
        Assert.Equal("Revised title", updated.Title);
        var v1 = await Body<DeliverableVersionDto>(await Submit(leader, d.Id));
        var file = Assert.Single(v1.Files);
        Assert.Equal(1, v1.VersionNumber);
        Assert.Equal("Submitted note", v1.Note);
        Assert.Equal(s.Accounts.Student, file.UploadedBy);
        Assert.Equal(64, file.Sha256.Length);
        var downloaded = await lecturer.GetAsync($"/api/v1/files/{file.Id}/download");
        Assert.Equal("Project evidence", await downloaded.Content.ReadAsStringAsync());
        Assert.Equal("attachment", downloaded.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("no-store", downloaded.Headers.CacheControl!.ToString());
        var metadataText = await (await leader.GetAsync($"/api/v1/files/{file.Id}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("storagePath", metadataText);
        Assert.DoesNotContain("storageKey", metadataText);
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(leader, d.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.DeleteAsync($"/api/v1/files/{file.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.DeleteAsync($"/api/v1/deliverables/{d.Id}")).StatusCode);
        var rejected = await Body<DeliverableFeedbackDto>(await lecturer.PostAsJsonAsync($"/api/v1/deliverable-versions/{v1.Id}/review", new ReviewDeliverableRequest("REJECTED", " Please revise ")));
        Assert.Equal("Please revise", rejected.Feedback);
        var v2 = await Body<DeliverableVersionDto>(await Submit(leader, d.Id, 1));
        Assert.Equal(2, v2.VersionNumber);
        await Body<DeliverableFeedbackDto>(await lecturer.PostAsJsonAsync($"/api/v1/deliverable-versions/{v2.Id}/review", new ReviewDeliverableRequest("ACCEPTED", "Good")));
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(leader, d.Id, 2)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync($"/api/v1/deliverables/{d.Id}", Data())).StatusCode);
        var versions = await Body<PagedResult<DeliverableVersionDto>>(await leader.GetAsync($"/api/v1/deliverables/{d.Id}/versions?pageSize=1&page=2"));
        Assert.Equal(2, versions.TotalCount);
        Assert.Equal(v1.Id, Assert.Single(versions.Items).Id);
        Assert.Single((await Body<PagedResult<DeliverableFeedbackDto>>(await leader.GetAsync($"/api/v1/deliverable-versions/{v1.Id}/feedback"))).Items);
        Assert.Equal(2, (await Body<PagedResult<ProjectFileDto>>(await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/files?search=report"))).TotalCount);
        await using var db = database.CreateContext();
        Assert.Equal(2, await db.Notifications.CountAsync(n => n.NotificationType == "DELIVERABLE_SUBMITTED" && (n.RelatedEntityId == v1.Id || n.RelatedEntityId == v2.Id)));
        Assert.Equal(2, app.Storage.Objects.Count);
        Assert.Equal("ACCEPTED", (await db.Deliverables.FindAsync(d.Id))!.Status);
        Assert.Equal(s.AssignmentId, rejected.AssignmentId);
    }

    [Fact]
    public async Task Role_claims_project_read_and_other_supervisors_do_not_grant_write_permissions()
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Accounts.Student);
        using var member = app.CreateAuthenticatedClient(s.MemberId, roles: AppRoles.Admin);
        using var staff = app.CreateAuthenticatedClient(s.Accounts.Staff);
        using var outsider = app.CreateAuthenticatedClient(s.Accounts.OtherLecturer, roles: AppRoles.Admin);
        var d = await Body<DeliverableDto>(await Create(leader, s.ProjectId));
        Assert.Equal(HttpStatusCode.Forbidden, (await Create(member, s.ProjectId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Create(staff, s.ProjectId)).StatusCode);
        var v = await Body<DeliverableVersionDto>(await Submit(member, d.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await Submit(staff, d.Id, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/v1/files/{v.Files[0].Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync($"/api/v1/deliverable-versions/{v.Id}/review", new ReviewDeliverableRequest("ACCEPTED", "Invalid"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/v1/deliverable-versions/{v.Id}/review", new ReviewDeliverableRequest("ACCEPTED", "Invalid"))).StatusCode);
        await using (var db = database.CreateContext())
        {
            (await db.SupervisorAssignments.FindAsync(s.AssignmentId))!.EndedAt = Now;
            await db.SaveChangesAsync();
        }
        using var oldLecturer = app.CreateAuthenticatedClient(s.Accounts.Lecturer);
        Assert.Equal(HttpStatusCode.Forbidden, (await oldLecturer.GetAsync($"/api/v1/files/{v.Files[0].Id}/download")).StatusCode);
    }

    [Theory]
    [InlineData("deadline")]
    [InlineData("period")]
    [InlineData("archived")]
    [InlineData("completed")]
    [InlineData("membership")]
    [InlineData("role")]
    public async Task Submission_rechecks_persisted_prerequisites(string change)
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Accounts.Student);
        var d = await Body<DeliverableDto>(await Create(leader, s.ProjectId));
        await using (var db = database.CreateContext())
        {
            if (change == "deadline") (await db.Deliverables.FindAsync(d.Id))!.DueAt = Now;
            if (change == "period") (await db.ProjectPeriods.FindAsync(s.PeriodId))!.Status = "CLOSED";
            if (change is "archived" or "completed") (await db.Projects.FindAsync(s.ProjectId))!.Status = change.ToUpperInvariant();
            if (change == "membership")
            {
                var memberEntry = await db.TeamMembers.SingleAsync(m => m.TeamId == s.TeamId && m.UserId == s.Accounts.Student);
                memberEntry.JoinedAt = Now.AddDays(-1);
                memberEntry.LeftAt = Now;
            }
            if (change == "role") db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == s.Accounts.Student).ToListAsync());
            await db.SaveChangesAsync();
        }
        var response = await Submit(leader, d.Id);
        Assert.Equal(change is "membership" or "role" ? HttpStatusCode.Forbidden : HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(app.Storage.Objects);
    }

    [Fact]
    public async Task Parent_relations_validation_and_pagination_are_enforced()
    {
        var s = await Seed();
        var other = await Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Accounts.Student);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Create(anonymous, s.ProjectId)).StatusCode);
        var bad = Data() with { MilestoneId = other.MilestoneId };
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/deliverables", bad)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/deliverables", Data(" "))).StatusCode);
        var d = await Body<DeliverableDto>(await Create(leader, s.ProjectId));
        foreach (var query in new[] { "page=0", "pageSize=101", "status=INVALID" })
            Assert.Equal(HttpStatusCode.BadRequest, (await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/deliverables?{query}")).StatusCode);
        using (var form = Form()) Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostAsync($"/api/v1/deliverables/{d.Id}/versions", form)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync("/api/v1/files/999999999/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await leader.DeleteAsync($"/api/v1/deliverables/{d.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("report.exe", "application/octet-stream", "fake")]
    [InlineData("report.pdf", "application/pdf", "not a pdf")]
    [InlineData("report.txt", "application/pdf", "text")]
    [InlineData("../report.txt", "text/plain", "text")]
    public async Task Invalid_upload_never_creates_storage_or_metadata(string name, string mime, string content)
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Accounts.Student);
        var d = await Body<DeliverableDto>(await Create(leader, s.ProjectId));
        using var form = Form(Encoding.UTF8.GetBytes(content), name, mime);
        form.Add(new StringContent("0"), "expectedLatestVersion");
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostAsync($"/api/v1/deliverables/{d.Id}/versions", form)).StatusCode);
        Assert.Empty(app.Storage.Objects);
    }

    [Theory]
    [InlineData("storage")]
    [InlineData("audit")]
    [InlineData("deadlineDuringWrite")]
    public async Task Failed_submit_rolls_back_metadata_notification_and_storage(string failure)
    {
        var s = await Seed();
        using var setup = new Factory(database);
        using var creator = setup.CreateAuthenticatedClient(s.Accounts.Student);
        var d = await Body<DeliverableDto>(await Create(creator, s.ProjectId));
        var clock = new Clock();
        var storage = new MemoryStorage { FailWrite = failure == "storage" };
        if (failure == "deadlineDuringWrite") storage.AfterWrite = () => clock.Current = Now.AddDays(2);
        using var app = new Factory(database, storage, failAudit: failure == "audit", clock: clock);
        using var leader = app.CreateAuthenticatedClient(s.Accounts.Student);
        var response = await Submit(leader, d.Id);
        Assert.Equal(failure == "deadlineDuringWrite" ? HttpStatusCode.Conflict : HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(storage.Objects);
        await using var db = database.CreateContext();
        Assert.False(await db.DeliverableVersions.AnyAsync(v => v.DeliverableId == d.Id));
        Assert.Equal("OPEN", (await db.Deliverables.FindAsync(d.Id))!.Status);
        Assert.False(await db.Notifications.AnyAsync(n => n.CreatedBy == s.Accounts.Student));
    }

    [Fact]
    public async Task Concurrent_submit_has_one_winner_and_no_duplicate_version_or_notification()
    {
        var s = await Seed();
        using var setup = new Factory(database);
        using var creator = setup.CreateAuthenticatedClient(s.Accounts.Student);
        var d = await Body<DeliverableDto>(await Create(creator, s.ProjectId));
        using var app = new Factory(database, interceptor: new ProjectLockBarrier());
        using var leader = app.CreateAuthenticatedClient(s.Accounts.Student);
        var responses = await Task.WhenAll(Submit(leader, d.Id), Submit(leader, d.Id));
        Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Single(app.Storage.Objects);
        await using var db = database.CreateContext();
        var v = await db.DeliverableVersions.SingleAsync(v => v.DeliverableId == d.Id);
        Assert.Equal(1, v.VersionNumber);
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.RelatedEntityId == v.Id && n.NotificationType == "DELIVERABLE_SUBMITTED"));
    }

    [Fact]
    public async Task Lost_commit_acknowledgement_does_not_delete_committed_file_and_retry_is_conflict()
    {
        var s = await Seed();
        using var setup = new Factory(database);
        using var creator = setup.CreateAuthenticatedClient(s.Accounts.Student);
        var d = await Body<DeliverableDto>(await Create(creator, s.ProjectId));
        var storage = new MemoryStorage();
        using var app = new Factory(database, storage, interceptor: new FailAfterCommit());
        using var leader = app.CreateAuthenticatedClient(s.Accounts.Student);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Submit(leader, d.Id)).StatusCode);
        Assert.Single(storage.Objects);
        Assert.Equal(0, storage.Deletes);
        using var recovery = new Factory(database, storage);
        using var retry = recovery.CreateAuthenticatedClient(s.Accounts.Student);
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(retry, d.Id)).StatusCode);
        await using var db = database.CreateContext();
        var v = await db.DeliverableVersions.Include(v => v.Files).SingleAsync(v => v.DeliverableId == d.Id);
        Assert.Equal(HttpStatusCode.OK, (await retry.GetAsync($"/api/v1/files/{v.Files.Single().Id}/download")).StatusCode);
    }

    [Fact]
    public async Task Draft_attachments_delete_after_commit_and_submitted_attachments_are_immutable()
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Accounts.Student);
        var file = await Body<ProjectFileDto>(await Attach(leader, s.ReportId));
        using var failing = new Factory(database, app.Storage, failAudit: true);
        using var failClient = failing.CreateAuthenticatedClient(s.Accounts.Student);
        Assert.Equal(HttpStatusCode.InternalServerError, (await failClient.DeleteAsync($"/api/v1/files/{file.Id}")).StatusCode);
        Assert.Single(app.Storage.Objects);
        Assert.Equal(HttpStatusCode.OK, (await leader.GetAsync($"/api/v1/files/{file.Id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await leader.DeleteAsync($"/api/v1/files/{file.Id}")).StatusCode);
        Assert.Empty(app.Storage.Objects);
        var retained = await Body<ProjectFileDto>(await Attach(leader, s.ReportId));
        await using (var db = database.CreateContext())
        {
            (await db.ProgressReports.FindAsync(s.ReportId))!.Status = "SUBMITTED";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await leader.DeleteAsync($"/api/v1/files/{retained.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Attach(leader, s.ReportId)).StatusCode);
    }

    [Fact]
    public async Task Response_materialization_failure_before_commit_cleans_storage_and_rolls_back()
    {
        var s = await Seed();
        using var setup = new Factory(database);
        using var creator = setup.CreateAuthenticatedClient(s.Accounts.Student);
        var d = await Body<DeliverableDto>(await Create(creator, s.ProjectId));
        using var app = new Factory(database, interceptor: new FailVersionRead());
        using var leader = app.CreateAuthenticatedClient(s.Accounts.Student);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Submit(leader, d.Id)).StatusCode);
        Assert.Empty(app.Storage.Objects);
        Assert.Equal(1, app.Storage.Deletes);
        await using var db = database.CreateContext();
        Assert.False(await db.DeliverableVersions.AnyAsync(v => v.DeliverableId == d.Id));
        Assert.False(await db.Notifications.AnyAsync(n => n.CreatedBy == s.Accounts.Student));
        Assert.Equal("OPEN", (await db.Deliverables.FindAsync(d.Id))!.Status);
    }

    [Fact]
    public async Task Failed_physical_delete_does_not_undo_metadata_commit_or_expose_orphan()
    {
        var s = await Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Accounts.Student);
        var file = await Body<ProjectFileDto>(await Attach(leader, s.ReportId));
        app.Storage.FailDelete = true;
        Assert.Equal(HttpStatusCode.NoContent, (await leader.DeleteAsync($"/api/v1/files/{file.Id}")).StatusCode);
        Assert.Single(app.Storage.Objects);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync($"/api/v1/files/{file.Id}/download")).StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.Files.AnyAsync(f => f.Id == file.Id));
    }

    [Fact]
    public async Task Notifications_can_be_disabled_and_missing_content_is_not_found()
    {
        var s = await Seed();
        using var app = new Factory(database, notifyReviewer: false);
        using var leader = app.CreateAuthenticatedClient(s.Accounts.Student);
        var d = await Body<DeliverableDto>(await Create(leader, s.ProjectId));
        var version = await Body<DeliverableVersionDto>(await Submit(leader, d.Id));
        await using var db = database.CreateContext();
        Assert.False(await db.Notifications.AnyAsync(n => n.CreatedBy == s.Accounts.Student));
        app.Storage.Objects.Clear();
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync($"/api/v1/files/{version.Files[0].Id}/download")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_accept_and_submit_cannot_accept_stale_version_or_modify_locked_deliverable()
    {
        var s = await Seed();
        using var setup = new Factory(database);
        using var creator = setup.CreateAuthenticatedClient(s.Accounts.Student);
        var d = await Body<DeliverableDto>(await Create(creator, s.ProjectId));
        var v1 = await Body<DeliverableVersionDto>(await Submit(creator, d.Id));
        using var app = new Factory(database, setup.Storage, interceptor: new ProjectLockBarrier());
        using var leader = app.CreateAuthenticatedClient(s.Accounts.Student);
        using var lecturer = app.CreateAuthenticatedClient(s.Accounts.Lecturer);
        var responses = await Task.WhenAll(Submit(leader, d.Id, 1), lecturer.PostAsJsonAsync(
            $"/api/v1/deliverable-versions/{v1.Id}/review", new ReviewDeliverableRequest("ACCEPTED", "Good")));
        Assert.Single(responses.Where(r => r.IsSuccessStatusCode));
        Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var db = database.CreateContext();
        var stored = (await db.Deliverables.FindAsync(d.Id))!;
        var count = await db.DeliverableVersions.CountAsync(v => v.DeliverableId == d.Id);
        Assert.Equal(stored.Status == "ACCEPTED" ? 1 : 2, count);
        Assert.Equal(count, app.Storage.Objects.Count);
    }

    [Fact]
    public async Task Filters_are_project_scoped_and_validate_parent_and_utc_date_ranges()
    {
        var s = await Seed();
        var other = await Seed();
        using var app = new Factory(database);
        using var leader = app.CreateAuthenticatedClient(s.Accounts.Student);
        var d = await Body<DeliverableDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/deliverables",
            Data() with { MilestoneId = s.MilestoneId }));
        await Body<DeliverableVersionDto>(await Submit(leader, d.Id));
        var attached = await Body<ProjectFileDto>(await Attach(leader, s.ReportId));
        var filesUrl = $"/api/v1/projects/{s.ProjectId}/files";
        var query = $"?parentType=REPORT&parentId={s.ReportId}&uploadedBy={s.Accounts.Student}&contentType=text%2Fplain&from=2026-09-11T00:00:00Z&to=2026-09-12T00:00:00Z";
        Assert.Equal(attached.Id, Assert.Single((await Body<PagedResult<ProjectFileDto>>(await leader.GetAsync(filesUrl + query))).Items).Id);
        foreach (var filter in new[] { $"parentType=REPORT&parentId={other.ReportId}", $"uploadedBy={s.MemberId}", "contentType=application%2Fpdf", "to=2026-09-11T10:00:00Z" })
            Assert.Empty((await Body<PagedResult<ProjectFileDto>>(await leader.GetAsync(filesUrl + "?" + filter))).Items);
        foreach (var filter in new[] { "parentId=1", "parentType=PROJECT", "uploadedBy=0", "from=2026-09-12T00:00:00Z&to=2026-09-11T00:00:00Z" })
            Assert.Equal(HttpStatusCode.BadRequest, (await leader.GetAsync(filesUrl + "?" + filter)).StatusCode);
        Assert.Single((await Body<PagedResult<DeliverableDto>>(await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/deliverables?milestoneId={s.MilestoneId}&deliverableType=REPORT"))).Items);
        Assert.Empty((await Body<PagedResult<DeliverableDto>>(await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/deliverables?milestoneId={other.MilestoneId}"))).Items);
    }

    [Fact]
    public async Task Meeting_attachments_require_project_participation_and_freeze_when_completed()
    {
        var s = await Seed();
        long meetingId;
        await using (var db = database.CreateContext())
        {
            var meeting = new M.Meeting { ProjectId = s.ProjectId, Title = "Review", StartAt = Now.AddHours(1),
                Status = "SCHEDULED", CreatedBy = s.Accounts.Lecturer };
            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();
            meetingId = meeting.Id;
        }
        using var app = new Factory(database);
        using var lecturer = app.CreateAuthenticatedClient(s.Accounts.Lecturer);
        using var member = app.CreateAuthenticatedClient(s.MemberId);
        using var outsider = app.CreateAuthenticatedClient(s.Accounts.OtherLecturer);
        var file = await Body<ProjectFileDto>(await Attach(lecturer, meetingId, "MEETING"));
        Assert.Equal("MEETING", file.ParentType);
        Assert.Equal(meetingId, file.ParentId);
        Assert.Equal(HttpStatusCode.Forbidden, (await Attach(outsider, meetingId, "MEETING")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.DeleteAsync($"/api/v1/files/{file.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await lecturer.DeleteAsync($"/api/v1/files/{file.Id}")).StatusCode);
        var retained = await Body<ProjectFileDto>(await Attach(member, meetingId, "MEETING"));
        await using (var db = database.CreateContext())
        {
            (await db.Meetings.FindAsync(meetingId))!.Status = "COMPLETED";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Attach(member, meetingId, "MEETING")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await lecturer.DeleteAsync($"/api/v1/files/{retained.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lecturer.GetAsync($"/api/v1/files/{retained.Id}/download")).StatusCode);
    }

    private sealed class FailVersionRead : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.Transaction is not null && command.CommandText.Contains("FROM [deliverable_versions]", StringComparison.Ordinal)
                && command.CommandText.Contains("LEFT JOIN [files]", StringComparison.Ordinal))
                throw new IOException("Simulated response materialization failure");
            return ValueTask.FromResult(result);
        }
    }

    private async Task<Scenario> Seed()
    {
        var s = await database.SeedAsync();
        await using var db = database.CreateContext();
        var semester = new M.AcademicSemester { OrganizationId = (await db.Departments.FindAsync(s.DepartmentId))!.OrganizationId,
            Code = Guid.NewGuid().ToString("N"), Name = "Semester", Status = "ACTIVE", StartDate = DateOnly.FromDateTime(Now.AddDays(-30)), EndDate = DateOnly.FromDateTime(Now.AddDays(30)) };
        var role = await db.Roles.SingleAsync(r => r.Code == AppRoles.Student);
        var member = new M.User { DepartmentId = s.DepartmentId, Email = $"{Guid.NewGuid():N}@test.local", FullName = "Member",
            PasswordHash = "unused", Status = "ACTIVE", UserRoleUsers = [new() { RoleId = role.Id }] };
        db.Users.Add(member);
        db.AcademicSemesters.Add(semester);
        await db.SaveChangesAsync();
        var p = new M.Project { Code = Guid.NewGuid().ToString("N"), Title = "Project", Status = "ACTIVE", CreatedBy = s.Student,
            Team = new() { Code = Guid.NewGuid().ToString("N"), Name = "Team", AcademicSemesterId = semester.Id, CreatedBy = s.Student,
                Status = "ELIGIBLE", TeamMembers = [new() { AcademicSemesterId = semester.Id, UserId = s.Student, IsLeader = true, JoinedAt = Now.AddDays(-1) },
                    new() { AcademicSemesterId = semester.Id, UserId = member.Id, JoinedAt = Now.AddDays(-1) }] },
            ProjectMajors = [new() { Major = new() { DepartmentId = s.DepartmentId, Code = Guid.NewGuid().ToString("N"), Name = "SE", IsActive = true } }] };
        db.Projects.Add(p);
        var period = new M.ProjectPeriod { AcademicSemesterId = semester.Id, Code = "EXEC", Name = "Execution", PeriodType = "EXECUTION", Status = "ACTIVE", StartAt = Now.AddDays(-1), EndAt = Now.AddDays(1) };
        db.ProjectPeriods.Add(period);
        await db.SaveChangesAsync();
        var request = new M.SupervisorRequest { ProjectId = p.Id, SupervisorProfileId = s.ProfileId, RequestedBy = s.Student, Status = "ACCEPTED", RequestedAt = Now.AddDays(-1) };
        db.SupervisorRequests.Add(request);
        await db.SaveChangesAsync();
        var assignment = new M.SupervisorAssignment { ProjectId = p.Id, SupervisorProfileId = s.ProfileId, SupervisorRequestId = request.Id, IsPrimary = true, AssignedAt = Now.AddDays(-1) };
        var milestone = new M.Milestone { ProjectId = p.Id, Title = "Milestone", Status = "PLANNED", CreatedBy = s.Student };
        var report = new M.ProgressReport { ProjectId = p.Id, SubmittedBy = s.Student, ReportType = "WEEKLY", PeriodStart = DateOnly.FromDateTime(Now), PeriodEnd = DateOnly.FromDateTime(Now.AddDays(6)), Summary = "Draft", Status = "DRAFT" };
        db.SupervisorAssignments.Add(assignment);
        db.Milestones.Add(milestone);
        db.ProgressReports.Add(report);
        await db.SaveChangesAsync();
        return new(s, p.Id, p.TeamId, period.Id, member.Id, assignment.Id, milestone.Id, report.Id);
    }

    private sealed record Scenario(SupervisorScenario Accounts, long ProjectId, long TeamId, long PeriodId,
        long MemberId, long AssignmentId, long MilestoneId, long ReportId);
    private sealed class Clock : TimeProvider
    {
        public DateTime Current { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => new(Current);
    }
    private sealed class MemoryStorage : IFileStorage
    {
        public ConcurrentDictionary<string, byte[]> Objects { get; } = new();
        public bool FailWrite { get; init; }
        public bool FailDelete { get; set; }
        public int Deletes { get; private set; }
        public Action? AfterWrite { get; set; }
        public async Task WriteAsync(string key, Stream content, CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            if (!Objects.TryAdd(key, buffer.ToArray())) throw new IOException("Object exists");
            try
            {
                AfterWrite?.Invoke();
                if (FailWrite) throw new IOException("Simulated interrupted write");
            }
            catch { Objects.TryRemove(key, out _); throw; }
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Objects.TryGetValue(key, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes)) : throw new FileNotFoundException();
        public Task DeleteAsync(string key, CancellationToken ct)
        {
            Deletes++;
            if (FailDelete) throw new IOException("Simulated delete failure");
            Objects.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
    private sealed class Factory(SupervisorDatabaseFixture database, MemoryStorage? storage = null,
        bool failAudit = false, IInterceptor? interceptor = null, Clock? clock = null, bool notifyReviewer = true) : AipmsWebApplicationFactory
    {
        public MemoryStorage Storage { get; } = storage ?? new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                ["Deliverables:NotifyReviewer"] = notifyReviewer.ToString()
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileStorage>(); services.AddSingleton<IFileStorage>(Storage);
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock ?? new Clock());
                if (failAudit) { services.RemoveAll<IAuditTrail>(); services.AddSingleton<IAuditTrail, FailAudit>(); }
                if (interceptor is not null)
                {
                    services.RemoveAll<DbContextOptions<AipmsDbContext>>();
                    services.AddDbContext<AipmsDbContext>(o => o.UseSqlServer(database.ConnectionString).AddInterceptors(interceptor));
                }
            });
        }
    }
    private sealed class FailAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Simulated audit failure");
    }
    private sealed class ProjectLockBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("dbo.projects WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal) && Volatile.Read(ref arrivals) < 2)
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
    private sealed class FailAfterCommit : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
            throw new IOException("Simulated loss of commit acknowledgement");
    }
}
