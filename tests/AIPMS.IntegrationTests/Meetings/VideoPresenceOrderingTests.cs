using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Services.Projects;
using AIPMS.Infrastructure.Video;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Meetings;

public sealed class VideoPresenceOrderingTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    private const string Secret = "presence-tests-only-signing-key-32-bytes-long";
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static DateTime T(int seconds) => Now.AddMinutes(-5).AddSeconds(seconds).UtcDateTime;
    private sealed record Scenario(long MeetingId, long SessionId, long UserId, string Room, string Identity);
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private async Task<Scenario> Seed()
    {
        var accounts = await database.SeedAsync();
        await using var db = database.CreateContext();
        var dept = await db.Departments.FindAsync(accounts.DepartmentId);
        var semester = new M.AcademicSemester { OrganizationId = dept!.OrganizationId, Code = Guid.NewGuid().ToString("N"),
            Name = "Presence tests", Status = "ACTIVE", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) };
        db.AcademicSemesters.Add(semester);
        await db.SaveChangesAsync();
        var project = new M.Project { Code = Guid.NewGuid().ToString("N"), Title = "Presence tests", Status = "ACTIVE", CreatedBy = accounts.Student,
            Team = new() { Code = Guid.NewGuid().ToString("N"), Name = "Test team", Status = "FORMING", AcademicSemesterId = semester.Id,
                CreatedBy = accounts.Student, TeamMembers = [new() { AcademicSemesterId = semester.Id, UserId = accounts.Student, IsLeader = true }] } };
        var meeting = new M.Meeting { Project = project, Title = "Presence tests", Status = "SCHEDULED", CreatedBy = accounts.Student,
            StartAt = T(-100), EndAt = T(600), MeetingDeliveryMode = "REMOTE", VideoChannel = "IN_APP_VIDEO" };
        var session = new M.MeetingVideoSession { Meeting = meeting, Provider = "LIVEKIT", ProviderRoomKey = "vm-" + Guid.NewGuid().ToString("N"),
            Status = "LIVE", StartedBy = accounts.Student, StartedAt = T(-60), CreatedAt = T(-60), UpdatedAt = T(-60) };
        var identity = "vp-" + Guid.NewGuid().ToString("N");
        session.ParticipantBindings.Add(new() { UserId = accounts.Student, ProviderParticipantIdentity = identity, CreatedAt = T(-50) });
        db.MeetingVideoSessions.Add(session);
        await db.SaveChangesAsync();
        return new(meeting.Id, session.Id, accounts.Student, session.ProviderRoomKey, identity);
    }

    private static string Event(Scenario s, string type, int at, string sid = "PA_one", int? joined = null, string? id = null) =>
        JsonSerializer.Serialize(new { id = id ?? Guid.NewGuid().ToString("N"), @event = type,
            createdAt = new DateTimeOffset(T(at)).ToUnixTimeSeconds().ToString(), room = new { name = s.Room },
            participant = new { identity = s.Identity, sid, joinedAt = joined.HasValue ? new DateTimeOffset(T(joined.Value)).ToUnixTimeSeconds().ToString() : null } });

    private static string Sign(string body)
    {
        static string B64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var data = B64(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\"}")) + "." + B64(JsonSerializer.SerializeToUtf8Bytes(new
        { iss = "presence-test-key", nbf = Now.AddMinutes(-1).ToUnixTimeSeconds(), exp = Now.AddMinutes(5).ToUnixTimeSeconds(),
            sha256 = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body))) }));
        return data + "." + B64(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(data)));
    }

    private static VideoProviderEventService Service(AipmsDbContext db) => new(db,
        new(Options.Create(new VideoMeetingOptions { Enabled = true, ApiKey = "presence-test-key", ApiSecret = Secret }), new Clock(Now)), new Clock(Now));

    private async Task Deliver(string body)
    {
        await using var db = database.CreateContext();
        await Service(db).ProcessLiveKitAsync(body, Sign(body), default);
    }

    private async Task<List<M.MeetingVideoPresenceSession>> Rows(Scenario s)
    {
        await using var db = database.CreateContext();
        return await db.MeetingVideoPresenceSessions.Where(x => x.MeetingVideoSessionId == s.SessionId).OrderBy(x => x.JoinedAt).ToListAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Leave_and_join_have_identical_results_in_either_order(bool leaveFirst)
    {
        var s = await Seed();
        var join = Event(s, "participant_joined", 0);
        var leave = Event(s, "participant_left", 12);
        await Deliver(leaveFirst ? leave : join);
        await Deliver(leaveFirst ? join : leave);
        var row = Assert.Single(await Rows(s));
        Assert.Equal(T(0), row.JoinedAt);
        Assert.Equal(T(12), row.LeftAt);
    }

    [Fact]
    public async Task Old_connection_leave_does_not_close_reconnect_and_repeated_joins_are_idempotent()
    {
        var s = await Seed();
        var join = Event(s, "participant_joined", 0);
        await Deliver(join); await Deliver(join);
        await Deliver(Event(s, "participant_joined", 0));
        await Deliver(Event(s, "participant_joined", 20, "PA_two"));
        await Deliver(Event(s, "participant_left", 10, joined: 0));
        var rows = await Rows(s);
        Assert.Equal(2, rows.Count);
        Assert.Equal(T(10), rows[0].LeftAt);
        Assert.Null(rows[1].LeftAt);
        await Deliver(Event(s, "participant_connection_aborted", 30, "PA_unknown"));
        Assert.Equal(2, (await Rows(s)).Count);
    }

    [Theory]
    [InlineData("JLF")][InlineData("JFL")][InlineData("LJF")]
    [InlineData("LFJ")][InlineData("FJL")][InlineData("FLJ")]
    public async Task Room_finish_and_delayed_participant_events_are_order_independent(string order)
    {
        var s = await Seed();
        var events = new Dictionary<char, string> { ['J'] = Event(s, "participant_joined", 0),
            ['L'] = Event(s, "participant_left", 10, joined: 0), ['F'] = Event(s, "room_finished", 20) };
        foreach (var key in order) await Deliver(events[key]);
        await Deliver(Event(s, "participant_joined", 30, "PA_late"));
        await Deliver(Event(s, "room_started", -10));
        var row = Assert.Single(await Rows(s));
        Assert.Equal(T(0), row.JoinedAt); Assert.Equal(T(10), row.LeftAt);
        await using var db = database.CreateContext();
        Assert.Equal("ENDED", (await db.MeetingVideoSessions.FindAsync(s.SessionId))!.Status);
        Assert.Equal("SCHEDULED", (await db.Meetings.FindAsync(s.MeetingId))!.Status);
        Assert.Empty(await db.MeetingParticipants.Where(x => x.MeetingId == s.MeetingId).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_replays_and_same_connection_events_create_one_segment()
    {
        var s = await Seed();
        var join = Event(s, "participant_joined", 0);
        var leave = Event(s, "participant_left", 15, joined: 0);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Deliver(i % 2 == 0 ? join : leave)));
        var row = Assert.Single(await Rows(s));
        Assert.Equal(T(0), row.JoinedAt); Assert.Equal(T(15), row.LeftAt);
        await using var db = database.CreateContext();
        Assert.Equal(2, await db.VideoProviderEvents.CountAsync(x => x.MeetingVideoSessionId == s.SessionId));
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Injected failure before commit");
    }

    [Fact]
    public async Task Failed_projection_rolls_back_inbox_and_presence_then_retry_succeeds()
    {
        var s = await Seed(); var body = Event(s, "participant_joined", 0);
        await using (var db = new AipmsDbContext(new DbContextOptionsBuilder<AipmsDbContext>().UseSqlServer(database.ConnectionString)
            .AddInterceptors(new FailAfterSave()).Options))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db).ProcessLiveKitAsync(body, Sign(body), default));
        Assert.Empty(await Rows(s));
        await using (var db = database.CreateContext())
            Assert.False(await db.VideoProviderEvents.AnyAsync(x => x.MeetingVideoSessionId == s.SessionId));
        await Deliver(body);
        Assert.Single(await Rows(s));
    }

    [Theory]
    [InlineData("missing-time")][InlineData("overflow-time")][InlineData("future-time")][InlineData("missing-sid")]
    public async Task Unusable_evidence_is_acknowledged_without_guessing_presence(string invalid)
    {
        var s = await Seed();
        var body = JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString("N"), @event = "participant_joined",
            createdAt = invalid == "missing-time" ? null : invalid == "overflow-time" ? long.MaxValue.ToString()
                : new DateTimeOffset(T(invalid == "future-time" ? 3600 : 0)).ToUnixTimeSeconds().ToString(),
            room = new { name = s.Room }, participant = new { identity = s.Identity, sid = invalid == "missing-sid" ? null : "PA_one" } });
        await Deliver(body); await Deliver(body);
        Assert.Empty(await Rows(s));
        await using var db = database.CreateContext();
        var inbox = await db.VideoProviderEvents.SingleAsync(x => x.MeetingVideoSessionId == s.SessionId);
        Assert.Equal("PROCESSED", inbox.ProcessingStatus); Assert.NotNull(inbox.ErrorCode);
    }

    [Fact]
    public async Task Millisecond_join_and_second_resolution_leave_never_produce_negative_duration()
    {
        var s = await Seed();
        var body = JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString("N"), @event = "participant_left",
            created_at = new DateTimeOffset(T(0)).ToUnixTimeSeconds(), room = new { name = s.Room },
            participant = new { identity = s.Identity, sid = "PA_one", joined_at_ms = new DateTimeOffset(T(0)).ToUnixTimeMilliseconds() + 500 } });
        await Deliver(body);
        var row = Assert.Single(await Rows(s));
        Assert.Equal(T(0).AddMilliseconds(500), row.JoinedAt); Assert.Equal(row.JoinedAt, row.LeftAt);
    }

    [Fact]
    public async Task Lifecycle_end_closes_presence_and_delayed_join_stays_closed_during_cleanup()
    {
        var s = await Seed(); await Deliver(Event(s, "participant_joined", 0));
        await using (var db = database.CreateContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await new VideoCleanupScheduler(db, new Clock(new DateTimeOffset(T(20)))).EnqueueForMeetingAsync(s.MeetingId, default);
            await tx.CommitAsync();
        }
        Assert.Equal(T(20), Assert.Single(await Rows(s)).LeftAt);
        await Deliver(Event(s, "participant_joined", 10, "PA_two"));
        Assert.All(await Rows(s), x => Assert.Equal(T(20), x.LeftAt));
        await using (var db = database.CreateContext())
            await new VideoCleanupProcessor(db, new FakeProvider(), new Clock(Now), new Audit()).ProcessAsync(default);
        Assert.All(await Rows(s), x => Assert.Equal(T(20), x.LeftAt));
        await Deliver(Event(s, "room_finished", 19));
        await using var check = database.CreateContext();
        Assert.Equal(T(20), (await check.MeetingVideoSessions.FindAsync(s.SessionId))!.EndedAt);
        Assert.Equal("SUCCEEDED", (await check.VideoProviderCleanupJobs.SingleAsync(x => x.MeetingVideoSessionId == s.SessionId)).Status);
    }

    private sealed class FakeProvider : IVideoMeetingProvider
    {
        public string Provider => "LIVEKIT";
        public Task CreateRoomAsync(VideoProviderRoomRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<VideoJoinCredentialDto> CreateJoinCredentialAsync(VideoProviderJoinRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CloseRoomAsync(string roomKey, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<VideoProviderRoomSnapshot?> GetRoomAsync(string roomKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Audit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Factory(SupervisorDatabaseFixture database) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = database.ConnectionString, ["VideoMeeting:Enabled"] = "false" }));
        }
    }

    [Fact]
    public async Task Presence_read_excludes_impossible_legacy_rows_and_caps_closed_room_duration()
    {
        var s = await Seed();
        await using (var db = database.CreateContext())
        {
            var session = (await db.MeetingVideoSessions.FindAsync(s.SessionId))!;
            session.Status = "ENDED"; session.EndedAt = T(20);
            db.MeetingVideoPresenceSessions.AddRange(
                new() { MeetingVideoSessionId = s.SessionId, UserId = s.UserId, ProviderParticipantIdentity = s.Identity, JoinedAt = T(0) },
                new() { MeetingVideoSessionId = s.SessionId, UserId = s.UserId, ProviderParticipantIdentity = s.Identity, JoinedAt = T(8), LeftAt = T(7) },
                new() { MeetingVideoSessionId = s.SessionId, UserId = s.UserId, ProviderParticipantIdentity = s.Identity, JoinedAt = T(30) });
            await db.SaveChangesAsync();
        }
        using var factory = new Factory(database); using var client = factory.CreateAuthenticatedClient(s.UserId);
        var result = await client.GetFromJsonAsync<VideoPresenceDto>($"/api/v1/meetings/{s.MeetingId}/video/presence");
        var participant = Assert.Single(result!.Participants);
        Assert.Equal(1, participant.ConnectionCount); Assert.Equal(20, participant.TotalConnectedSeconds);
        Assert.Equal(T(20), participant.LastLeftAt);
        Assert.Equal(3, (await Rows(s)).Count);
    }
}
