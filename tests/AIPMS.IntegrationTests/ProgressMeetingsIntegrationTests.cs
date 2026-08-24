using System.Net;
using System.Net.Http.Json;
using AIPMS.Api.Controllers;
using AIPMS.Application.Features.ProgressMeetings.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AIPMS.Infrastructure.Persistence.Models;

namespace AIPMS.IntegrationTests;

[Collection("ProgressMeetings database")]
public sealed class ProgressMeetingsIntegrationTests(AipmsWebApplicationFactory factory)
    : IClassFixture<AipmsWebApplicationFactory>
{
    [Fact]
    public async Task Sample_leader_can_submit_report_and_schedule_notified_meeting()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
        var project = await db.Projects.AsNoTracking().Select(p => new
        {
            p.Id,
            LeaderId = p.Team.TeamMembers.Where(m => m.IsLeader && m.LeftAt == null).Select(m => m.UserId).First(),
            Members = p.Team.TeamMembers.Where(m => m.LeftAt == null).Select(m => m.UserId).ToArray()
        }).FirstAsync(x => x.Members.Length >= 2);
        var client = factory.CreateAuthenticatedClient(project.LeaderId);
        long reportId = 0, blockedReportId = 0, meetingId = 0, periodId = 0, blockedPeriodId = 0;
        try
        {
            var marker = DateTime.UtcNow.Ticks % 1000000;
            var periodStart = new DateOnly(2098, 1, 1).AddDays((int)(marker % 300));
            var period = new ProgressReportPeriod { ProjectId = project.Id, ReportType = "WEEKLY", PeriodStart = periodStart,
                PeriodEnd = periodStart.AddDays(6), DeadlineAt = DateTime.UtcNow.AddMinutes(-1), LatePolicy = "FLAG", Status = "OPEN" };
            db.Set<ProgressReportPeriod>().Add(period); await db.SaveChangesAsync(); periodId = period.Id;
            var reportResponse = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/progress-reports",
                new CreateProgressReportPayload(period.Id, "Integration summary", "Completed", "Working", "None", "Low", "Next"));
            Assert.True(reportResponse.StatusCode == HttpStatusCode.OK, await reportResponse.Content.ReadAsStringAsync());
            var report = await reportResponse.Content.ReadFromJsonAsync<ProgressReportDto>();
            reportId = report!.Id;
            Assert.Equal(5, report.Sections.Count);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/v1/progress-reports/{reportId}/contributions", new ContributionPayload("IN_PROGRESS", "Member contribution"))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/progress-reports/{reportId}/submit", null)).StatusCode);
            Assert.Equal("SUBMITTED", (await db.ProgressReports.AsNoTracking().SingleAsync(x => x.Id == reportId)).Status);
            Assert.True((await db.Set<ProgressReportMetadata>().AsNoTracking().SingleAsync(x => x.ReportId == reportId)).IsLate);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/progress-reports/{reportId}/contributions", new ContributionPayload("RISKS", "Too late"))).StatusCode);

            var blockedPeriod = new ProgressReportPeriod { ProjectId = project.Id, ReportType = "WEEKLY", PeriodStart = periodStart.AddDays(14),
                PeriodEnd = periodStart.AddDays(20), DeadlineAt = DateTime.UtcNow.AddMinutes(-1), LatePolicy = "BLOCK", Status = "OPEN" };
            db.Set<ProgressReportPeriod>().Add(blockedPeriod); await db.SaveChangesAsync(); blockedPeriodId = blockedPeriod.Id;
            var blockedCreate = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/progress-reports",
                new CreateProgressReportPayload(blockedPeriod.Id, "Blocked late report", "", "", "", "", ""));
            var blockedReport = await blockedCreate.Content.ReadFromJsonAsync<ProgressReportDto>(); blockedReportId = blockedReport!.Id;
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/progress-reports/{blockedReportId}/submit", null)).StatusCode);

            var start = DateTime.UtcNow.AddDays(30);
            var meetingResponse = await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/meetings",
                new CreateMeetingPayload($"BE03 integration {marker}", "Agenda", start, start.AddHours(1), "Room", null, project.Members));
            Assert.True(meetingResponse.StatusCode == HttpStatusCode.OK, await meetingResponse.Content.ReadAsStringAsync());
            var meeting = await meetingResponse.Content.ReadFromJsonAsync<MeetingDto>();
            meetingId = meeting!.Id;
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/v1/meetings/{meetingId}/decisions", new MeetingTextPayload("Approved architecture"))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/v1/meetings/{meetingId}/blockers", new MeetingTextPayload("Waiting for review"))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/v1/meetings/{meetingId}/action-items", new CreateActionItemPayload("Follow up", null, project.LeaderId, DateOnly.FromDateTime(start.AddDays(2)), "TODO", null, null))).StatusCode);
            var outsiderId = await db.Users.Where(u => !project.Members.Contains(u.Id)).Select(u => u.Id).FirstAsync();
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/v1/meetings/{meetingId}/action-items", new CreateActionItemPayload("Invalid owner", null, outsiderId, null, "TODO", null, null))).StatusCode);
            Assert.Equal(project.Members.Length, await db.MeetingParticipants.CountAsync(x => x.MeetingId == meetingId));
            Assert.Single(await db.Set<MeetingDecision>().Where(x => x.MeetingId == meetingId).ToListAsync());
            Assert.Single(await db.Set<MeetingBlocker>().Where(x => x.MeetingId == meetingId).ToListAsync());
            Assert.Single(await db.Set<MeetingActionItem>().Where(x => x.MeetingId == meetingId).ToListAsync());
            var notification = await db.Notifications.Include(x => x.NotificationRecipients).SingleAsync(x => x.RelatedEntityType == "MEETING" && x.RelatedEntityId == meetingId);
            Assert.Equal(project.Members.Length, notification.NotificationRecipients.Count);
        }
        finally
        {
            if (meetingId != 0)
            {
                var notifications = await db.Notifications.Where(x => x.RelatedEntityType == "MEETING" && x.RelatedEntityId == meetingId).ToListAsync();
                db.Notifications.RemoveRange(notifications);
                var meeting = await db.Meetings.FindAsync(meetingId); if (meeting is not null) db.Meetings.Remove(meeting);
            }
            if (reportId != 0) { var report = await db.ProgressReports.FindAsync(reportId); if (report is not null) db.ProgressReports.Remove(report); }
            if (blockedReportId != 0) { var report = await db.ProgressReports.FindAsync(blockedReportId); if (report is not null) db.ProgressReports.Remove(report); }
            await db.SaveChangesAsync();
            if (periodId != 0) { var period = await db.Set<ProgressReportPeriod>().FindAsync(periodId); if (period is not null) db.Set<ProgressReportPeriod>().Remove(period); await db.SaveChangesAsync(); }
            if (blockedPeriodId != 0) { var period = await db.Set<ProgressReportPeriod>().FindAsync(blockedPeriodId); if (period is not null) db.Set<ProgressReportPeriod>().Remove(period); await db.SaveChangesAsync(); }
        }
    }
}

[CollectionDefinition("ProgressMeetings database", DisableParallelization = true)]
public sealed class ProgressMeetingsDatabaseCollection;
