using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ProjectHub;
using AIPMS.Application.Features.ProjectLabels;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.IntegrationTests.Evaluations;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Security;

public sealed class ProjectHubAndLabelsSecurityTests(EvaluationDraftDatabaseFixture database)
    : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static DateTime Now => EvaluationDraftDatabaseFixture.Now;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, JsonOpts)!;
    }

    private static async Task<string?> ReadProblemCode(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) return c.GetString();
        if (doc.RootElement.TryGetProperty("errorCode", out var ec) && ec.ValueKind == JsonValueKind.String) return ec.GetString();
        return null;
    }

    [Fact]
    public async Task Hub_And_Labels_Enforce_ReadOnlyGets_D18_AuthZ_SystemLabelProtection_And_Locks()
    {
        var s = await database.Seed(false);
        long unrelatedStudentId, otherDeptStaffId, lockedMeetingId;

        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "DRAFT"; // Start in DRAFT to verify D18 blocks writes!
            project.TrackCode = "IT_ONLY";

            var leaderMember = await db.TeamMembers.SingleAsync(m => m.TeamId == project.TeamId && m.UserId == s.Scope.Users.Student);
            leaderMember.IsLeader = true;

            var stuRoleId = await db.Roles.Where(r => r.Code == AppRoles.Student).Select(r => r.Id).SingleAsync();
            var staffRoleId = await db.Roles.Where(r => r.Code == AppRoles.DepartmentStaff).Select(r => r.Id).SingleAsync();
            var otherDeptId = (await db.Users.SingleAsync(u => u.Id == s.Scope.Users.OtherLecturer)).DepartmentId!.Value;

            var unrelatedStu = new M.User
            {
                Email = $"hubsec_stu_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "Unrelated Student",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = s.Scope.Users.DepartmentId,
                UserRoleUsers = [new() { RoleId = stuRoleId }]
            };
            var otherStaff = new M.User
            {
                Email = $"hubsec_ostaff_{Guid.NewGuid():N}@e2e.invalid",
                FullName = "Other Dept Staff",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = otherDeptId,
                UserRoleUsers = [new() { RoleId = staffRoleId }]
            };
            db.Users.AddRange(unrelatedStu, otherStaff);

            var lockedMeeting = new M.Meeting
            {
                ProjectId = s.ProjectId,
                Title = "Locked Minutes Sync",
                StartAt = Now.AddDays(-2),
                EndAt = Now.AddDays(-2).AddHours(1),
                Status = "COMPLETED",
                NotesVersion = 1,
                NotesLockedAt = Now.AddDays(-1),
                NotesLockedBy = s.Scope.Users.Staff,
                CreatedBy = s.Scope.Users.Student,
                CreatedAt = Now.AddDays(-3),
                UpdatedAt = Now.AddDays(-1),
                ConcurrencyToken = Guid.NewGuid()
            };
            db.Meetings.Add(lockedMeeting);
            await db.SaveChangesAsync();

            unrelatedStudentId = unrelatedStu.Id;
            otherDeptStaffId = otherStaff.Id;
            lockedMeetingId = lockedMeeting.Id;
        }

        using var app = new EvaluationFactory(database);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: [AppRoles.Student]);
        using var unrelatedStudent = app.CreateAuthenticatedClient(unrelatedStudentId, roles: [AppRoles.Student]);
        using var otherStaffClient = app.CreateAuthenticatedClient(otherDeptStaffId, roles: [AppRoles.DepartmentStaff]);

        // 1. Positive (Pure Read-Only GETs): GET /hub/channels/GENERAL and GET /labels return 200 with ZERO DB writes
        var ch = await Body<HubChannelDto>(await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/hub/channels/GENERAL"));
        Assert.Equal(0, ch.ConversationId);
        Assert.Empty(ch.Messages);

        var lbls = await Body<List<ProjectLabelDto>>(await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/labels"));
        Assert.Contains(lbls, l => l.Slug == "bug" && l.Id == 0);

        await using (var db = database.CreateContext())
        {
            Assert.Equal(0, await db.Set<M.ChatConversation>().CountAsync(c => c.ProjectId == s.ProjectId));
            Assert.Equal(0, await db.ProjectLabels.CountAsync(l => l.ProjectId == s.ProjectId));
        }

        // 2. Negative (IDOR on GET/POST /labels): unrelated student & other-dept staff -> 403 SCOPE_ACCESS_DENIED
        foreach (var rogue in new[] { unrelatedStudent, otherStaffClient })
        {
            var rGet = await rogue.GetAsync($"/api/v1/projects/{s.ProjectId}/labels");
            Assert.Equal(HttpStatusCode.Forbidden, rGet.StatusCode);
            Assert.Equal(WorkflowErrorCodes.ScopeAccessDenied, await ReadProblemCode(rGet));

            var rPost = await rogue.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/labels",
                new SaveProjectLabelRequest("rogue-lbl", "Rogue", "#112233", "Blocked"));
            Assert.Equal(HttpStatusCode.Forbidden, rPost.StatusCode);
            Assert.Equal(WorkflowErrorCodes.ScopeAccessDenied, await ReadProblemCode(rPost));
        }

        // 3. Negative (D18 Activation Lock on DRAFT project): Hub & Label writes -> 409 PROJECT_NOT_ACTIVE
        var pinOnDraft = await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/hub/pinned-resources",
            new CreatePinnedResourceRequest("DOC", "Spec", "https://example.invalid/doc"));
        Assert.Equal(HttpStatusCode.Conflict, pinOnDraft.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ProjectNotActive, await ReadProblemCode(pinOnDraft));

        var msgOnDraft = await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/hub/channels/GENERAL/messages",
            new PostChannelMessageRequest("Should fail on DRAFT"));
        Assert.Equal(HttpStatusCode.Conflict, msgOnDraft.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ProjectNotActive, await ReadProblemCode(msgOnDraft));

        var lblOnDraft = await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/labels",
            new SaveProjectLabelRequest("draft-lbl", "Draft", "#1D4ED8", "Blocked"));
        Assert.Equal(HttpStatusCode.Conflict, lblOnDraft.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ProjectNotActive, await ReadProblemCode(lblOnDraft));

        // Activate project -> test system label protection, locked meeting notes, and READ_ONLY chat channel
        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            await db.SaveChangesAsync();
        }

        // Create a custom label (triggers DB seed of system labels)
        var created = await Body<ProjectLabelDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/labels",
            new SaveProjectLabelRequest("api-layer", "API Layer", "#1D4ED8", "Custom")));
        Assert.Equal("api-layer", created.Slug);

        // 4. Negative: Deleting or renaming slug of system label -> 409 RESTRICTED_DELETE
        var seededList = await Body<List<ProjectLabelDto>>(await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/labels"));
        var sysBug = seededList.Single(l => l.Slug == "bug");
        Assert.True(sysBug.Id > 0 && sysBug.IsSystem);

        var delSys = await leader.DeleteAsync($"/api/v1/projects/{s.ProjectId}/labels/{sysBug.Id}");
        Assert.Equal(HttpStatusCode.Conflict, delSys.StatusCode);
        Assert.Equal(WorkflowErrorCodes.RestrictedDelete, await ReadProblemCode(delSys));

        var renameSys = await leader.PutAsJsonAsync($"/api/v1/projects/{s.ProjectId}/labels/{sysBug.Id}",
            new SaveProjectLabelRequest("bug-renamed", "Bug", "#DC2626", "Try rename"));
        Assert.Equal(HttpStatusCode.Conflict, renameSys.StatusCode);
        Assert.Equal(WorkflowErrorCodes.RestrictedDelete, await ReadProblemCode(renameSys));

        // 5. Negative: Convert message to decision on a meeting with NotesLockedAt -> 409 MEETING_NOTES_LOCKED
        var postedMsg = await Body<HubChannelMessageDto>(await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/channels/GENERAL/messages",
            new PostChannelMessageRequest("Decide on Redis cache")));
        var convertLocked = await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/chat/messages/{postedMsg.Id}/to-decision",
            new ConvertMessageToDecisionRequest(lockedMeetingId, "Try append to locked meeting"));
        Assert.Equal(HttpStatusCode.Conflict, convertLocked.StatusCode);
        Assert.Equal("MEETING_NOTES_LOCKED", await ReadProblemCode(convertLocked));

        // 6. Negative: Posting to READ_ONLY chat conversation -> 409 CHAT_CONVERSATION_READ_ONLY
        await using (var db = database.CreateContext())
        {
            var conv = await db.Set<M.ChatConversation>().SingleAsync(c => c.ProjectId == s.ProjectId && c.ChannelScope == "GENERAL");
            conv.Status = "READ_ONLY";
            await db.SaveChangesAsync();
        }
        var postReadOnly = await leader.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/hub/channels/GENERAL/messages",
            new PostChannelMessageRequest("Should fail on READ_ONLY"));
        Assert.Equal(HttpStatusCode.Conflict, postReadOnly.StatusCode);
        Assert.Equal("CHAT_CONVERSATION_READ_ONLY", await ReadProblemCode(postReadOnly));
    }
}
