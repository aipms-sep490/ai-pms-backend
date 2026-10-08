using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using AIPMS.Application.Features.Teams.DTOs;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Pdf.IO;

namespace AIPMS.IntegrationTests.Teams;

public sealed partial class InterdisciplinaryWorkflowTests
{
    [Fact]
    public async Task Depart_D07_exports_use_server_scope_valid_formats_and_audit_every_download()
    {
        var s = await SeedAsync();
        var foreign = await SeedAsync();
        using var app = new TeamTestFactory(database, s.Team);
        using var otherApp = new TeamTestFactory(database, foreign.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]);
        using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        using var otherLeader = otherApp.CreateAuthenticatedClient(foreign.Team.Students[0]);
        using var otherMember = otherApp.CreateAuthenticatedClient(foreign.Team.Students[4]);
        var team = await Create(leader, s);
        var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        var project = await Proposal(leader, s);
        var otherTeam = await Create(otherLeader, foreign);
        var otherInvitation = await Invite(otherLeader, otherTeam.Id, foreign.Team.Students[4]);
        await Body<TeamDto>(await otherMember.PostAsync($"/api/v1/teams/invitations/{otherInvitation.Id}/accept", null));
        var otherProject = await Proposal(otherLeader, foreign);
        using var staff = app.CreateAuthenticatedClient(s.LeadStaff, roles: ["DEPARTMENT_STAFF"]);
        const string url = "/api/v1/dashboards/portfolio/export";
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync(url + "?format=html")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(url + $"?departmentId={foreign.LeadDepartment}")).StatusCode);
        var csv = await staff.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        var text = await csv.Content.ReadAsStringAsync();
        Assert.Contains(project.Code, text);
        Assert.DoesNotContain(otherProject.Code, text);
        Assert.True(csv.Headers.Contains("X-Correlation-Id"));
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var xlsx = await staff.GetAsync(url + "?format=xlsx");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        using (var zip = new ZipArchive(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync())))
        using (var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open())
        {
            var xml = XDocument.Load(stream).ToString();
            Assert.Contains(project.Code, xml);
            Assert.DoesNotContain(otherProject.Code, xml);
        }
        var pdf = await staff.GetAsync(url + "?format=pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        using var parsed = PdfReader.Open(new MemoryStream(await pdf.Content.ReadAsByteArrayAsync()), PdfDocumentOpenMode.Import);
        Assert.True(parsed.PageCount > 0);
        await using var db = database.CreateContext();
        Assert.Equal(3, await db.AuditLogs.CountAsync(a => a.ActorUserId == s.LeadStaff && a.Action == "DASHBOARD_EXPORTED"));
        var correlation = Guid.Parse(csv.Headers.GetValues("X-Correlation-Id").Single());
        Assert.True(await db.AuditLogs.AnyAsync(a => a.ActorUserId == s.LeadStaff && a.Action == "DASHBOARD_EXPORTED"
            && a.CorrelationId == correlation));
    }
}
