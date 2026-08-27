using System.Net;
using System.Net.Http.Json;
using AIPMS.Api.Controllers;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests;

public sealed class DeliverableEndpointsIntegrationTests : IClassFixture<AipmsWebApplicationFactory>, IAsyncLifetime
{
    private readonly AipmsWebApplicationFactory factory;
    private readonly List<Seeded> seededRows=[];
    public DeliverableEndpointsIntegrationTests(AipmsWebApplicationFactory factory)=>this.factory=factory;
    public Task InitializeAsync()=>Task.CompletedTask;
    public async Task DisposeAsync()
    {
        foreach(var seeded in seededRows)
        {
            var organizationId=seeded.OrganizationId;
            using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AipmsDbContext>();var storage=scope.ServiceProvider.GetRequiredService<IFileStorage>();
            var keys=await db.Files.Where(x=>x.DeliverableVersion!=null&&x.DeliverableVersion.Deliverable.Project.Team.AcademicSemester.OrganizationId==organizationId).Select(x=>x.StoragePath).ToListAsync();
            foreach(var key in keys)await storage.DeleteAsync(key,default);
            await db.Database.ExecuteSqlInterpolatedAsync($@"
DELETE nr FROM notification_recipients nr JOIN notifications n ON n.id=nr.notification_id JOIN deliverable_versions v ON n.related_entity_type='DeliverableVersion' AND n.related_entity_id=v.id JOIN deliverables d ON d.id=v.deliverable_id JOIN projects p ON p.id=d.project_id JOIN teams t ON t.id=p.team_id JOIN academic_semesters s ON s.id=t.academic_semester_id WHERE s.organization_id={organizationId};
DELETE n FROM notifications n JOIN deliverable_versions v ON n.related_entity_type='DeliverableVersion' AND n.related_entity_id=v.id JOIN deliverables d ON d.id=v.deliverable_id JOIN projects p ON p.id=d.project_id JOIN teams t ON t.id=p.team_id JOIN academic_semesters s ON s.id=t.academic_semester_id WHERE s.organization_id={organizationId};
DELETE sf FROM supervisor_feedback sf JOIN projects p ON p.id=sf.project_id JOIN teams t ON t.id=p.team_id JOIN academic_semesters s ON s.id=t.academic_semester_id WHERE s.organization_id={organizationId};
DELETE f FROM files f JOIN deliverable_versions v ON v.id=f.deliverable_version_id JOIN deliverables d ON d.id=v.deliverable_id JOIN projects p ON p.id=d.project_id JOIN teams t ON t.id=p.team_id JOIN academic_semesters s ON s.id=t.academic_semester_id WHERE s.organization_id={organizationId};
DELETE v FROM deliverable_versions v JOIN deliverables d ON d.id=v.deliverable_id JOIN projects p ON p.id=d.project_id JOIN teams t ON t.id=p.team_id JOIN academic_semesters s ON s.id=t.academic_semester_id WHERE s.organization_id={organizationId};
DELETE d FROM deliverables d JOIN projects p ON p.id=d.project_id JOIN teams t ON t.id=p.team_id JOIN academic_semesters s ON s.id=t.academic_semester_id WHERE s.organization_id={organizationId};
DELETE sa FROM supervisor_assignments sa JOIN projects p ON p.id=sa.project_id JOIN teams t ON t.id=p.team_id JOIN academic_semesters s ON s.id=t.academic_semester_id WHERE s.organization_id={organizationId};
DELETE sr FROM supervisor_requests sr JOIN projects p ON p.id=sr.project_id JOIN teams t ON t.id=p.team_id JOIN academic_semesters s ON s.id=t.academic_semester_id WHERE s.organization_id={organizationId};
DELETE p FROM projects p JOIN teams t ON t.id=p.team_id JOIN academic_semesters s ON s.id=t.academic_semester_id WHERE s.organization_id={organizationId};
DELETE tm FROM team_members tm JOIN teams t ON t.id=tm.team_id JOIN academic_semesters s ON s.id=t.academic_semester_id WHERE s.organization_id={organizationId};
DELETE t FROM teams t JOIN academic_semesters s ON s.id=t.academic_semester_id WHERE s.organization_id={organizationId};
DELETE FROM supervisor_profiles WHERE id={seeded.ProfileId};
DELETE FROM users WHERE id IN ({seeded.StudentId},{seeded.OutsiderId},{seeded.SupervisorId});
DELETE FROM academic_semesters WHERE organization_id={organizationId};DELETE FROM organizations WHERE id={organizationId};");
        }
    }

    [Fact]
    public async Task SubmitHistoryDownloadAndSupervisorFeedback_WorkThroughAuthorizedEndpoints()
    {
        var seeded=await SeedAsync();
        using var student=factory.CreateAuthenticatedClient(seeded.StudentId,"deliverable.student@aipms.test","Student","STUDENT");
        using var form=new MultipartFormDataContent();
        form.Add(new StringContent("integration version"),"Note");
        var bytes=new ByteArrayContent([1,2,3,4]);bytes.Headers.ContentType=new("application/pdf");
        form.Add(bytes,"File","result.pdf");
        var submit=await student.PostAsync($"/api/v1/deliverables/{seeded.DeliverableId}/versions",form);
        Assert.Equal(HttpStatusCode.OK,submit.StatusCode);
        var version=await submit.Content.ReadFromJsonAsync<DeliverableVersionDto>();
        Assert.NotNull(version);Assert.Equal(1,version.VersionNumber);Assert.Equal("SUBMITTED",version.Status);Assert.Equal("integration version",version.SubmissionNote);Assert.NotNull(version.Files.Single().ChecksumSha256);

        using var secondForm=new MultipartFormDataContent();secondForm.Add(new StringContent("second"),"Note");var secondBytes=new ByteArrayContent([5,6]);secondBytes.Headers.ContentType=new("application/pdf");secondForm.Add(secondBytes,"File","second.pdf");
        var second=await student.PostAsync($"/api/v1/deliverables/{seeded.DeliverableId}/versions",secondForm);Assert.Equal(HttpStatusCode.OK,second.StatusCode);Assert.Equal(2,(await second.Content.ReadFromJsonAsync<DeliverableVersionDto>())!.VersionNumber);
        var history=await student.GetFromJsonAsync<List<DeliverableVersionDto>>($"/api/v1/deliverables/{seeded.DeliverableId}/versions");
        Assert.Equal(2,history!.Count);Assert.Equal([2,1],history.Select(x=>x.VersionNumber));
        var download=await student.GetAsync($"/api/v1/files/{version.Files.Single().Id}/download");
        Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Equal([1,2,3,4],await download.Content.ReadAsByteArrayAsync());
        var immutableDelete=await student.DeleteAsync($"/api/v1/files/{version.Files.Single().Id}");
        Assert.Equal(HttpStatusCode.Conflict,immutableDelete.StatusCode);

        using var draftForm=new MultipartFormDataContent();draftForm.Add(new StringContent("draft"),"Note");var draftBytes=new ByteArrayContent([7,8]);draftBytes.Headers.ContentType=new("application/pdf");draftForm.Add(draftBytes,"File","draft.pdf");
        var draftResponse=await student.PostAsync($"/api/v1/deliverables/{seeded.DeliverableId}/files",draftForm);Assert.Equal(HttpStatusCode.OK,draftResponse.StatusCode);var uploaded=await draftResponse.Content.ReadFromJsonAsync<DeliverableVersionDto>();Assert.Equal("SUBMITTED",uploaded!.Status);Assert.Equal(3,uploaded.VersionNumber);Assert.Equal(HttpStatusCode.Conflict,(await student.DeleteAsync($"/api/v1/files/{uploaded.Files.Single().Id}")).StatusCode);

        using var outsider=factory.CreateAuthenticatedClient(seeded.OutsiderId,"deliverable.outsider@aipms.test","Outsider","STUDENT");
        Assert.Equal(HttpStatusCode.Forbidden,(await outsider.GetAsync($"/api/v1/files/{version.Files.Single().Id}/download")).StatusCode);

        using var supervisor=factory.CreateAuthenticatedClient(seeded.SupervisorId,"deliverable.supervisor@aipms.test","Supervisor","LECTURER");
        var feedback=await supervisor.PostAsJsonAsync($"/api/v1/deliverables/versions/{version.Id}/feedback",new FeedbackPayload("Approved metadata"));
        Assert.Equal(HttpStatusCode.OK,feedback.StatusCode);
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
        Assert.True(await db.Notifications.AnyAsync(x=>x.RelatedEntityType=="DeliverableVersion"&&x.RelatedEntityId==version.Id));
        Assert.True(await db.SupervisorFeedbacks.AnyAsync(x=>x.DeliverableVersionId==version.Id&&x.SupervisorAssignmentId==seeded.AssignmentId));
    }

    [Fact]
    public async Task ConcurrentSubmissions_ReceiveDistinctSequentialVersionNumbers()
    {
        var seeded=await SeedAsync();
        using var firstClient=factory.CreateAuthenticatedClient(seeded.StudentId,"deliverable.student@aipms.test","Student","STUDENT");
        using var secondClient=factory.CreateAuthenticatedClient(seeded.StudentId,"deliverable.student@aipms.test","Student","STUDENT");
        using var first=CreateUpload("first.pdf",[1]);using var second=CreateUpload("second.pdf",[2]);
        var responses=await Task.WhenAll(firstClient.PostAsync($"/api/v1/deliverables/{seeded.DeliverableId}/versions",first),secondClient.PostAsync($"/api/v1/deliverables/{seeded.DeliverableId}/versions",second));
        Assert.All(responses,x=>Assert.Equal(HttpStatusCode.OK,x.StatusCode));
        var versions=await Task.WhenAll(responses.Select(x=>x.Content.ReadFromJsonAsync<DeliverableVersionDto>()));
        Assert.Equal([1,2],versions.Select(x=>x!.VersionNumber).OrderBy(x=>x));
    }

    private static MultipartFormDataContent CreateUpload(string name,byte[] content)
    {var form=new MultipartFormDataContent();var bytes=new ByteArrayContent(content);bytes.Headers.ContentType=new("application/pdf");form.Add(bytes,"File",name);return form;}

    private async Task<Seeded> SeedAsync()
    {
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AipmsDbContext>();var suffix=Guid.NewGuid().ToString("N")[..8];var now=DateTime.UtcNow;
        var org=new Organization{Code=$"D{suffix}",Name="Deliverable Test",IsActive=true,CreatedAt=now,UpdatedAt=now};db.Organizations.Add(org);await db.SaveChangesAsync();
        var semester=new AcademicSemester{OrganizationId=org.Id,Code=$"S{suffix}",Name="Semester",StartDate=DateOnly.FromDateTime(now.AddDays(-1)),EndDate=DateOnly.FromDateTime(now.AddDays(30)),Status="ACTIVE",CreatedAt=now,UpdatedAt=now};db.AcademicSemesters.Add(semester);await db.SaveChangesAsync();
        var student=new User{Email=$"student.{suffix}@test.local",PasswordHash="hash",FullName="Student",Status="ACTIVE",CreatedAt=now,UpdatedAt=now};var outsider=new User{Email=$"outside.{suffix}@test.local",PasswordHash="hash",FullName="Outside",Status="ACTIVE",CreatedAt=now,UpdatedAt=now};var lecturer=new User{Email=$"supervisor.{suffix}@test.local",PasswordHash="hash",FullName="Supervisor",Status="ACTIVE",CreatedAt=now,UpdatedAt=now};db.Users.AddRange(student,outsider,lecturer);await db.SaveChangesAsync();
        var team=new Team{AcademicSemesterId=semester.Id,Code=$"T{suffix}",Name="Team",Status="ELIGIBLE",CreatedBy=student.Id,CreatedAt=now,UpdatedAt=now};db.Teams.Add(team);await db.SaveChangesAsync();db.TeamMembers.Add(new TeamMember{TeamId=team.Id,AcademicSemesterId=semester.Id,UserId=student.Id,IsLeader=true,JoinedAt=now,CreatedAt=now,UpdatedAt=now});
        var profile=new SupervisorProfile{UserId=lecturer.Id,MaxActiveProjects=3,IsAvailable=true,CreatedAt=now,UpdatedAt=now};db.SupervisorProfiles.Add(profile);await db.SaveChangesAsync();
        var project=new Project{TeamId=team.Id,Code=$"P{suffix}",Title="Project",Status="ACTIVE",CreatedBy=student.Id,RegisteredAt=now,CreatedAt=now,UpdatedAt=now};db.Projects.Add(project);await db.SaveChangesAsync();
        var request=new SupervisorRequest{ProjectId=project.Id,SupervisorProfileId=profile.Id,RequestedBy=student.Id,Status="ACCEPTED",RequestedAt=now,RespondedAt=now,CreatedAt=now,UpdatedAt=now};db.SupervisorRequests.Add(request);await db.SaveChangesAsync();
        var assignment=new SupervisorAssignment{ProjectId=project.Id,SupervisorProfileId=profile.Id,SupervisorRequestId=request.Id,AssignedAt=now,CreatedAt=now,UpdatedAt=now};db.SupervisorAssignments.Add(assignment);await db.SaveChangesAsync();
        var deliverable=new Deliverable{ProjectId=project.Id,Title="Final report",Status="DRAFT",DueAt=now.AddDays(2),CreatedBy=student.Id,CreatedAt=now,UpdatedAt=now};db.Deliverables.Add(deliverable);await db.SaveChangesAsync();var result=new Seeded(student.Id,outsider.Id,lecturer.Id,deliverable.Id,assignment.Id,org.Id,profile.Id);seededRows.Add(result);return result;
    }
    private sealed record Seeded(long StudentId,long OutsiderId,long SupervisorId,long DeliverableId,long AssignmentId,long OrganizationId,long ProfileId);
}
