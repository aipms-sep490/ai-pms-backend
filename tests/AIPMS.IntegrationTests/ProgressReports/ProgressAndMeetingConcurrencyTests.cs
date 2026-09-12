using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Meetings.DTOs;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.ProgressReports;

public sealed class ProgressAndMeetingConcurrencyTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    private static readonly DateTime Now = new(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);

    private sealed record TestProjectContext(
        long ProjectId,
        long TeamId,
        long LeaderUserId,
        long MemberUserId,
        long DepartmentId);

    private async Task<TestProjectContext> SeedTestProjectAsync()
    {
        var s = await database.SeedAsync();
        await using var db = database.CreateContext();

        var dept = await db.Departments.FindAsync(s.DepartmentId);
        var semester = new M.AcademicSemester
        {
            OrganizationId = dept!.OrganizationId,
            Code = Guid.NewGuid().ToString("N"),
            Name = "Semester",
            Status = "ACTIVE",
            StartDate = DateOnly.FromDateTime(Now.AddDays(-30)),
            EndDate = DateOnly.FromDateTime(Now.AddDays(30))
        };
        db.AcademicSemesters.Add(semester);
        await db.SaveChangesAsync();

        var studentRole = await db.Roles.SingleAsync(r => r.Code == AppRoles.Student);

        var memberUser = new M.User
        {
            DepartmentId = s.DepartmentId,
            Email = $"{Guid.NewGuid():N}@test.local",
            FullName = "Member Student",
            PasswordHash = "x",
            Status = "ACTIVE",
            UserRoleUsers = [new() { RoleId = studentRole.Id }]
        };
        db.Users.Add(memberUser);
        await db.SaveChangesAsync();

        var team = new M.Team
        {
            Code = Guid.NewGuid().ToString("N")[..10],
            Name = "Project Team",
            AcademicSemesterId = semester.Id,
            CreatedBy = s.Student,
            Status = "ELIGIBLE",
            TeamMembers =
            [
                new() { AcademicSemesterId = semester.Id, UserId = s.Student, IsLeader = true, JoinedAt = Now.AddDays(-20), CreatedAt = Now.AddDays(-20), UpdatedAt = Now.AddDays(-20) },
                new() { AcademicSemesterId = semester.Id, UserId = memberUser.Id, IsLeader = false, JoinedAt = Now.AddDays(-20), CreatedAt = Now.AddDays(-20), UpdatedAt = Now.AddDays(-20) }
            ]
        };
        db.Teams.Add(team);
        await db.SaveChangesAsync();

        var project = new M.Project
        {
            TeamId = team.Id,
            Title = "Concurrency Test Project",
            Code = Guid.NewGuid().ToString("N")[..10],
            Status = "ACTIVE",
            CreatedBy = s.Student,
            CreatedAt = Now.AddDays(-10),
            UpdatedAt = Now.AddDays(-10)
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        return new TestProjectContext(project.Id, team.Id, s.Student, memberUser.Id, s.DepartmentId);
    }

    #region Finding 1: Update vs Submit Race & Concurrent Submit

    [Fact]
    public async Task UpdateVsSubmit_SubmitWins_StaleUpdateReturns409_AndContentRemainsSubmittedVersion()
    {
        var ctx = await SeedTestProjectAsync();
        long reportId;

        // 1. Seed complete DRAFT report in database
        await using (var seedDb = database.CreateContext())
        {
            var report = new M.ProgressReport
            {
                ProjectId = ctx.ProjectId,
                SubmittedBy = ctx.LeaderUserId,
                ReportType = "WEEKLY",
                PeriodStart = DateOnly.FromDateTime(Now.AddDays(-7)),
                PeriodEnd = DateOnly.FromDateTime(Now),
                Summary = "Initial Submitted Version",
                CompletedWork = "Completed initial deliverables",
                PlannedWork = "Plan next iteration",
                IssuesAndRisks = "No major risks",
                Status = "DRAFT",
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.ProgressReports.Add(report);
            await seedDb.SaveChangesAsync();
            reportId = report.Id;
        }

        // 2. Submit wins: Submit transition executes
        await using (var submitDb = database.CreateContext())
        {
            var submitRepo = new ProgressReportRepository(submitDb);
            var submittedDto = await submitRepo.SubmitAsync(reportId, ctx.LeaderUserId, Now.AddMinutes(5), cancellationToken: CancellationToken.None);
            Assert.Equal("SUBMITTED", submittedDto.Status);
        }

        // 3. Stale update attempt on the now-submitted report MUST return 409 Conflict
        await using (var updateDb = database.CreateContext())
        {
            var updateRepo = new ProgressReportRepository(updateDb);
            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                updateRepo.UpdateAsync(
                    reportId,
                    "Hacked Overwritten Summary",
                    "Hacked Work",
                    "Hacked Planned",
                    "Hacked Risks",
                    Now.AddMinutes(10),
                    CancellationToken.None));

            Assert.Contains("cannot be modified", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // 4. Verify content in DB remains exactly the submitted version
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.ProgressReports.AsNoTracking().SingleAsync(r => r.Id == reportId);
            Assert.Equal("SUBMITTED", persisted.Status);
            Assert.Equal("Initial Submitted Version", persisted.Summary);
            Assert.Equal("Completed initial deliverables", persisted.CompletedWork);
            Assert.Equal("Plan next iteration", persisted.PlannedWork);
            Assert.Equal("No major risks", persisted.IssuesAndRisks);
        }
    }

    [Fact]
    public async Task ConcurrentSubmit_OnlyOneSucceeds_OtherReturns409()
    {
        var ctx = await SeedTestProjectAsync();
        long reportId;

        // Seed complete DRAFT report
        await using (var seedDb = database.CreateContext())
        {
            var report = new M.ProgressReport
            {
                ProjectId = ctx.ProjectId,
                SubmittedBy = ctx.LeaderUserId,
                ReportType = "WEEKLY",
                PeriodStart = DateOnly.FromDateTime(Now.AddDays(-14)),
                PeriodEnd = DateOnly.FromDateTime(Now.AddDays(-8)),
                Summary = "Concurrent Submit Test",
                CompletedWork = "Completed work",
                PlannedWork = "Planned work",
                IssuesAndRisks = "None",
                Status = "DRAFT",
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.ProgressReports.Add(report);
            await seedDb.SaveChangesAsync();
            reportId = report.Id;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task1 = Task.Run(async () =>
        {
            await tcs.Task;
            await using var db1 = database.CreateContext();
            var repo1 = new ProgressReportRepository(db1);
            return await repo1.SubmitAsync(reportId, ctx.LeaderUserId, Now.AddMinutes(1), cancellationToken: CancellationToken.None);
        });

        var task2 = Task.Run(async () =>
        {
            await tcs.Task;
            await using var db2 = database.CreateContext();
            var repo2 = new ProgressReportRepository(db2);
            return await repo2.SubmitAsync(reportId, ctx.LeaderUserId, Now.AddMinutes(1), cancellationToken: CancellationToken.None);
        });

        // Release both tasks at once
        tcs.SetResult();

        var results = await Task.WhenAll(
            task1.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Exception: t.Exception?.InnerException)),
            task2.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Exception: t.Exception?.InnerException)));

        var successes = results.Count(r => r.Success);
        var conflicts = results.Count(r => r.Exception is ConflictException);

        Assert.Equal(1, successes);
        Assert.Equal(1, conflicts);

        // Verify row state in DB
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.ProgressReports.AsNoTracking().SingleAsync(r => r.Id == reportId);
            Assert.Equal("SUBMITTED", persisted.Status);
            Assert.NotNull(persisted.SubmittedAt);
        }
    }

    #endregion

    #region Finding 4: Concurrent Duplicate Progress Report Create

    [Fact]
    public async Task ConcurrentCreate_SameProjectTypePeriod_OneSucceeds_OneReturns409()
    {
        var ctx = await SeedTestProjectAsync();
        var periodStart = DateOnly.FromDateTime(Now.AddDays(-21));
        var periodEnd = DateOnly.FromDateTime(Now.AddDays(-15));

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task1 = Task.Run(async () =>
        {
            await tcs.Task;
            await using var db1 = database.CreateContext();
            var repo1 = new ProgressReportRepository(db1);
            return await repo1.CreateAsync(
                ctx.ProjectId, ctx.LeaderUserId, "WEEKLY", periodStart, periodEnd,
                "Summary 1", "Work 1", "Plan 1", "Risk 1", Now, CancellationToken.None);
        });

        var task2 = Task.Run(async () =>
        {
            await tcs.Task;
            await using var db2 = database.CreateContext();
            var repo2 = new ProgressReportRepository(db2);
            return await repo2.CreateAsync(
                ctx.ProjectId, ctx.LeaderUserId, "WEEKLY", periodStart, periodEnd,
                "Summary 2", "Work 2", "Plan 2", "Risk 2", Now, CancellationToken.None);
        });

        tcs.SetResult();

        var results = await Task.WhenAll(
            task1.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Exception: t.Exception?.InnerException)),
            task2.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Exception: t.Exception?.InnerException)));

        var successes = results.Count(r => r.Success);
        var conflicts = results.Count(r => r.Exception is ConflictException);

        Assert.Equal(1, successes);
        Assert.Equal(1, conflicts);

        // Assert exactly one DB row exists
        await using (var verifyDb = database.CreateContext())
        {
            var count = await verifyDb.ProgressReports
                .AsNoTracking()
                .CountAsync(r => r.ProjectId == ctx.ProjectId && r.ReportType == "WEEKLY" && r.PeriodStart == periodStart);
            Assert.Equal(1, count);
        }
    }

    #endregion

    #region Finding 8: Concurrent AddParticipant Race

    [Fact]
    public async Task ConcurrentAddParticipant_SameMeetingUser_OneSucceeds_OneReturns409()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;

        // Seed a meeting
        await using (var seedDb = database.CreateContext())
        {
            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Sprint Coordination",
                StartAt = Now.AddDays(2),
                Status = "SCHEDULED",
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.Meetings.Add(meeting);
            await seedDb.SaveChangesAsync();
            meetingId = meeting.Id;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task1 = Task.Run(async () =>
        {
            await tcs.Task;
            await using var db1 = database.CreateContext();
            var repo1 = new MeetingRepository(db1);
            return await repo1.AddParticipantAsync(meetingId, ctx.MemberUserId, "INVITED", Now, CancellationToken.None);
        });

        var task2 = Task.Run(async () =>
        {
            await tcs.Task;
            await using var db2 = database.CreateContext();
            var repo2 = new MeetingRepository(db2);
            return await repo2.AddParticipantAsync(meetingId, ctx.MemberUserId, "INVITED", Now, CancellationToken.None);
        });

        tcs.SetResult();

        var results = await Task.WhenAll(
            task1.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Exception: t.Exception?.InnerException)),
            task2.ContinueWith(t => (Success: t.IsCompletedSuccessfully, Exception: t.Exception?.InnerException)));

        var successes = results.Count(r => r.Success);
        var conflicts = results.Count(r => r.Exception is ConflictException);

        Assert.Equal(1, successes);
        Assert.Equal(1, conflicts);

        // Assert exactly one DB row exists for this meeting and user
        await using (var verifyDb = database.CreateContext())
        {
            var count = await verifyDb.MeetingParticipants
                .AsNoTracking()
                .CountAsync(p => p.MeetingId == meetingId && p.UserId == ctx.MemberUserId);
            Assert.Equal(1, count);
        }
    }

    #endregion

    #region Finding 2: Meeting Mutation vs Cancel Race & Terminal State Protection

    [Fact]
    public async Task UpdateVsCancel_CancelWins_StaleUpdateReturns409_AndOriginalMeetingDataPreserved()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;

        // 1. Seed SCHEDULED meeting in database
        await using (var seedDb = database.CreateContext())
        {
            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Original Meeting Title",
                Agenda = "Original Agenda",
                StartAt = Now.AddDays(2),
                Status = "SCHEDULED",
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.Meetings.Add(meeting);
            await seedDb.SaveChangesAsync();
            meetingId = meeting.Id;
        }

        // 2. Cancel wins: cancel commits first
        await using (var cancelDb = database.CreateContext())
        {
            var cancelRepo = new MeetingRepository(cancelDb);
            var cancelled = await cancelRepo.CancelAsync(meetingId, Now.AddMinutes(1), CancellationToken.None);
            Assert.Equal("CANCELLED", cancelled.Status);
        }

        // 3. Stale update attempt on now-cancelled meeting MUST return 409 Conflict
        await using (var updateDb = database.CreateContext())
        {
            var updateRepo = new MeetingRepository(updateDb);
            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                updateRepo.UpdateAsync(
                    meetingId,
                    "Hacked Overwritten Title",
                    "Hacked Agenda",
                    Now.AddDays(5),
                    Now.AddDays(5).AddHours(1),
                    "Room Hack",
                    null,
                    Now.AddMinutes(2),
                    CancellationToken.None));

            Assert.Contains("cannot be updated", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // 4. Assert DB state: status remains CANCELLED and title/agenda NOT overwritten
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.Meetings.AsNoTracking().SingleAsync(m => m.Id == meetingId);
            Assert.Equal("CANCELLED", persisted.Status);
            Assert.Equal("Original Meeting Title", persisted.Title);
            Assert.Equal("Original Agenda", persisted.Agenda);
        }
    }

    [Fact]
    public async Task AddParticipantVsCancel_CancelWins_StaleAddReturns409()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;

        await using (var seedDb = database.CreateContext())
        {
            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Sync Meeting",
                StartAt = Now.AddDays(2),
                Status = "SCHEDULED",
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.Meetings.Add(meeting);
            await seedDb.SaveChangesAsync();
            meetingId = meeting.Id;
        }

        // Cancel wins
        await using (var cancelDb = database.CreateContext())
        {
            var cancelRepo = new MeetingRepository(cancelDb);
            await cancelRepo.CancelAsync(meetingId, Now.AddMinutes(1), CancellationToken.None);
        }

        // Stale add participant attempt
        await using (var addDb = database.CreateContext())
        {
            var addRepo = new MeetingRepository(addDb);
            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                addRepo.AddParticipantAsync(meetingId, ctx.MemberUserId, "INVITED", Now.AddMinutes(2), CancellationToken.None));

            Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Assert DB state: no participant was added
        await using (var verifyDb = database.CreateContext())
        {
            var count = await verifyDb.MeetingParticipants.AsNoTracking().CountAsync(p => p.MeetingId == meetingId);
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task RemoveParticipantVsCancel_CancelWins_StaleRemoveReturns409()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;

        await using (var seedDb = database.CreateContext())
        {
            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Meeting with Participant",
                StartAt = Now.AddDays(2),
                Status = "SCHEDULED",
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now,
                MeetingParticipants =
                [
                    new() { UserId = ctx.MemberUserId, AttendanceStatus = "INVITED", CreatedAt = Now, UpdatedAt = Now }
                ]
            };
            seedDb.Meetings.Add(meeting);
            await seedDb.SaveChangesAsync();
            meetingId = meeting.Id;
        }

        // Cancel wins
        await using (var cancelDb = database.CreateContext())
        {
            var cancelRepo = new MeetingRepository(cancelDb);
            await cancelRepo.CancelAsync(meetingId, Now.AddMinutes(1), CancellationToken.None);
        }

        // Stale remove participant attempt
        await using (var removeDb = database.CreateContext())
        {
            var removeRepo = new MeetingRepository(removeDb);
            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                removeRepo.RemoveParticipantAsync(meetingId, ctx.MemberUserId, CancellationToken.None));

            Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Assert DB state: participant was NOT removed
        await using (var verifyDb = database.CreateContext())
        {
            var count = await verifyDb.MeetingParticipants.AsNoTracking().CountAsync(p => p.MeetingId == meetingId);
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task NotesVsCancel_CancelWins_StaleNotesReturns409()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;

        await using (var seedDb = database.CreateContext())
        {
            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Meeting For Notes",
                StartAt = Now.AddDays(2),
                Status = "SCHEDULED",
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.Meetings.Add(meeting);
            await seedDb.SaveChangesAsync();
            meetingId = meeting.Id;
        }

        // Cancel wins
        await using (var cancelDb = database.CreateContext())
        {
            var cancelRepo = new MeetingRepository(cancelDb);
            await cancelRepo.CancelAsync(meetingId, Now.AddMinutes(1), CancellationToken.None);
        }

        // Stale notes update attempt
        await using (var notesDb = database.CreateContext())
        {
            var notesRepo = new MeetingRepository(notesDb);
            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                notesRepo.UpdateNotesAsync(meetingId, "Stale Notes Content", null, Now.AddMinutes(2), CancellationToken.None));

            Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Assert DB state: notes not overwritten
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.Meetings.AsNoTracking().SingleAsync(m => m.Id == meetingId);
            Assert.Null(persisted.MeetingNotes);
        }
    }

    #endregion

    #region Finding 3: Submit + Audit Atomicity & Rollback Tests

    private sealed class FaultyAuditFactory(SupervisorDatabaseFixture database) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString
            }));
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IAuditTrail, ThrowingAuditTrail>();
            });
        }
    }

    private sealed class ThrowingAuditTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated audit failure inside atomic transaction.");
        }
    }

    [Fact]
    public async Task Submit_WhenAuditFails_RollsBackReportAndAuditAtomically()
    {
        var ctx = await SeedTestProjectAsync();
        long reportId;

        // Seed a complete DRAFT report
        await using (var seedDb = database.CreateContext())
        {
            var report = new M.ProgressReport
            {
                ProjectId = ctx.ProjectId,
                SubmittedBy = ctx.LeaderUserId,
                ReportType = "WEEKLY",
                PeriodStart = DateOnly.FromDateTime(Now.AddDays(-14)),
                PeriodEnd = DateOnly.FromDateTime(Now.AddDays(-7)),
                Summary = "Original Draft Summary",
                CompletedWork = "Completed tasks A and B",
                PlannedWork = "Plan tasks C and D",
                IssuesAndRisks = "Identified dependency risks",
                Status = "DRAFT",
                CreatedAt = Now.AddDays(-7),
                UpdatedAt = Now.AddDays(-7)
            };
            seedDb.ProgressReports.Add(report);
            await seedDb.SaveChangesAsync();
            reportId = report.Id;
        }

        using var app = new FaultyAuditFactory(database);
        using var leaderClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.Student]);

        // Submit via HTTP API with faulty audit trail
        var response = await leaderClient.PostAsync($"/api/v1/progress-reports/{reportId}/submit", null);

        // Assert: Endpoint returned failure (500)
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // Assert DB state reloaded in fresh DbContext:
        // Report MUST be rolled back to DRAFT, SubmittedAt MUST be null, and NO submit audit row exists
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.ProgressReports.AsNoTracking().SingleAsync(r => r.Id == reportId);
            Assert.Equal("DRAFT", persisted.Status);
            Assert.Null(persisted.SubmittedAt);
            Assert.Equal("Original Draft Summary", persisted.Summary);
            Assert.Equal("Completed tasks A and B", persisted.CompletedWork);
            Assert.Equal("Plan tasks C and D", persisted.PlannedWork);
            Assert.Equal("Identified dependency risks", persisted.IssuesAndRisks);

            var auditCount = await verifyDb.AuditLogs
                .AsNoTracking()
                .CountAsync(a => a.EntityId == reportId.ToString() && a.Action == "PROGRESS_REPORT_SUBMITTED");
            Assert.Equal(0, auditCount);
        }
    }

    #endregion
}