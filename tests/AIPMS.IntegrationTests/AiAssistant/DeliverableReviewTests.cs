using System.Text;
using System.Security.Cryptography;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Projects;
using AIPMS.IntegrationTests.Evaluations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.AiAssistant;

public sealed class DeliverableReviewTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private readonly Storage storage = new();
    private readonly Audit audit = new();
    [Fact]
    public async Task Migration_can_be_repeated_without_losing_saved_runs()
    {
        var (s, v) = await Seed(); await using var db = database.CreateContext();
        var result = await Service(db, s.Scope.Users.Student).ReviewAsync(s.ProjectId, v.Id, new(s.RubricId, Scope: "COMMON"), default);
        var sql = await File.ReadAllTextAsync(Path.Combine(SqlSchemaBootstrap.Root, "db", "changes", "20261101_add_ai_deliverable_review.sql"));
        await SqlSchemaBootstrap.ExecuteAsync(database.ConnectionString, sql);
        await SqlSchemaBootstrap.ExecuteAsync(database.ConnectionString, sql);
        Assert.True(await db.AiDeliverableReviewRuns.AnyAsync(r => r.Id == result.RunId));
    }

    [Fact]
    public async Task Interrupted_file_read_retains_a_failed_run_without_result_or_evidence()
    {
        var (s, v) = await Seed(); await using var db = database.CreateContext();
        storage.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, s.Scope.Users.Student).ReviewAsync(s.ProjectId, v.Id, new(s.RubricId, Scope: "COMMON"), default));
        var run = await db.Set<AiRun>().AsNoTracking().SingleAsync(r => r.ProjectId == s.ProjectId);
        Assert.Equal("FAILED", run.Status); Assert.Null(run.ResultJson); Assert.Equal("[]", run.EvidenceJson);
        Assert.Empty(await db.AiDeliverableReviewRuns.Where(r => r.ProjectId == s.ProjectId).ToArrayAsync());
    }
    private DeliverableReviewService Service(AipmsDbContext db, long actor, bool enabled = true)
    {
        var current = new Actor(actor);
        return new(db, new(db, current), current, storage, audit, TimeProvider.System,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AiAssistant:DeliverableReviewEnabled"] = enabled.ToString() }).Build(), new Provider());
    }
    private async Task<(EvaluationScenario Scenario, M.DeliverableVersion Version)> Seed()
    {
        var scenario = await database.Seed(false);
        await using var db = database.CreateContext();
        var version = new M.DeliverableVersion { Deliverable = new() { ProjectId = scenario.ProjectId, Title = "Report", Status = "OPEN", CreatedBy = scenario.Scope.Users.Student },
            VersionNumber = 1, Status = "SUBMITTED", SubmittedBy = scenario.Scope.Users.Student };
        var bytes = Encoding.UTF8.GetBytes("Design\nThis report describes the system architecture.");
        var key = Guid.NewGuid().ToString("N"); storage.Bytes[key] = bytes;
        version.Files.Add(new() { UploadedBy = scenario.Scope.Users.Student, OriginalFileName = "report.txt", StoragePath = key,
            FileSizeBytes = bytes.Length, ChecksumSha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
        db.Add(version); await db.SaveChangesAsync(); return (scenario, version);
    }

    [Fact]
    public async Task Review_saves_immutable_snapshot_and_does_not_change_deliverable_or_grades()
    {
        var (s, v) = await Seed(); await using var db = database.CreateContext();
        var service = Service(db, s.Scope.Users.Student);
        var before = await db.Evaluations.CountAsync();
        var result = await service.ReviewAsync(s.ProjectId, v.Id, new(s.RubricId, Scope: "COMMON"), default);
        Assert.Equal("FALLBACK", result.GenerationStatus); Assert.Equal(v.Id, result.DeliverableVersionId);
        Assert.Equal(2, result.Criteria.Count); Assert.All(result.Criteria, c => Assert.NotEqual("PASS", c.Status));
        Assert.Equal("SUBMITTED", (await db.DeliverableVersions.FindAsync(v.Id))!.Status);
        Assert.Equal(before, await db.Evaluations.CountAsync());
        var stored = await db.AiDeliverableReviewRuns.SingleAsync(x => x.Id == result.RunId);
        Assert.Contains("system architecture", stored.EvidenceSnapshot);
        Assert.NotEmpty(await db.AiDeliverableReviewFindings.Where(x => x.RunId == result.RunId).ToArrayAsync());
        var history = await service.ListAsync(s.ProjectId, v.Id, 1, 20, default); Assert.Single(history.Items);
        Assert.Equal(result.RunId, (await service.GetAsync(s.ProjectId, result.RunId, default)).RunId);
        var again = await service.ReviewAsync(s.ProjectId, v.Id, new(s.RubricId, Scope: "COMMON"), default);
        Assert.NotEqual(result.RunId, again.RunId); Assert.Equal(stored.EvidenceSnapshot, (await db.AiDeliverableReviewRuns.AsNoTracking().SingleAsync(x => x.Id == result.RunId)).EvidenceSnapshot);
    }

    [Fact]
    public async Task Wrong_project_scope_checklist_and_disabled_flag_are_rejected()
    {
        var (s, v) = await Seed(); var other = await database.Seed(false);
        await using var db = database.CreateContext();
        var service = Service(db, s.Scope.Users.Student);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ReviewAsync(other.ProjectId, v.Id, new(s.RubricId, Scope: "COMMON"), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ReviewAsync(s.ProjectId, v.Id, new(s.RubricId, Scope: "INDIVIDUAL"), default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ReviewAsync(s.ProjectId, v.Id, new(s.RubricId, long.MaxValue, "COMMON"), default));
        await Assert.ThrowsAsync<ConflictException>(() => Service(db, s.Scope.Users.Student, false).OptionsAsync(s.ProjectId, v.Id, default));
        Assert.False(await db.AiDeliverableReviewRuns.AnyAsync(x => x.ProjectId == s.ProjectId));
    }

    [Fact]
    public async Task Draft_rubric_returns_insufficient_and_individual_target_is_enforced()
    {
        var (s, v) = await Seed(); await using var db = database.CreateContext();
        (await db.Set<RubricVersion>().FindAsync(s.RubricId))!.Status = "DRAFT"; await db.SaveChangesAsync();
        var result = await Service(db, s.Scope.Users.Student).ReviewAsync(s.ProjectId, v.Id, new(s.RubricId, Scope: "COMMON"), default);
        Assert.True(result.InsufficientEvidence); Assert.Empty(result.Criteria);
        var deliverable = await db.Deliverables.FindAsync(v.DeliverableId);
        deliverable!.TargetStudentId = s.Scope.Users.Staff; deliverable.EvidenceScope = "INDIVIDUAL";
        deliverable.TargetMajorId = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db, s.Scope.Users.Student).GetAsync(s.ProjectId, result.RunId, default));
    }

    private sealed class Actor(long id) : ICurrentUser {
        public bool IsAuthenticated => true; public long? UserId => id; public string? Email => null; public string? FullName => null;
        public IReadOnlyCollection<string> Roles => ["STUDENT"];
    }
    private sealed class Audit : IAuditTrail { public Task RecordAsync(AuditEntry entry, CancellationToken ct = default) => Task.CompletedTask; }
    private sealed class Provider : IAiTextGenerationProvider {
        public string ModelVersion => "GROUNDED_LOCAL_V1";
        public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
    private sealed class Storage : IFileStorage {
        public bool Fail { get; set; }
        public Dictionary<string, byte[]> Bytes { get; } = [];
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Fail ? throw new InvalidOperationException("Injected failure") : Task.FromResult<Stream>(new MemoryStream(Bytes[key]));
        public Task WriteAsync(string key, Stream content, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
}
