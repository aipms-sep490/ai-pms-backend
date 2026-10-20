using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Supervisors.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Supervisors;

public sealed partial class SupervisorRequestEndpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preconfirmed_approval_activates_and_audit_failure_rolls_back_every_transition(bool failAudit)
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        await SeedActivationTemplate(s.Admin, p.SemesterId);
        await using (var db = database.CreateContext())
        {
            (await db.Projects.FindAsync(p.Id))!.Status = "DRAFT";
            await db.SaveChangesAsync();
        }
        using (var setup = new SupervisorFactory(database, clock: new Clock()))
        {
            using var leader = setup.CreateAuthenticatedClient(p.LeaderId);
            using var lecturer = setup.CreateAuthenticatedClient(s.Lecturer);
            var request = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
            await Body<SupervisorRequestDto>(await Respond(lecturer, request.Id, "accept"));
        }
        var token = await PrepareApproval(p);
        using var app = new SupervisorFactory(database, clock: new Clock(),
            saveInterceptor: failAudit ? new FailActivationAudit() : null);
        using var staff = app.CreateAuthenticatedClient(s.Staff, roles: AppRoles.DepartmentStaff);
        var response = await staff.PostAsJsonAsync($"/api/v1/projects/{p.Id}/approve", new { concurrencyToken = token });
        Assert.Equal(failAudit ? HttpStatusCode.InternalServerError : HttpStatusCode.OK, response.StatusCode);
        if (!failAudit) Assert.Equal("ACTIVE", (await Body<ProjectDto>(response)).Status);
        await using var verify = database.CreateContext();
        Assert.Equal(failAudit ? "UNDER_REVIEW" : "ACTIVE", (await verify.Projects.FindAsync(p.Id))!.Status);
        Assert.Equal(failAudit ? 0 : 3, await verify.ProjectStatusHistories.CountAsync(h => h.ProjectId == p.Id));
        Assert.Equal(failAudit ? 0 : 1, await verify.AuditLogs.CountAsync(a => a.EntityId == p.Id.ToString() && a.Action == "PROJECT_ACTIVATED"));
        Assert.Equal(failAudit ? 0 : 1, await verify.AuditLogs.CountAsync(a => a.EntityId == p.Id.ToString() && a.Action == "PROJECT_APPROVED"));
        Assert.Equal(failAudit ? 0 : 1, await verify.Notifications.CountAsync(n => n.RelatedEntityId == p.Id && n.NotificationType == "PROJECT_APPROVED"));
        Assert.Equal(!failAudit, await verify.Milestones.AnyAsync(m => m.ProjectId == p.Id));
    }
}
