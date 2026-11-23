using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class MeetingVideoServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 7, 38, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static AipmsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AipmsDbContext SeedMeeting(DateTime startAt, DateTime? endAt, M.MeetingVideoSession? session = null)
    {
        var db = CreateDb();
        db.Users.AddRange(
            new M.User { Id = 1, Email = "leader@test.vn", FullName = "Lê Hoàng Long", PasswordHash = "h", Status = "ACTIVE" },
            new M.User { Id = 2, Email = "member@test.vn", FullName = "Phạm Thùy Linh", PasswordHash = "h", Status = "ACTIVE" });

        db.Teams.Add(new M.Team { Id = 10, AcademicSemesterId = 1, Code = "T10", Name = "StudySpace", Status = "ACTIVE", CreatedBy = 1 });
        db.TeamMembers.AddRange(
            new M.TeamMember { Id = 1, TeamId = 10, UserId = 1, IsLeader = true, AcademicSemesterId = 1 },
            new M.TeamMember { Id = 2, TeamId = 10, UserId = 2, IsLeader = false, AcademicSemesterId = 1 });

        db.Projects.Add(new M.Project { Id = 20, TeamId = 10, Code = "PRJ-1", Title = "StudySpace", Status = "ACTIVE", RowVersion = [1] });
        db.Meetings.Add(new M.Meeting
        {
            Id = 80012,
            ProjectId = 20,
            Title = "Họp rà soát tiến độ tuần",
            StartAt = startAt,
            EndAt = endAt,
            Status = "SCHEDULED",
            MeetingDeliveryMode = "REMOTE",
            VideoChannel = "IN_APP_VIDEO",
            CreatedBy = 1,
            ConcurrencyToken = Guid.NewGuid()
        });
        db.MeetingParticipants.AddRange(
            new M.MeetingParticipant { Id = 1, MeetingId = 80012, UserId = 1, AttendanceStatus = "CONFIRMED" },
            new M.MeetingParticipant { Id = 2, MeetingId = 80012, UserId = 2, AttendanceStatus = "INVITED" });

        if (session is not null)
        {
            session.MeetingId = 80012;
            db.MeetingVideoSessions.Add(session);
        }

        db.SaveChanges();
        return db;
    }

    private static MeetingVideoService CreateService(AipmsDbContext db, long actorId) =>
        new(
            db,
            new StubCurrentUser(actorId),
            new StubProjectAccess(),
            new StubVideoProvider(),
            Options.Create(new VideoMeetingOptions
            {
                Enabled = true,
                Provider = "LIVEKIT",
                ServerUrl = "https://livekit.example.com",
                ApiKey = "test-key",
                ApiSecret = "test-secret-32-bytes-long-value!",
                JoinTokenTtlSeconds = 300,
                JoinOpenBeforeMinutes = 15,
                JoinCloseAfterMinutes = 15,
            }),
            new FixedClock(Now),
            new NoopAuditTrail(),
            new NoopCleanupScheduler());

    [Theory]
    [InlineData("LIVE")]
    [InlineData("CREATED")]
    public async Task GetSessionAsync_allows_joining_active_started_session_even_past_scheduled_end(string status)
    {
        await using var db = SeedMeeting(
            startAt: Now.AddMinutes(-53),
            endAt: Now.AddMinutes(-25),
            session: new M.MeetingVideoSession
            {
                Id = 100,
                Provider = "LIVEKIT",
                ProviderRoomKey = "vm-overtime",
                Status = status,
                StartedBy = 1,
                StartedAt = Now.AddMinutes(-5),
                CreatedAt = Now.AddMinutes(-5),
                UpdatedAt = Now.AddMinutes(-5),
                ConcurrencyToken = Guid.NewGuid()
            });

        var svc = CreateService(db, actorId: 1);
        var dto = await svc.GetSessionAsync(80012, Ct);

        Assert.True(dto.Capabilities.CanJoin);
        Assert.False(dto.Capabilities.CanStart);
        Assert.True(dto.Capabilities.CanEnd);
        Assert.DoesNotContain("VIDEO_JOIN_WINDOW_CLOSED", dto.DenialReasons);
        Assert.DoesNotContain("VIDEO_JOIN_TOO_EARLY", dto.DenialReasons);
    }

    [Fact]
    public async Task GetSessionAsync_allows_joining_active_started_session_opened_early()
    {
        await using var db = SeedMeeting(
            startAt: Now.AddMinutes(45),
            endAt: Now.AddMinutes(105),
            session: new M.MeetingVideoSession
            {
                Id = 101,
                Provider = "LIVEKIT",
                ProviderRoomKey = "vm-early",
                Status = "CREATED",
                StartedBy = 1,
                StartedAt = Now.AddMinutes(-1),
                CreatedAt = Now.AddMinutes(-1),
                UpdatedAt = Now.AddMinutes(-1),
                ConcurrencyToken = Guid.NewGuid()
            });

        var svc = CreateService(db, actorId: 2);
        var dto = await svc.GetSessionAsync(80012, Ct);

        Assert.True(dto.Capabilities.CanJoin);
        Assert.DoesNotContain("VIDEO_JOIN_TOO_EARLY", dto.DenialReasons);
    }

    [Fact]
    public async Task GetSessionAsync_keeps_window_closed_reason_when_no_session_is_active()
    {
        await using var db = SeedMeeting(startAt: Now.AddMinutes(-53), endAt: Now.AddMinutes(-25), session: null);

        var svc = CreateService(db, actorId: 1);
        var dto = await svc.GetSessionAsync(80012, Ct);

        Assert.True(dto.Capabilities.CanStart);
        Assert.False(dto.Capabilities.CanJoin);
        Assert.Contains("VIDEO_SESSION_NOT_STARTED", dto.DenialReasons);
        Assert.Contains("VIDEO_JOIN_WINDOW_CLOSED", dto.DenialReasons);
    }

    [Fact]
    public async Task GetSessionAsync_reports_ended_and_allows_organizer_to_reopen()
    {
        await using var db = SeedMeeting(
            startAt: Now.AddMinutes(-53),
            endAt: Now.AddMinutes(-25),
            session: new M.MeetingVideoSession
            {
                Id = 102,
                Provider = "LIVEKIT",
                ProviderRoomKey = "vm-ended",
                Status = "ENDED",
                StartedBy = 1,
                StartedAt = Now.AddMinutes(-50),
                EndedAt = Now.AddMinutes(-20),
                CreatedAt = Now.AddMinutes(-50),
                UpdatedAt = Now.AddMinutes(-20),
                ConcurrencyToken = Guid.NewGuid()
            });

        var svc = CreateService(db, actorId: 1);
        var dto = await svc.GetSessionAsync(80012, Ct);

        Assert.True(dto.Capabilities.CanStart);
        Assert.False(dto.Capabilities.CanJoin);
        Assert.Contains("VIDEO_SESSION_ENDED", dto.DenialReasons);
    }

    private sealed class StubCurrentUser(long userId) : ICurrentUser
    {
        public long? UserId => userId;
        public string? Email => "test@example.com";
        public string? FullName => "Lê Hoàng Long";
        public IReadOnlyCollection<string> Roles => ["STUDENT"];
        public bool IsAuthenticated => true;
    }

    private sealed class StubProjectAccess : IProjectAccessService
    {
        public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class StubVideoProvider : IVideoMeetingProvider
    {
        public string Provider => "LIVEKIT";
        public Task CreateRoomAsync(VideoProviderRoomRequest request, CancellationToken ct) => Task.CompletedTask;
        public Task<VideoJoinCredentialDto> CreateJoinCredentialAsync(VideoProviderJoinRequest request, CancellationToken ct) =>
            Task.FromResult(new VideoJoinCredentialDto("LIVEKIT", "wss://livekit.example.com", request.ParticipantIdentity, request.ParticipantName, "tok", request.ExpiresAt, new(true, true, true, request.Moderator)));
        public Task CloseRoomAsync(string roomKey, CancellationToken ct) => Task.CompletedTask;
        public Task<VideoProviderRoomSnapshot?> GetRoomAsync(string roomKey, CancellationToken ct) => Task.FromResult<VideoProviderRoomSnapshot?>(null);
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class NoopAuditTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoopCleanupScheduler : IVideoCleanupScheduler
    {
        public Task EnqueueForMeetingAsync(long meetingId, CancellationToken ct) => Task.CompletedTask;
    }
}
