using AIPMS.Application.Features.ActionItems.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Meetings.DTOs;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.Infrastructure.Services.Auditing;
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
        long DepartmentId,
        long SupervisorUserId,
        long SupervisorProfileId,
        long SupervisorAssignmentId);

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

        var projectPeriod = new M.ProjectPeriod
        {
            AcademicSemesterId = semester.Id,
            Code = Guid.NewGuid().ToString("N").Substring(0, 10),
            Name = "Execution Period",
            PeriodType = "EXECUTION",
            Status = "ACTIVE",
            StartAt = Now.AddDays(-20),
            EndAt = Now.AddDays(20),
            CreatedAt = Now.AddDays(-20),
            UpdatedAt = Now.AddDays(-20)
        };
        db.ProjectPeriods.Add(projectPeriod);
        await db.SaveChangesAsync();

        var studentRole = await db.Roles.SingleAsync(r => r.Code == AppRoles.Student);
        var staffRole = await db.Roles.SingleAsync(r => r.Code == AppRoles.DepartmentStaff);

        // Add DepartmentStaff role to the leader user so they can create reporting cycles
        var leaderUser = await db.Users.FindAsync(s.Student);
        if (leaderUser != null)
        {
            db.UserRoles.Add(new M.UserRole { UserId = leaderUser.Id, RoleId = staffRole.Id, AssignedAt = Now });
            await db.SaveChangesAsync();
        }

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

        var request = new M.SupervisorRequest
        {
            ProjectId = project.Id,
            SupervisorProfileId = s.ProfileId,
            RequestedBy = s.Student,
            Status = "ACCEPTED",
            RequestedAt = Now.AddDays(-1)
        };
        db.SupervisorRequests.Add(request);
        await db.SaveChangesAsync();

        var assignment = new M.SupervisorAssignment
        {
            ProjectId = project.Id,
            SupervisorProfileId = s.ProfileId,
            SupervisorRequestId = request.Id,
            IsPrimary = true,
            AssignedAt = Now.AddDays(-1)
        };
        db.SupervisorAssignments.Add(assignment);
        await db.SaveChangesAsync();

        return new TestProjectContext(project.Id, team.Id, s.Student, memberUser.Id, s.DepartmentId, s.Lecturer, s.ProfileId, assignment.Id);
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
            var projectPeriod = await seedDb.ProjectPeriods.FirstAsync();
            var cycle = new AIPMS.Infrastructure.Persistence.Models.ProgressReportPeriod
            {
                ProjectId = ctx.ProjectId,
                ProjectPeriodId = projectPeriod.Id,
                ReportType = "WEEKLY",
                PeriodStart = DateOnly.FromDateTime(Now.AddDays(-7)).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                PeriodEnd = DateOnly.FromDateTime(Now).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Deadline = Now.AddDays(7),
                LatePolicy = "BLOCK",
                ConcurrencyToken = Guid.NewGuid(),
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.ProgressReportPeriods.Add(cycle);
            await seedDb.SaveChangesAsync();

            var report = new M.ProgressReport
            {
                ProjectId = ctx.ProjectId,
                ProgressReportPeriodId = cycle.Id,
                SubmittedBy = ctx.LeaderUserId,
                ReportType = "WEEKLY",
                PeriodStart = DateOnly.FromDateTime(Now.AddDays(-7)),
                PeriodEnd = DateOnly.FromDateTime(Now),
                Summary = "Initial Submitted Version",
                CompletedWork = "Completed initial deliverables",
                PlannedWork = "Plan next iteration",
                IssuesAndRisks = "No major risks",
                InProgressWork = "Ongoing work",
                Blockers = "No major risks",
                Risks = "None",
                NextActions = "Plan next iteration",
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
            var projectPeriod = await seedDb.ProjectPeriods.FirstAsync();
            var cycle = new AIPMS.Infrastructure.Persistence.Models.ProgressReportPeriod
            {
                ProjectId = ctx.ProjectId,
                ProjectPeriodId = projectPeriod.Id,
                ReportType = "WEEKLY",
                PeriodStart = DateOnly.FromDateTime(Now.AddDays(-14)).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                PeriodEnd = DateOnly.FromDateTime(Now.AddDays(-8)).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Deadline = Now.AddDays(7),
                LatePolicy = "BLOCK",
                ConcurrencyToken = Guid.NewGuid(),
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.ProgressReportPeriods.Add(cycle);
            await seedDb.SaveChangesAsync();

            var report = new M.ProgressReport
            {
                ProjectId = ctx.ProjectId,
                ProgressReportPeriodId = cycle.Id,
                SubmittedBy = ctx.LeaderUserId,
                ReportType = "WEEKLY",
                PeriodStart = DateOnly.FromDateTime(Now.AddDays(-14)),
                PeriodEnd = DateOnly.FromDateTime(Now.AddDays(-8)),
                Summary = "Concurrent Submit Test",
                CompletedWork = "Completed work",
                PlannedWork = "Planned work",
                IssuesAndRisks = "None",
                InProgressWork = "Ongoing work",
                Blockers = "None",
                Risks = "None",
                NextActions = "Planned work",
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

    #region Finding 3: Submit & Meeting Mutation + Audit Atomicity & Rollback Tests

    public sealed class ConfigurableAuditState
    {
        public bool FailOnMeetingCompleted { get; set; }
        public bool FailOnMeetingCancelled { get; set; }
        public bool FailOnProgressReportSubmitted { get; set; }
        public bool FailOnProgressReportCreated { get; set; }
        public bool FailOnProgressReportUpdated { get; set; }
        public bool FailOnProgressReportFeedbackAdded { get; set; }
        public bool FailOnReportingCycleCreated { get; set; }
        public bool FailOnProjectActionItemCreated { get; set; }
    }

    private sealed class ConfigurableAuditTrail(
        AipmsDbContext context,
        IRequestContext requestContext,
        TimeProvider timeProvider,
        ConfigurableAuditState state) : IAuditTrail
    {
        private readonly DatabaseAuditTrail _inner = new(context, requestContext, timeProvider);

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (entry.Action == "MEETING_COMPLETED" && state.FailOnMeetingCompleted)
            {
                throw new InvalidOperationException("Simulated audit failure on MEETING_COMPLETED inside atomic transaction.");
            }

            if (entry.Action == "MEETING_CANCELLED" && state.FailOnMeetingCancelled)
            {
                throw new InvalidOperationException("Simulated audit failure on MEETING_CANCELLED inside atomic transaction.");
            }

            if (entry.Action == "PROGRESS_REPORT_SUBMITTED" && state.FailOnProgressReportSubmitted)
            {
                throw new InvalidOperationException("Simulated audit failure on PROGRESS_REPORT_SUBMITTED inside atomic transaction.");
            }

            if (entry.Action == "PROGRESS_REPORT_CREATED" && state.FailOnProgressReportCreated)
            {
                throw new InvalidOperationException("Simulated audit failure on PROGRESS_REPORT_CREATED inside atomic transaction.");
            }

            if (entry.Action == "PROGRESS_REPORT_UPDATED" && state.FailOnProgressReportUpdated)
            {
                throw new InvalidOperationException("Simulated audit failure on PROGRESS_REPORT_UPDATED inside atomic transaction.");
            }

            if (entry.Action == "PROGRESS_REPORT_FEEDBACK_ADDED" && state.FailOnProgressReportFeedbackAdded)
            {
                throw new InvalidOperationException("Simulated audit failure on PROGRESS_REPORT_FEEDBACK_ADDED inside atomic transaction.");
            }

            if (entry.Action == "REPORTING_CYCLE_CREATED" && state.FailOnReportingCycleCreated)
            {
                throw new InvalidOperationException("Simulated audit failure on REPORTING_CYCLE_CREATED inside atomic transaction.");
            }

            if (entry.Action == "PROJECT_ACTION_ITEM_CREATED" && state.FailOnProjectActionItemCreated)
            {
                throw new InvalidOperationException("Simulated audit failure on PROJECT_ACTION_ITEM_CREATED inside atomic transaction.");
            }

            return _inner.RecordAsync(entry, cancellationToken);
        }
    }

        private sealed class NormalFactory(SupervisorDatabaseFixture database) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString
            }));
        }
    }

    private sealed class FaultyAuditFactory(SupervisorDatabaseFixture database) : AipmsWebApplicationFactory
    {
        public ConfigurableAuditState AuditState { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString
            }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(AuditState);
                services.AddScoped<IAuditTrail, ConfigurableAuditTrail>();
            });
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
            var projectPeriod = await seedDb.ProjectPeriods.FirstAsync();
            var cycle = new AIPMS.Infrastructure.Persistence.Models.ProgressReportPeriod
            {
                ProjectId = ctx.ProjectId,
                ProjectPeriodId = projectPeriod.Id,
                ReportType = "WEEKLY",
                PeriodStart = DateOnly.FromDateTime(Now.AddDays(-14)).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                PeriodEnd = DateOnly.FromDateTime(Now.AddDays(-7)).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Deadline = DateTime.UtcNow.AddDays(30),
                LatePolicy = "BLOCK",
                ConcurrencyToken = Guid.NewGuid(),
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now.AddDays(-7),
                UpdatedAt = Now.AddDays(-7)
            };
            seedDb.ProgressReportPeriods.Add(cycle);
            await seedDb.SaveChangesAsync();

            var report = new M.ProgressReport
            {
                ProjectId = ctx.ProjectId,
                ProgressReportPeriodId = cycle.Id,
                SubmittedBy = ctx.LeaderUserId,
                ReportType = "WEEKLY",
                PeriodStart = DateOnly.FromDateTime(Now.AddDays(-14)),
                PeriodEnd = DateOnly.FromDateTime(Now.AddDays(-7)),
                Summary = "Original Draft Summary",
                CompletedWork = "Completed tasks A and B",
                PlannedWork = "Plan tasks C and D",
                IssuesAndRisks = "Identified dependency risks",
                InProgressWork = "Ongoing task X",
                Blockers = "No blockers",
                Risks = "Identified dependency risks",
                NextActions = "Plan tasks C and D",
                Status = "DRAFT",
                CreatedAt = Now.AddDays(-7),
                UpdatedAt = Now.AddDays(-7)
            };
            seedDb.ProgressReports.Add(report);
            await seedDb.SaveChangesAsync();
            reportId = report.Id;
        }

        using var app = new FaultyAuditFactory(database);
        app.AuditState.FailOnProgressReportSubmitted = true;
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

    [Fact]
    public async Task CompleteMeeting_WhenAuditFails_RollsBackToScheduled_AndCanRetry()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;

        await using (var seedDb = database.CreateContext())
        {
            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Sprint Review Meeting",
                Agenda = "Review sprint deliverables",
                Location = "Room 301",
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

        using var app = new FaultyAuditFactory(database);
        app.AuditState.FailOnMeetingCompleted = true;
        using var leaderClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.Student]);

        // 1. Call complete endpoint with failing audit trail
        var failResponse = await leaderClient.PostAsync($"/api/v1/meetings/{meetingId}/complete", null);
        Assert.Equal(HttpStatusCode.InternalServerError, failResponse.StatusCode);

        // 2. Open / reload via FRESH DbContext to verify atomic rollback
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.Meetings.AsNoTracking().SingleAsync(m => m.Id == meetingId);
            Assert.Equal("SCHEDULED", persisted.Status);
            Assert.Equal("Sprint Review Meeting", persisted.Title);
            Assert.Equal("Review sprint deliverables", persisted.Agenda);
            Assert.Equal("Room 301", persisted.Location);

            var auditCount = await verifyDb.AuditLogs
                .AsNoTracking()
                .CountAsync(a => a.EntityId == meetingId.ToString() && a.Action == "MEETING_COMPLETED");
            Assert.Equal(0, auditCount);
        }

        // 3. Disable audit failure and retry
        app.AuditState.FailOnMeetingCompleted = false;
        var retryResponse = await leaderClient.PostAsync($"/api/v1/meetings/{meetingId}/complete", null);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);

        var completedDto = await retryResponse.Content.ReadFromJsonAsync<MeetingDto>();
        Assert.NotNull(completedDto);
        Assert.Equal("COMPLETED", completedDto.Status);

        // 4. Verify DB state after retry: Status is COMPLETED and exactly ONE audit row exists
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.Meetings.AsNoTracking().SingleAsync(m => m.Id == meetingId);
            Assert.Equal("COMPLETED", persisted.Status);

            var auditCount = await verifyDb.AuditLogs
                .AsNoTracking()
                .CountAsync(a => a.EntityId == meetingId.ToString() && a.Action == "MEETING_COMPLETED");
            Assert.Equal(1, auditCount);
        }
    }

    [Fact]
    public async Task CancelMeeting_WhenAuditFails_RollsBackState_AndCanRetry()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;

        await using (var seedDb = database.CreateContext())
        {
            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Standup To Cancel",
                Agenda = "Daily sync",
                StartAt = Now.AddDays(1),
                Status = "SCHEDULED",
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.Meetings.Add(meeting);
            await seedDb.SaveChangesAsync();
            meetingId = meeting.Id;
        }

        using var app = new FaultyAuditFactory(database);
        app.AuditState.FailOnMeetingCancelled = true;
        using var leaderClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.Student]);

        // 1. Call cancel endpoint with failing audit trail
        var failResponse = await leaderClient.PostAsync($"/api/v1/meetings/{meetingId}/cancel", null);
        Assert.Equal(HttpStatusCode.InternalServerError, failResponse.StatusCode);

        // 2. Open / reload via FRESH DbContext to verify atomic rollback
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.Meetings.AsNoTracking().SingleAsync(m => m.Id == meetingId);
            Assert.Equal("SCHEDULED", persisted.Status);
            Assert.Equal("Standup To Cancel", persisted.Title);

            var auditCount = await verifyDb.AuditLogs
                .AsNoTracking()
                .CountAsync(a => a.EntityId == meetingId.ToString() && a.Action == "MEETING_CANCELLED");
            Assert.Equal(0, auditCount);
        }

        // 3. Disable audit failure and retry
        app.AuditState.FailOnMeetingCancelled = false;
        var retryResponse = await leaderClient.PostAsync($"/api/v1/meetings/{meetingId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);

        var cancelledDto = await retryResponse.Content.ReadFromJsonAsync<MeetingDto>();
        Assert.NotNull(cancelledDto);
        Assert.Equal("CANCELLED", cancelledDto.Status);

        // 4. Verify DB state after retry: Status is CANCELLED and exactly ONE audit row exists
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.Meetings.AsNoTracking().SingleAsync(m => m.Id == meetingId);
            Assert.Equal("CANCELLED", persisted.Status);

            var auditCount = await verifyDb.AuditLogs
                .AsNoTracking()
                .CountAsync(a => a.EntityId == meetingId.ToString() && a.Action == "MEETING_CANCELLED");
            Assert.Equal(1, auditCount);
        }
    }

    [Fact]
    public async Task CreateProgressReport_WhenAuditFails_RollsBackAndCanRetry()
    {
        var ctx = await SeedTestProjectAsync();
        using var app = new FaultyAuditFactory(database);
        app.AuditState.FailOnProgressReportCreated = true;
        using var leaderClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.Student]);

        var createReq = new CreateProgressReportRequest(
            "WEEKLY",
            DateOnly.FromDateTime(Now.AddDays(-7)),
            DateOnly.FromDateTime(Now),
            "Initial Draft Summary",
            "Initial Done",
            "Initial Planned",
            "Initial Risks");

        // 1. First attempt fails due to audit failure (HTTP 500)
        var failResponse = await leaderClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/progress-reports", createReq);
        Assert.Equal(HttpStatusCode.InternalServerError, failResponse.StatusCode);

        // 2. Verify with fresh DbContext: NO report exists in DB, 0 audit rows exist
        await using (var verifyDb = database.CreateContext())
        {
            var reportCount = await verifyDb.ProgressReports.AsNoTracking().CountAsync(r => r.ProjectId == ctx.ProjectId);
            Assert.Equal(0, reportCount);

            var auditCount = await verifyDb.AuditLogs.AsNoTracking().CountAsync(a => a.Action == "PROGRESS_REPORT_CREATED" && a.ActorUserId == ctx.LeaderUserId);
            Assert.Equal(0, auditCount);
        }

        // 3. Turn off failure and retry: retry succeeds (201 Created)
        app.AuditState.FailOnProgressReportCreated = false;
        var successResponse = await leaderClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/progress-reports", createReq);
        Assert.Equal(HttpStatusCode.Created, successResponse.StatusCode);
        var created = await successResponse.Content.ReadFromJsonAsync<ProgressReportDto>();
        Assert.NotNull(created);
        Assert.Equal("DRAFT", created.Status);

        // 4. Verify with fresh DbContext: exactly 1 report and exactly 1 audit row exist
        await using (var verifyDb = database.CreateContext())
        {
            var reports = await verifyDb.ProgressReports.AsNoTracking().Where(r => r.ProjectId == ctx.ProjectId).ToListAsync();
            Assert.Single(reports);
            Assert.Equal(created.Id, reports[0].Id);
            Assert.Equal("DRAFT", reports[0].Status);

            var auditCount = await verifyDb.AuditLogs.AsNoTracking().CountAsync(a => a.EntityId == created.Id.ToString() && a.Action == "PROGRESS_REPORT_CREATED");
            Assert.Equal(1, auditCount);
        }
    }

    [Fact]
    public async Task UpdateProgressReport_WhenAuditFails_RollsBackContentAndCanRetry()
    {
        var ctx = await SeedTestProjectAsync();
        long reportId;

        await using (var seedDb = database.CreateContext())
        {
            var report = new M.ProgressReport
            {
                ProjectId = ctx.ProjectId,
                SubmittedBy = ctx.LeaderUserId,
                ReportType = "WEEKLY",
                PeriodStart = DateOnly.FromDateTime(Now.AddDays(-7)),
                PeriodEnd = DateOnly.FromDateTime(Now),
                Summary = "Original Draft Summary",
                CompletedWork = "Original Work",
                PlannedWork = "Original Plan",
                IssuesAndRisks = "Original Risks",
                Status = "DRAFT",
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.ProgressReports.Add(report);
            await seedDb.SaveChangesAsync();
            reportId = report.Id;
        }

        using var app = new FaultyAuditFactory(database);
        app.AuditState.FailOnProgressReportUpdated = true;
        using var leaderClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.Student]);

        var updateReq = new UpdateProgressReportRequest(
            "Mutated Draft Summary",
            "Mutated Work",
            "Mutated Plan",
            "Mutated Risks");

        // 1. Update attempt fails due to audit failure (HTTP 500)
        var failResponse = await leaderClient.PutAsJsonAsync($"/api/v1/progress-reports/{reportId}", updateReq);
        Assert.Equal(HttpStatusCode.InternalServerError, failResponse.StatusCode);

        // 2. Verify with fresh DbContext: Original content is preserved, NO audit row exists
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.ProgressReports.AsNoTracking().SingleAsync(r => r.Id == reportId);
            Assert.Equal("Original Draft Summary", persisted.Summary);
            Assert.Equal("Original Work", persisted.CompletedWork);
            Assert.Equal("Original Plan", persisted.PlannedWork);
            Assert.Equal("Original Risks", persisted.IssuesAndRisks);

            var auditCount = await verifyDb.AuditLogs.AsNoTracking().CountAsync(a => a.EntityId == reportId.ToString() && a.Action == "PROGRESS_REPORT_UPDATED");
            Assert.Equal(0, auditCount);
        }

        // 3. Turn off failure and retry: retry succeeds (200 OK)
        app.AuditState.FailOnProgressReportUpdated = false;
        var successResponse = await leaderClient.PutAsJsonAsync($"/api/v1/progress-reports/{reportId}", updateReq);
        Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);

        // 4. Verify with fresh DbContext: Content is updated, exactly 1 audit row exists
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.ProgressReports.AsNoTracking().SingleAsync(r => r.Id == reportId);
            Assert.Equal("Mutated Draft Summary", persisted.Summary);
            Assert.Equal("Mutated Work", persisted.CompletedWork);

            var auditCount = await verifyDb.AuditLogs.AsNoTracking().CountAsync(a => a.EntityId == reportId.ToString() && a.Action == "PROGRESS_REPORT_UPDATED");
            Assert.Equal(1, auditCount);
        }
    }

    [Fact]
    public async Task AddProgressReportFeedback_WhenAuditFails_RollsBackStatusFeedbackAndCanRetry()
    {
        var ctx = await SeedTestProjectAsync();
        long reportId;

        await using (var seedDb = database.CreateContext())
        {
            var report = new M.ProgressReport
            {
                ProjectId = ctx.ProjectId,
                SubmittedBy = ctx.LeaderUserId,
                ReportType = "WEEKLY",
                PeriodStart = DateOnly.FromDateTime(Now.AddDays(-14)),
                PeriodEnd = DateOnly.FromDateTime(Now.AddDays(-7)),
                Summary = "Submitted Summary",
                CompletedWork = "Completed Work",
                PlannedWork = "Planned Work",
                IssuesAndRisks = "Issues and Risks",
                Status = "SUBMITTED",
                SubmittedAt = Now.AddDays(-7),
                CreatedAt = Now.AddDays(-7),
                UpdatedAt = Now.AddDays(-7)
            };
            seedDb.ProgressReports.Add(report);
            await seedDb.SaveChangesAsync();
            reportId = report.Id;
        }

        using var app = new FaultyAuditFactory(database);
        app.AuditState.FailOnProgressReportFeedbackAdded = true;
        using var supervisorClient = app.CreateAuthenticatedClient(ctx.SupervisorUserId, roles: [AppRoles.Lecturer]);

        var feedbackReq = new AddProgressReportFeedbackRequest("Excellent progress on deliverables.");

        // 1. Feedback attempt fails due to audit failure (HTTP 500)
        var failResponse = await supervisorClient.PostAsJsonAsync($"/api/v1/progress-reports/{reportId}/feedback", feedbackReq);
        Assert.Equal(HttpStatusCode.InternalServerError, failResponse.StatusCode);

        // 2. Verify with fresh DbContext: Status is still SUBMITTED (NOT REVIEWED), 0 feedback rows, 0 audit rows
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.ProgressReports.AsNoTracking().SingleAsync(r => r.Id == reportId);
            Assert.Equal("SUBMITTED", persisted.Status);

            var feedbackCount = await verifyDb.SupervisorFeedbacks.AsNoTracking().CountAsync(f => f.ProgressReportId == reportId);
            Assert.Equal(0, feedbackCount);

            var auditCount = await verifyDb.AuditLogs.AsNoTracking().CountAsync(a => a.EntityId == reportId.ToString() && a.Action == "PROGRESS_REPORT_FEEDBACK_ADDED");
            Assert.Equal(0, auditCount);
        }

        // 3. Turn off failure and retry: retry succeeds (201 Created)
        app.AuditState.FailOnProgressReportFeedbackAdded = false;
        var successResponse = await supervisorClient.PostAsJsonAsync($"/api/v1/progress-reports/{reportId}/feedback", feedbackReq);
        Assert.Equal(HttpStatusCode.Created, successResponse.StatusCode);

        // 4. Verify with fresh DbContext: Status is REVIEWED, exactly 1 feedback row, exactly 1 audit row
        await using (var verifyDb = database.CreateContext())
        {
            var persisted = await verifyDb.ProgressReports.AsNoTracking().SingleAsync(r => r.Id == reportId);
            Assert.Equal("REVIEWED", persisted.Status);

            var feedbackCount = await verifyDb.SupervisorFeedbacks.AsNoTracking().CountAsync(f => f.ProgressReportId == reportId);
            Assert.Equal(1, feedbackCount);

            var auditCount = await verifyDb.AuditLogs.AsNoTracking().CountAsync(a => a.EntityId == reportId.ToString() && a.Action == "PROGRESS_REPORT_FEEDBACK_ADDED");
            Assert.Equal(1, auditCount);
        }
    }

    [Fact]
    public async Task AddMeetingFeedbackVsCancel_CancelWins_StaleFeedbackReturns409_NoFeedbackOrAudit()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;

        await using (var seedDb = database.CreateContext())
        {
            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Meeting To Cancel Before Feedback",
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
            await cancelRepo.CancelAsync(meetingId, Now.AddMinutes(1), cancellationToken: CancellationToken.None);
        }

        // Stale feedback attempt via repository on cancelled meeting MUST return 409 Conflict
        await using (var feedbackDb = database.CreateContext())
        {
            var feedbackRepo = new MeetingRepository(feedbackDb);
            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                feedbackRepo.AddFeedbackAsync(meetingId, ctx.SupervisorAssignmentId, "Feedback on cancelled meeting", Now.AddMinutes(2), cancellationToken: CancellationToken.None));

            Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // Also verify via HTTP client to ensure HTTP layer returns 409 Conflict
        using var app = new FaultyAuditFactory(database);
        using var supervisorClient = app.CreateAuthenticatedClient(ctx.SupervisorUserId, roles: [AppRoles.Lecturer]);

        var httpResponse = await supervisorClient.PostAsJsonAsync(
            $"/api/v1/meetings/{meetingId}/feedback",
            new AddMeetingFeedbackRequest("HTTP Feedback on cancelled meeting"));

        Assert.Equal(HttpStatusCode.Conflict, httpResponse.StatusCode);

        // Verify with fresh DbContext: 0 feedback rows for this meeting, 0 audit rows for MEETING_FEEDBACK_ADDED
        await using (var verifyDb = database.CreateContext())
        {
            var meeting = await verifyDb.Meetings.AsNoTracking().SingleAsync(m => m.Id == meetingId);
            Assert.Equal("CANCELLED", meeting.Status);

            var feedbackCount = await verifyDb.SupervisorFeedbacks.AsNoTracking().CountAsync(f => f.MeetingId == meetingId);
            Assert.Equal(0, feedbackCount);

            var auditCount = await verifyDb.AuditLogs.AsNoTracking().CountAsync(a => a.EntityId == meetingId.ToString() && a.Action == "MEETING_FEEDBACK_ADDED");
            Assert.Equal(0, auditCount);
        }
    }

    #endregion

    #region Concurrency Tests (C1-C5)

    [Fact]
    public async Task C1_ConcurrentOverlappingCreate_OneSucceeds_OneFails409()
    {
        var ctx = await SeedTestProjectAsync();
        using var app = new NormalFactory(database);
        using var client1 = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);
        using var client2 = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);

        var req1 = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), "BLOCK", null);
        var req2 = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 12, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), "BLOCK", null);

        var task1 = client1.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", req1);
        var task2 = client2.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", req2);

        var results = await Task.WhenAll(task1, task2);
        var c1 = results[0].StatusCode;
        var c2 = results[1].StatusCode;

        Assert.True((c1 == HttpStatusCode.Created && c2 == HttpStatusCode.Conflict) || (c1 == HttpStatusCode.Conflict && c2 == HttpStatusCode.Created));
    }

    [Fact]
    public async Task C2_ConcurrentNonOverlappingCreate_BothSucceed()
    {
        var ctx = await SeedTestProjectAsync();
        using var app = new NormalFactory(database);
        using var client1 = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);
        using var client2 = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);

        var req1 = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero), "BLOCK", null);
        var req2 = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 2, 20, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 28, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), "BLOCK", null);

        var task1 = client1.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", req1);
        var task2 = client2.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", req2);

        var results = await Task.WhenAll(task1, task2);
        Assert.Equal(HttpStatusCode.Created, results[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, results[1].StatusCode);
    }

    [Fact]
    public async Task C3_ConcurrentUpdateCreatingOverlap_OneSucceeds_OneFails409()
    {
        var ctx = await SeedTestProjectAsync();
        using var app = new NormalFactory(database);
        using var client = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);

        var req1 = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero), "BLOCK", null);
        var req2 = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 28, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 5, 0, 0, 0, TimeSpan.Zero), "BLOCK", null);

        var res1 = await client.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", req1);
        var res2 = await client.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", req2);

        var cycle1 = await res1.Content.ReadFromJsonAsync<ReportingCycleDto>();
        var cycle2 = await res2.Content.ReadFromJsonAsync<ReportingCycleDto>();

        var upReq1 = new UpdateReportingCycleRequest(PeriodEnd: new DateTimeOffset(2026, 3, 18, 0, 0, 0, TimeSpan.Zero));
        var upReq2 = new UpdateReportingCycleRequest(PeriodStart: new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero));

        using var client1 = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);
        using var client2 = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);

        var task1 = client1.PutAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles/{cycle1!.Id}", upReq1);
        var task2 = client2.PutAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles/{cycle2!.Id}", upReq2);

        var results = await Task.WhenAll(task1, task2);
        var c1 = results[0].StatusCode;
        var c2 = results[1].StatusCode;

        Assert.True((c1 == HttpStatusCode.OK && c2 == HttpStatusCode.Conflict) ||
                    (c1 == HttpStatusCode.Conflict && c2 == HttpStatusCode.OK));

        using var freshCtx = database.CreateContext();
        var reloaded1 = await freshCtx.ProgressReportPeriods.FindAsync(cycle1.Id);
        var reloaded2 = await freshCtx.ProgressReportPeriods.FindAsync(cycle2.Id);

        Assert.False(reloaded1!.PeriodStart < reloaded2!.PeriodEnd && reloaded2.PeriodStart < reloaded1.PeriodEnd);
    }

    [Fact]
    public async Task C4_WeeklyVsMonthly_BothSucceed()
    {
        var ctx = await SeedTestProjectAsync();
        using var app = new NormalFactory(database);
        using var client1 = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);
        using var client2 = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);

        var req1 = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 30, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 5, 0, 0, 0, TimeSpan.Zero), "BLOCK", null);
        var req2 = new CreateReportingCycleRequest("MONTHLY",
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 30, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 5, 0, 0, 0, TimeSpan.Zero), "BLOCK", null);

        var task1 = client1.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", req1);
        var task2 = client2.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", req2);

        var results = await Task.WhenAll(task1, task2);
        Assert.Equal(HttpStatusCode.Created, results[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, results[1].StatusCode);
    }

    [Fact]
    public async Task C5_AdjacentHalfOpenBoundaries_BothSucceed()
    {
        var ctx = await SeedTestProjectAsync();
        using var app = new NormalFactory(database);
        using var client1 = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);
        using var client2 = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);

        var req1 = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero), "BLOCK", null);
        var req2 = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 5, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero), "BLOCK", null);

        var task1 = client1.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", req1);
        var task2 = client2.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", req2);

        var results = await Task.WhenAll(task1, task2);
        Assert.Equal(HttpStatusCode.Created, results[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, results[1].StatusCode);
    }

    #endregion

    #region R1-R3 Tests

        [Fact]
    public async Task DebugRoleDbCheck()
    {
        var ctx = await SeedTestProjectAsync();
        await using var db = database.CreateContext();
        var isStaff = await db.UserRoles.AnyAsync(ur => ur.UserId == ctx.LeaderUserId && ur.Role.Code == AIPMS.Application.Common.Security.AppRoles.DepartmentStaff);
        Assert.True(isStaff, $"User {ctx.LeaderUserId} does not have DepartmentStaff role in DB.");
    }

    [Fact]
    public async Task R1_ReferencedCycle_IdentityFieldsAreImmutable()
    {
        var ctx = await SeedTestProjectAsync();
        using var app = new NormalFactory(database);
        using var staffClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);
        using var studentClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.Student]);

        var reqCycle = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), "BLOCK", null);

        var resCycle = await staffClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", reqCycle);
        var resCycleStr = await resCycle.Content.ReadAsStringAsync();
        Assert.True(resCycle.StatusCode == HttpStatusCode.Created, $"Cycle Create Failed: {resCycle.StatusCode} - {resCycleStr}");
        var cycle = await resCycle.Content.ReadFromJsonAsync<ReportingCycleDto>();

        var reqReport = new CreateProgressReportRequest(
            ReportType: "WEEKLY",
            PeriodStart: DateOnly.FromDateTime(new DateTime(2026, 6, 1)),
            PeriodEnd: DateOnly.FromDateTime(new DateTime(2026, 6, 10)),
            Summary: "Summary",
            CompletedWork: "Completed", PlannedWork: null, IssuesAndRisks: null,


            ProgressReportPeriodId: cycle!.Id,
            InProgressWork: "In progress",
            Blockers: "Blockers", Risks: "Risks",
            NextActions: "Next actions" );
        var resReport = await studentClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/progress-reports", reqReport);
        var responseString = await resReport.Content.ReadAsStringAsync();
        Assert.True(resReport.StatusCode == HttpStatusCode.Created, $"Report Create Failed: {resReport.StatusCode} - {responseString}");

        var upReq1 = new UpdateReportingCycleRequest(PeriodStart: new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        var res1 = await staffClient.PutAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles/{cycle.Id}", upReq1);

        var upReq2 = new UpdateReportingCycleRequest(PeriodEnd: new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        var res2 = await staffClient.PutAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles/{cycle.Id}", upReq2);

        Assert.Equal(HttpStatusCode.Conflict, res1.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);

        using var freshCtx = database.CreateContext();
        var reloaded = await freshCtx.ProgressReportPeriods.FindAsync(cycle.Id);
        Assert.Equal("2026-06-01T00:00:00", reloaded!.PeriodStart.ToString("yyyy-MM-ddTHH:mm:ss"));
        Assert.Equal("2026-06-10T00:00:00", reloaded!.PeriodEnd.ToString("yyyy-MM-ddTHH:mm:ss"));
    }

    [Fact]
    public async Task R2_SubmittedReport_CycleDeadlineAndLatePolicyAreImmutable()
    {
        var ctx = await SeedTestProjectAsync();
        using var app = new NormalFactory(database);
        using var staffClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);
        using var studentClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.Student]);

        var futureDeadline = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 12, 0, 0, TimeSpan.Zero).AddDays(30);

        var reqCycle = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
            futureDeadline, "BLOCK", null);

        var resCycle = await staffClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", reqCycle);
        var resCycleStr = await resCycle.Content.ReadAsStringAsync();
        Assert.True(resCycle.StatusCode == HttpStatusCode.Created, $"Cycle Create Failed: {resCycle.StatusCode} - {resCycleStr}");
        var cycle = await resCycle.Content.ReadFromJsonAsync<ReportingCycleDto>();

        var reqReport = new CreateProgressReportRequest(
            ReportType: "WEEKLY",
            PeriodStart: DateOnly.FromDateTime(new DateTime(2026, 7, 1)),
            PeriodEnd: DateOnly.FromDateTime(new DateTime(2026, 7, 10)),
            Summary: "Summary",
            CompletedWork: "Completed", PlannedWork: null, IssuesAndRisks: null,


            ProgressReportPeriodId: cycle!.Id,
            InProgressWork: "In progress",
            Blockers: "Blockers", Risks: "Risks",
            NextActions: "Next actions" );
        var resReport = await studentClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/progress-reports", reqReport);
        var resReportStr = await resReport.Content.ReadAsStringAsync();
        Assert.True(resReport.StatusCode == HttpStatusCode.Created, $"Report Create Failed: {resReport.StatusCode} - {resReportStr}");
        var report = await resReport.Content.ReadFromJsonAsync<ProgressReportDto>();

        var submitRes = await studentClient.PostAsync($"/api/v1/progress-reports/{report!.Id}/submit", null);
        Assert.True(submitRes.IsSuccessStatusCode, $"Submit failed: {await submitRes.Content.ReadAsStringAsync()}");

        var upReq = new UpdateReportingCycleRequest(
            Deadline: DateTimeOffset.UtcNow.AddDays(40),
            LatePolicy: "FLAG");

        var res1 = await staffClient.PutAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles/{cycle.Id}", upReq);
        Assert.Equal(HttpStatusCode.Conflict, res1.StatusCode);

        using var freshCtx = database.CreateContext();
        var reloaded = await freshCtx.ProgressReportPeriods.FindAsync(cycle.Id);
        Assert.Equal(futureDeadline.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), reloaded!.Deadline.ToString("yyyy-MM-ddTHH:mm:ss"));
        Assert.Equal("BLOCK", reloaded.LatePolicy);
    }

    [Fact]
    public async Task R3_CycleDeadlineUpdateVsSubmit_FinalStateIsConsistent()
    {
        var ctx = await SeedTestProjectAsync();

        using var app = new NormalFactory(database);

        using var staffClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);
        using var studentClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.Student]);

        var futureDeadline = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 12, 0, 0, TimeSpan.Zero).AddDays(30);

        var reqCycle = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero),
            futureDeadline, "FLAG", null);

        var resCycle = await staffClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", reqCycle);
        var resCycleStr = await resCycle.Content.ReadAsStringAsync();
        Assert.True(resCycle.StatusCode == HttpStatusCode.Created, $"Cycle Create Failed: {resCycle.StatusCode} - {resCycleStr}");
        var cycle = await resCycle.Content.ReadFromJsonAsync<ReportingCycleDto>();

        var reqReport = new CreateProgressReportRequest(
            ReportType: "WEEKLY",
            PeriodStart: DateOnly.FromDateTime(new DateTime(2026, 8, 1)),
            PeriodEnd: DateOnly.FromDateTime(new DateTime(2026, 8, 10)),
            Summary: "Summary",
            CompletedWork: "Completed", PlannedWork: null, IssuesAndRisks: null,


            ProgressReportPeriodId: cycle!.Id,
            InProgressWork: "In progress",
            Blockers: "Blockers", Risks: "Risks",
            NextActions: "Next actions" );
        var resReport = await studentClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/progress-reports", reqReport);
        var resReportStr = await resReport.Content.ReadAsStringAsync();
        Assert.True(resReport.StatusCode == HttpStatusCode.Created, $"Report Create Failed: {resReport.StatusCode} - {resReportStr}");
        var report = await resReport.Content.ReadFromJsonAsync<ProgressReportDto>();

        var pastDeadline = new DateTimeOffset(2000, 8, 11, 0, 0, 0, TimeSpan.Zero);
        var upReq = new UpdateReportingCycleRequest(Deadline: pastDeadline);

        var task1 = staffClient.PutAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles/{cycle.Id}", upReq);
        var task2 = studentClient.PostAsync($"/api/v1/progress-reports/{report!.Id}/submit", null);

        var results = await Task.WhenAll(task1, task2);

        var c1 = results[0].StatusCode;
        var c2 = results[1].StatusCode;

        using var freshCtx = database.CreateContext();
        var reloadedCycle = await freshCtx.ProgressReportPeriods.FindAsync(cycle.Id);
        var reloadedReport = await freshCtx.ProgressReports.FindAsync(report.Id);

        if (c1 == HttpStatusCode.OK && c2 == HttpStatusCode.OK)
        {
            Assert.Equal(pastDeadline.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), reloadedCycle!.Deadline.ToString("yyyy-MM-ddTHH:mm:ss"));
            Assert.Equal("SUBMITTED", reloadedReport!.Status);
            Assert.True(reloadedReport.IsLate);
        }
        else if (c1 == HttpStatusCode.Conflict && c2 == HttpStatusCode.OK)
        {
            Assert.Equal(futureDeadline.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), reloadedCycle!.Deadline.ToString("yyyy-MM-ddTHH:mm:ss"));
            Assert.Equal("SUBMITTED", reloadedReport!.Status);
            Assert.False(reloadedReport.IsLate);
        }
        else
        {
            Assert.Fail($"Invalid concurrent execution states: Update {c1} / Submit {c2}");
        }
    }

    #endregion

    #region PR #89 Review Regression Coverage

    [Fact]
    public async Task Regression_AuditRollback_ReportingCycleMutation()
    {
        var ctx = await SeedTestProjectAsync();
        using var app = new FaultyAuditFactory(database);
        app.AuditState.FailOnReportingCycleCreated = true;
        using var staffClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);

        var reqCycle = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero), "BLOCK", null);

        var response = await staffClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", reqCycle);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        await using var verifyDb = database.CreateContext();
        var cycleCount = await verifyDb.ProgressReportPeriods.AsNoTracking()
            .CountAsync(c => c.ProjectId == ctx.ProjectId && c.ReportType == "WEEKLY" && c.PeriodStart == new DateTime(2026, 9, 1));
        Assert.Equal(0, cycleCount);

        var auditCount = await verifyDb.AuditLogs.AsNoTracking().CountAsync(a => a.Action == "REPORTING_CYCLE_CREATED" && a.ActorUserId == ctx.LeaderUserId);
        Assert.Equal(0, auditCount);
    }

    [Fact]
    public async Task Regression_AuditRollback_ActionItemMutation()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;
        await using (var seedDb = database.CreateContext())
        {
            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Sync Meeting For Action Item",
                StartAt = Now.AddDays(1),
                Status = "SCHEDULED",
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.Meetings.Add(meeting);
            await seedDb.SaveChangesAsync();
            meetingId = meeting.Id;
        }

        using var app = new FaultyAuditFactory(database);
        app.AuditState.FailOnProjectActionItemCreated = true;
        using var leaderClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.Student]);

        var req = new CreateProjectActionItemRequest("MEETING", "Follow up on action", MeetingId: meetingId, Description: "Action details", OwnerId: ctx.LeaderUserId);
        var response = await leaderClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/action-items", req);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        await using var verifyDb = database.CreateContext();
        var itemCount = await verifyDb.ProjectActionItems.AsNoTracking()
            .CountAsync(a => a.ProjectId == ctx.ProjectId && a.MeetingId == meetingId);
        Assert.Equal(0, itemCount);

        var auditCount = await verifyDb.AuditLogs.AsNoTracking().CountAsync(a => a.Action == "PROJECT_ACTION_ITEM_CREATED" && a.ActorUserId == ctx.LeaderUserId);
        Assert.Equal(0, auditCount);
    }

    [Fact]
    public async Task Regression_ReportingCycleAuth_PersistedStateRequired()
    {
        var ctx = await SeedTestProjectAsync();
        using var app = new NormalFactory(database);

        // A user with Student role in DB claiming to be Admin in JWT token -> Must be 403 Forbidden!
        using var fakeAdminClient = app.CreateAuthenticatedClient(ctx.MemberUserId, roles: [AppRoles.Admin]);

        var reqCycle = new CreateReportingCycleRequest("WEEKLY",
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero), "BLOCK", null);

        var response = await fakeAdminClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", reqCycle);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Regression_ProjectActionItem_CancelledMeeting_MutationBlocked()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;
        await using (var seedDb = database.CreateContext())
        {
            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Meeting To Be Cancelled",
                StartAt = Now.AddDays(1),
                Status = "CANCELLED",
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.Meetings.Add(meeting);
            await seedDb.SaveChangesAsync();
            meetingId = meeting.Id;
        }

        using var app = new NormalFactory(database);
        using var leaderClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.Student]);

        var createReq = new CreateProjectActionItemRequest("MEETING", "Action on cancelled meeting", MeetingId: meetingId, OwnerId: ctx.LeaderUserId);
        var res = await leaderClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/action-items", createReq);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Regression_ProgressReport_CycleMismatch_Rejected()
    {
        var ctx = await SeedTestProjectAsync();
        using var app = new NormalFactory(database);
        using var staffClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.DepartmentStaff]);
        using var studentClient = app.CreateAuthenticatedClient(ctx.LeaderUserId, roles: [AppRoles.Student]);

        var reqCycle = new CreateReportingCycleRequest("MONTHLY",
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 5, 0, 0, 0, TimeSpan.Zero), "BLOCK", null);

        var resCycle = await staffClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/reporting-cycles", reqCycle);
        Assert.Equal(HttpStatusCode.Created, resCycle.StatusCode);
        var cycle = await resCycle.Content.ReadFromJsonAsync<ReportingCycleDto>();

        // Try creating a WEEKLY report attaching to this MONTHLY cycle -> 400 Bad Request!
        var reqReport = new CreateProgressReportRequest(
            ReportType: "WEEKLY",
            PeriodStart: new DateOnly(2026, 12, 1),
            PeriodEnd: new DateOnly(2026, 12, 31),
            Summary: "Summary",
            CompletedWork: "Done",
            PlannedWork: "Next",
            IssuesAndRisks: "None",
            ProgressReportPeriodId: cycle!.Id);

        var resReport = await studentClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/progress-reports", reqReport);
        Assert.Equal(HttpStatusCode.BadRequest, resReport.StatusCode);
    }

    [Fact]
    public async Task StaleAdminJwt_CreateActionItem_Returns403Forbidden()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;
        long adminUserId;

        await using (var seedDb = database.CreateContext())
        {
            adminUserId = await seedDb.UserRoles
                .Where(ur => ur.Role.Code == AppRoles.Admin)
                .Select(ur => ur.UserId)
                .FirstAsync();

            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Leader Only Meeting",
                StartAt = Now.AddDays(1),
                Status = "SCHEDULED",
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.Meetings.Add(meeting);
            await seedDb.SaveChangesAsync();
            meetingId = meeting.Id;
        }

        using var app = new NormalFactory(database);

        // 1. Stale token: Non-privileged member claiming Admin in JWT token -> Must be 403 Forbidden!
        using var fakeAdminClient = app.CreateAuthenticatedClient(ctx.MemberUserId, roles: [AppRoles.Admin]);
        var req = new CreateProjectActionItemRequest("MEETING", "New Action Item", MeetingId: meetingId, OwnerId: ctx.LeaderUserId);
        var fakeRes = await fakeAdminClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/action-items", req);
        Assert.Equal(HttpStatusCode.Forbidden, fakeRes.StatusCode);

        // 2. Real persisted Admin: user with Admin in DB -> 201 Created!
        using var realAdminClient = app.CreateAuthenticatedClient(adminUserId, roles: [AppRoles.Admin]);
        var realRes = await realAdminClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/action-items", req);
        Assert.Equal(HttpStatusCode.Created, realRes.StatusCode);
    }

    [Fact]
    public async Task StaleAdminJwt_UpdateActionItemDetails_Returns403Forbidden()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;
        long itemId;
        long adminUserId;

        await using (var seedDb = database.CreateContext())
        {
            adminUserId = await seedDb.UserRoles
                .Where(ur => ur.Role.Code == AppRoles.Admin)
                .Select(ur => ur.UserId)
                .FirstAsync();

            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Meeting for Update Details",
                StartAt = Now.AddDays(1),
                Status = "SCHEDULED",
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.Meetings.Add(meeting);
            await seedDb.SaveChangesAsync();
            meetingId = meeting.Id;

            var item = new AIPMS.Infrastructure.Persistence.Models.ProjectActionItem
            {
                ProjectId = ctx.ProjectId,
                SourceType = "MEETING",
                MeetingId = meetingId,
                Title = "Original Action Item Title",
                Status = "TODO",
                CreatedBy = ctx.LeaderUserId,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.ProjectActionItems.Add(item);
            await seedDb.SaveChangesAsync();
            itemId = item.Id;
        }

        using var app = new NormalFactory(database);

        // 1. Stale token: Non-privileged member claiming Admin in JWT token -> Must be 403 Forbidden!
        using var fakeAdminClient = app.CreateAuthenticatedClient(ctx.MemberUserId, roles: [AppRoles.Admin]);
        var updateReq = new UpdateProjectActionItemRequest(Title: "Title Updated by Fake Admin");
        var fakeRes = await fakeAdminClient.PutAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/action-items/{itemId}", updateReq);
        Assert.Equal(HttpStatusCode.Forbidden, fakeRes.StatusCode);

        // 2. Real persisted Admin: user with Admin in DB -> 200 OK!
        using var realAdminClient = app.CreateAuthenticatedClient(adminUserId, roles: [AppRoles.Admin]);
        var realRes = await realAdminClient.PutAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/action-items/{itemId}", updateReq);
        Assert.Equal(HttpStatusCode.OK, realRes.StatusCode);

        // 3. Confirm in DB title is updated
        await using (var verifyDb = database.CreateContext())
        {
            var updated = await verifyDb.ProjectActionItems.FindAsync(itemId);
            Assert.Equal("Title Updated by Fake Admin", updated!.Title);
        }
    }

    [Fact]
    public async Task StaleAdminJwt_ReopenTerminalActionItem_Returns403Forbidden()
    {
        var ctx = await SeedTestProjectAsync();
        long itemId;
        long adminUserId;

        await using (var seedDb = database.CreateContext())
        {
            adminUserId = await seedDb.UserRoles
                .Where(ur => ur.Role.Code == AppRoles.Admin)
                .Select(ur => ur.UserId)
                .FirstAsync();

            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Completed Action Item Meeting",
                StartAt = Now.AddDays(1),
                Status = "SCHEDULED",
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.Meetings.Add(meeting);
            await seedDb.SaveChangesAsync();

            var item = new AIPMS.Infrastructure.Persistence.Models.ProjectActionItem
            {
                ProjectId = ctx.ProjectId,
                SourceType = "MEETING",
                MeetingId = meeting.Id,
                Title = "Completed Action Item",
                Status = "DONE",
                CreatedBy = ctx.LeaderUserId,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.ProjectActionItems.Add(item);
            await seedDb.SaveChangesAsync();
            itemId = item.Id;
        }

        using var app = new NormalFactory(database);

        // 1. Stale token: Student user claiming Admin in JWT token -> Must be 403 Forbidden!
        using var fakeAdminClient = app.CreateAuthenticatedClient(ctx.MemberUserId, roles: [AppRoles.Admin]);
        var reopenReq = new UpdateProjectActionItemStatusRequest("IN_PROGRESS");
        var fakeRes = await fakeAdminClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/action-items/{itemId}/status", reopenReq);
        Assert.Equal(HttpStatusCode.Forbidden, fakeRes.StatusCode);

        // 2. Real persisted Admin: user with Admin in DB -> 200 OK!
        using var realAdminClient = app.CreateAuthenticatedClient(adminUserId, roles: [AppRoles.Admin]);
        var realRes = await realAdminClient.PostAsJsonAsync($"/api/v1/projects/{ctx.ProjectId}/action-items/{itemId}/status", reopenReq);
        Assert.Equal(HttpStatusCode.OK, realRes.StatusCode);

        // 3. Confirm in DB status is now IN_PROGRESS
        await using (var verifyDb = database.CreateContext())
        {
            var updated = await verifyDb.ProjectActionItems.FindAsync(itemId);
            Assert.Equal("IN_PROGRESS", updated!.Status);
        }
    }

    [Fact]
    public async Task CreateReport_vs_CycleDateUpdate_DeterministicRace()
    {
        var ctx = await SeedTestProjectAsync();
        long cycleId;

        await using (var seedDb = database.CreateContext())
        {
            var projectPeriod = await seedDb.ProjectPeriods.FirstAsync();
            var cycle = new AIPMS.Infrastructure.Persistence.Models.ProgressReportPeriod
            {
                ProjectId = ctx.ProjectId,
                ProjectPeriodId = projectPeriod.Id,
                ReportType = "WEEKLY",
                PeriodStart = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc),
                Deadline = DateTime.UtcNow.AddDays(30),
                LatePolicy = "BLOCK",
                ConcurrencyToken = Guid.NewGuid(),
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.ProgressReportPeriods.Add(cycle);
            await seedDb.SaveChangesAsync();
            cycleId = cycle.Id;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Exception? reportCreateEx = null;
        Exception? cycleUpdateEx = null;

        var task1 = Task.Run(async () =>
        {
            await tcs.Task;
            try
            {
                await using var db1 = database.CreateContext();
                var repo1 = new ProgressReportRepository(db1);
                await repo1.CreateAsync(
                    ctx.ProjectId,
                    ctx.LeaderUserId,
                    "WEEKLY",
                    new DateOnly(2026, 11, 1),
                    new DateOnly(2026, 11, 8),
                    "Summary",
                    "Completed",
                    "Planned",
                    "Risks",
                    Now,
                    progressReportPeriodId: cycleId,
                    inProgressWork: "Ongoing",
                    blockers: "None",
                    risks: "None",
                    nextActions: "Next",
                    onCreated: null);
            }
            catch (Exception ex)
            {
                reportCreateEx = ex;
            }
        });

        var task2 = Task.Run(async () =>
        {
            await tcs.Task;
            try
            {
                await using var db2 = database.CreateContext();
                var repo2 = new ReportingCycleRepository(db2);
                await repo2.UpdateAsync(
                    cycleId,
                    periodStart: new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc),
                    periodEnd: new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc),
                    deadline: null,
                    latePolicy: null,
                    expectedToken: null,
                    now: Now);
            }
            catch (Exception ex)
            {
                cycleUpdateEx = ex;
            }
        });

        tcs.SetResult();
        await Task.WhenAll(task1, task2);

        var oneSucceeded = (reportCreateEx is null && cycleUpdateEx is not null)
                        || (reportCreateEx is not null && cycleUpdateEx is null);
        Assert.True(oneSucceeded, $"Expected exactly one to succeed. CreateEx: {reportCreateEx?.Message}, UpdateEx: {cycleUpdateEx?.Message}");

        if (reportCreateEx is not null)
        {
            Assert.True(reportCreateEx is ValidationException || reportCreateEx is ConflictException);
        }
        else
        {
            Assert.IsType<ConflictException>(cycleUpdateEx);
        }

        await using (var verifyDb = database.CreateContext())
        {
            var finalCycle = await verifyDb.ProgressReportPeriods.FindAsync(cycleId);
            var report = await verifyDb.ProgressReports.FirstOrDefaultAsync(r => r.ProgressReportPeriodId == cycleId);
            if (report is not null)
            {
                Assert.Equal(DateOnly.FromDateTime(finalCycle!.PeriodStart), report.PeriodStart);
                Assert.Equal(DateOnly.FromDateTime(finalCycle.PeriodEnd), report.PeriodEnd);
            }
        }
    }

    [Fact]
    public async Task UpdateReportCycleLink_vs_CycleDateUpdate_DeterministicRace()
    {
        var ctx = await SeedTestProjectAsync();
        long cycleId;
        long reportId;

        await using (var seedDb = database.CreateContext())
        {
            var projectPeriod = await seedDb.ProjectPeriods.FirstAsync();
            var cycle = new AIPMS.Infrastructure.Persistence.Models.ProgressReportPeriod
            {
                ProjectId = ctx.ProjectId,
                ProjectPeriodId = projectPeriod.Id,
                ReportType = "WEEKLY",
                PeriodStart = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc),
                Deadline = DateTime.UtcNow.AddDays(30),
                LatePolicy = "BLOCK",
                ConcurrencyToken = Guid.NewGuid(),
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.ProgressReportPeriods.Add(cycle);
            await seedDb.SaveChangesAsync();
            cycleId = cycle.Id;

            var report = new M.ProgressReport
            {
                ProjectId = ctx.ProjectId,
                SubmittedBy = ctx.LeaderUserId,
                ReportType = "WEEKLY",
                PeriodStart = new DateOnly(2026, 11, 1),
                PeriodEnd = new DateOnly(2026, 11, 8),
                Summary = "Unlinked Draft",
                Status = "DRAFT",
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.ProgressReports.Add(report);
            await seedDb.SaveChangesAsync();
            reportId = report.Id;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Exception? reportUpdateEx = null;
        Exception? cycleUpdateEx = null;

        var task1 = Task.Run(async () =>
        {
            await tcs.Task;
            try
            {
                await using var db1 = database.CreateContext();
                var repo1 = new ProgressReportRepository(db1);
                await repo1.UpdateAsync(
                    id: reportId,
                    summary: "Updated Summary",
                    completedWork: "Completed",
                    plannedWork: "Planned",
                    issuesAndRisks: "Risks",
                    now: Now,
                    progressReportPeriodId: cycleId,
                    inProgressWork: "Ongoing",
                    blockers: "None",
                    risks: "None",
                    nextActions: "Next",
                    onUpdated: null,
                    cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                reportUpdateEx = ex;
            }
        });

        var task2 = Task.Run(async () =>
        {
            await tcs.Task;
            try
            {
                await using var db2 = database.CreateContext();
                var repo2 = new ReportingCycleRepository(db2);
                await repo2.UpdateAsync(
                    cycleId,
                    periodStart: new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc),
                    periodEnd: new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc),
                    deadline: null,
                    latePolicy: null,
                    expectedToken: null,
                    now: Now);
            }
            catch (Exception ex)
            {
                cycleUpdateEx = ex;
            }
        });

        tcs.SetResult();
        await Task.WhenAll(task1, task2);

        var oneSucceeded = (reportUpdateEx is null && cycleUpdateEx is not null)
                        || (reportUpdateEx is not null && cycleUpdateEx is null);
        Assert.True(oneSucceeded, $"Expected exactly one to succeed. ReportUpdateEx: {reportUpdateEx?.Message}, CycleUpdateEx: {cycleUpdateEx?.Message}");

        if (reportUpdateEx is not null)
        {
            Assert.True(reportUpdateEx is ValidationException || reportUpdateEx is ConflictException);
        }
        else
        {
            Assert.IsType<ConflictException>(cycleUpdateEx);
        }

        await using (var verifyDb = database.CreateContext())
        {
            var finalCycle = await verifyDb.ProgressReportPeriods.FindAsync(cycleId);
            var finalReport = await verifyDb.ProgressReports.FindAsync(reportId);
            if (finalReport!.ProgressReportPeriodId.HasValue)
            {
                Assert.Equal(DateOnly.FromDateTime(finalCycle!.PeriodStart), finalReport.PeriodStart);
                Assert.Equal(DateOnly.FromDateTime(finalCycle.PeriodEnd), finalReport.PeriodEnd);
            }
        }
    }

    [Fact]
    public async Task CreateActionItem_vs_CancelMeeting_DeterministicRace()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;

        await using (var seedDb = database.CreateContext())
        {
            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Meeting For Race",
                StartAt = Now.AddDays(1),
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

        Exception? createActionItemEx = null;
        Exception? cancelMeetingEx = null;

        var task1 = Task.Run(async () =>
        {
            await tcs.Task;
            try
            {
                await using var db1 = database.CreateContext();
                var repo1 = new ProjectActionItemRepository(db1);
                await repo1.CreateAsync(
                    ctx.ProjectId,
                    "MEETING",
                    meetingId,
                    null,
                    "Action Item from Meeting",
                    null,
                    ctx.LeaderUserId,
                    null,
                    null,
                    null,
                    ctx.LeaderUserId,
                    Now);
            }
            catch (Exception ex)
            {
                createActionItemEx = ex;
            }
        });

        var task2 = Task.Run(async () =>
        {
            await tcs.Task;
            try
            {
                await using var db2 = database.CreateContext();
                var repo2 = new MeetingRepository(db2);
                await repo2.CancelAsync(meetingId, Now);
            }
            catch (Exception ex)
            {
                cancelMeetingEx = ex;
            }
        });

        tcs.SetResult();
        await Task.WhenAll(task1, task2);

        Assert.Null(cancelMeetingEx);

        await using (var verifyDb = database.CreateContext())
        {
            var finalMeeting = await verifyDb.Meetings.FindAsync(meetingId);
            Assert.Equal("CANCELLED", finalMeeting!.Status);

            var itemsCount = await verifyDb.ProjectActionItems.CountAsync(a => a.MeetingId == meetingId);

            if (createActionItemEx is not null)
            {
                Assert.IsType<ConflictException>(createActionItemEx);
                Assert.Equal(0, itemsCount);
            }
            else
            {
                Assert.Equal(1, itemsCount);
            }
        }
    }

    [Fact]
    public async Task CancelMeeting_vs_UpdateActionItemDetails()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;
        long actionItemId;

        await using (var seedDb = database.CreateContext())
        {
            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Meeting For Details Race",
                StartAt = Now.AddDays(1),
                Status = "SCHEDULED",
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.Meetings.Add(meeting);
            await seedDb.SaveChangesAsync();
            meetingId = meeting.Id;

            var item = new AIPMS.Infrastructure.Persistence.Models.ProjectActionItem
            {
                ProjectId = ctx.ProjectId,
                SourceType = "MEETING",
                MeetingId = meetingId,
                Title = "Original Title",
                Status = "TODO",
                CreatedBy = ctx.LeaderUserId,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.ProjectActionItems.Add(item);
            await seedDb.SaveChangesAsync();
            actionItemId = item.Id;
        }

        var cancelInsideTx = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Exception? cancelEx = null;
        Exception? updateEx = null;

        var task1 = Task.Run(async () =>
        {
            try
            {
                await using var db1 = database.CreateContext();
                var repo1 = new MeetingRepository(db1);
                await repo1.CancelAsync(
                    meetingId,
                    Now,
                    onCancelled: async _ =>
                    {
                        cancelInsideTx.SetResult();
                        await updateStarted.Task;
                    });
            }
            catch (Exception ex)
            {
                cancelEx = ex;
            }
        });

        var task2 = Task.Run(async () =>
        {
            try
            {
                await cancelInsideTx.Task;
                await using var db2 = database.CreateContext();
                var repo2 = new ProjectActionItemRepository(db2);
                updateStarted.SetResult();

                await repo2.UpdateDetailsAsync(
                    id: actionItemId,
                    title: "Stale Updated Title",
                    description: "Stale description",
                    ownerId: null,
                    taskId: null,
                    milestoneId: null,
                    dueAt: null,
                    expectedToken: null,
                    now: Now,
                    onUpdated: async _ =>
                    {
                        db2.AuditLogs.Add(new M.AuditLog
                        {
                            ActorUserId = ctx.LeaderUserId,
                            Action = "PROJECT_ACTION_ITEM_UPDATED",
                            EntityType = "PROJECT_ACTION_ITEM",
                            EntityId = actionItemId.ToString(),
                            Outcome = "SUCCESS",
                            OccurredAt = Now
                        });
                        await db2.SaveChangesAsync();
                    });
            }
            catch (Exception ex)
            {
                updateEx = ex;
            }
        });

        await Task.WhenAll(task1, task2);

        Assert.Null(cancelEx);
        Assert.NotNull(updateEx);
        Assert.IsType<ConflictException>(updateEx);

        await using (var verifyDb = database.CreateContext())
        {
            var finalMeeting = await verifyDb.Meetings.FindAsync(meetingId);
            Assert.Equal("CANCELLED", finalMeeting!.Status);

            var finalItem = await verifyDb.ProjectActionItems.FindAsync(actionItemId);
            Assert.Equal("Original Title", finalItem!.Title);
            Assert.Null(finalItem.Description);

            var hasAudit = await verifyDb.AuditLogs
                .AnyAsync(a => a.EntityId == actionItemId.ToString() && a.Action == "PROJECT_ACTION_ITEM_UPDATED");
            Assert.False(hasAudit);
        }
    }

    [Fact]
    public async Task CancelMeeting_vs_UpdateActionItemStatus()
    {
        var ctx = await SeedTestProjectAsync();
        long meetingId;
        long actionItemId;

        await using (var seedDb = database.CreateContext())
        {
            var meeting = new M.Meeting
            {
                ProjectId = ctx.ProjectId,
                Title = "Meeting For Status Race",
                StartAt = Now.AddDays(1),
                Status = "SCHEDULED",
                CreatedBy = ctx.LeaderUserId,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.Meetings.Add(meeting);
            await seedDb.SaveChangesAsync();
            meetingId = meeting.Id;

            var item = new AIPMS.Infrastructure.Persistence.Models.ProjectActionItem
            {
                ProjectId = ctx.ProjectId,
                SourceType = "MEETING",
                MeetingId = meetingId,
                Title = "Meeting Action Item",
                Status = "TODO",
                CreatedBy = ctx.LeaderUserId,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = Now,
                UpdatedAt = Now
            };
            seedDb.ProjectActionItems.Add(item);
            await seedDb.SaveChangesAsync();
            actionItemId = item.Id;
        }

        var cancelInsideTx = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Exception? cancelEx = null;
        Exception? updateEx = null;

        var task1 = Task.Run(async () =>
        {
            try
            {
                await using var db1 = database.CreateContext();
                var repo1 = new MeetingRepository(db1);
                await repo1.CancelAsync(
                    meetingId,
                    Now,
                    onCancelled: async _ =>
                    {
                        cancelInsideTx.SetResult();
                        await updateStarted.Task;
                    });
            }
            catch (Exception ex)
            {
                cancelEx = ex;
            }
        });

        var task2 = Task.Run(async () =>
        {
            try
            {
                await cancelInsideTx.Task;
                await using var db2 = database.CreateContext();
                var repo2 = new ProjectActionItemRepository(db2);
                updateStarted.SetResult();

                await repo2.UpdateStatusAsync(
                    id: actionItemId,
                    newStatus: "IN_PROGRESS",
                    expectedToken: null,
                    now: Now,
                    onUpdated: async _ =>
                    {
                        db2.AuditLogs.Add(new M.AuditLog
                        {
                            ActorUserId = ctx.LeaderUserId,
                            Action = "PROJECT_ACTION_ITEM_STATUS_UPDATED",
                            EntityType = "PROJECT_ACTION_ITEM",
                            EntityId = actionItemId.ToString(),
                            Outcome = "SUCCESS",
                            OccurredAt = Now
                        });
                        await db2.SaveChangesAsync();
                    });
            }
            catch (Exception ex)
            {
                updateEx = ex;
            }
        });

        await Task.WhenAll(task1, task2);

        Assert.Null(cancelEx);
        Assert.NotNull(updateEx);
        Assert.IsType<ConflictException>(updateEx);

        await using (var verifyDb = database.CreateContext())
        {
            var finalMeeting = await verifyDb.Meetings.FindAsync(meetingId);
            Assert.Equal("CANCELLED", finalMeeting!.Status);

            var finalItem = await verifyDb.ProjectActionItems.FindAsync(actionItemId);
            Assert.Equal("TODO", finalItem!.Status);

            var hasAudit = await verifyDb.AuditLogs
                .AnyAsync(a => a.EntityId == actionItemId.ToString() && a.Action == "PROJECT_ACTION_ITEM_STATUS_UPDATED");
            Assert.False(hasAudit);
        }
    }

    #endregion
}
