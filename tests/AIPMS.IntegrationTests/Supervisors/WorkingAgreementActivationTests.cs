using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Features.ExecutionCapabilities.DTOs;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.WorkingAgreements;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AIPMS.IntegrationTests.Supervisors;

public sealed partial class SupervisorRequestEndpointTests
{
    [Fact]
    public async Task Required_agreement_blocks_acceptance_then_activates_only_after_current_roster_consents()
    {
        var s = await database.SeedAsync(); var p = await SeedProject(s);
        await using (var db = database.CreateContext())
        {
            var major = await db.ProjectMajors.Where(m => m.ProjectId == p.Id).Select(m => m.MajorId).SingleAsync();
            (await db.Users.FindAsync(p.LeaderId))!.MajorId = major;
            (await db.Users.FindAsync(p.MemberId))!.MajorId = major;
            await db.SaveChangesAsync();
            await AcademicSnapshotFixture.AddAsync(db, p.Id, p.PeriodId, s.DepartmentId, p.LeaderId, Now.AddHours(-1));
            var snapshot = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(r => r.ProjectId == p.Id);
            var policy = new PeriodPolicyVersion { ProjectPeriodId = p.PeriodId, Version = 1, Status = "LOCKED",
                EffectiveFrom = Now.AddDays(-1), EffectiveTo = Now.AddDays(1),
                SnapshotJson = JsonSerializer.Serialize(new PeriodPolicyFields("SINGLE_MAJOR", "STUDENT_PROPOSAL", 1, 5, 1, 5, true)) };
            db.Add(policy); await db.SaveChangesAsync();
            db.Add(new PeriodPolicyUsage { PolicyVersionId = policy.Id, EntityType = "PROJECT_SUBMISSION", EntityId = snapshot.Id, CreatedAt = Now });
            await db.SaveChangesAsync();
        }
        using var app = new SupervisorFactory(database, clock: new Clock());
        app.Services.GetRequiredService<IOptions<WorkingAgreementSettings>>().Value.Enabled = true;
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        using var member = app.CreateAuthenticatedClient(p.MemberId);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        var request = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        var denial = await Respond(lecturer, request.Id, "accept");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, denial.StatusCode);
        Assert.Contains("WORKING_AGREEMENT_INCOMPLETE", await denial.Content.ReadAsStringAsync());
        var capabilities = await Body<ProjectCapabilitiesDto>(await leader.GetAsync($"/api/v1/projects/{p.Id}/my-capabilities"));
        Assert.True(capabilities.WorkingAgreement!.Required); Assert.Equal("AWAITING_ACCEPTANCE", capabilities.WorkingAgreement.Status);
        var url = $"/api/v1/projects/{p.Id}/working-agreement";
        var draft = await Body<WorkingAgreementDto>(await leader.PutAsJsonAsync(url + "/draft", new SaveWorkingAgreementRequest(0, null, [new("Team", "Agree to work together")])));
        var active = await Body<WorkingAgreementDto>(await leader.PostAsJsonAsync(url + "/publish", new AgreementTokenRequest(draft.Id, draft.ConcurrencyToken)));
        var token = new AgreementTokenRequest(active.Id, active.ConcurrencyToken);
        await Body<WorkingAgreementDto>(await leader.PostAsJsonAsync(url + "/acceptances", token));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Respond(lecturer, request.Id, "accept")).StatusCode);
        await using (var db = database.CreateContext())
        {
            Assert.Equal("PENDING", (await db.SupervisorRequests.FindAsync(request.Id))!.Status);
            Assert.Equal("APPROVED", (await db.Projects.FindAsync(p.Id))!.Status);
            Assert.False(await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == p.Id));
        }
        var ready = await Body<WorkingAgreementDto>(await member.PostAsJsonAsync(url + "/acceptances", token));
        Assert.Equal("READY", ready.Readiness.Status);
        await Body<SupervisorRequestDto>(await Respond(lecturer, request.Id, "accept"));
        await using (var db = database.CreateContext())
        {
            Assert.Equal("ACTIVE", (await db.Projects.FindAsync(p.Id))!.Status);
            Assert.Single(await db.SupervisorAssignments.Where(a => a.ProjectId == p.Id).ToArrayAsync());
        }
    }
}
