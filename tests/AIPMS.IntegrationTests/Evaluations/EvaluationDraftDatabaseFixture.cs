using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class EvaluationDraftDatabaseFixture : IAsyncLifetime
{
    private readonly RubricDatabaseFixture rubricDatabase = new();
    public string ConnectionString => rubricDatabase.ConnectionString;
    public static readonly DateTime Now = new(2026, 9, 11, 15, 0, 0, DateTimeKind.Utc);
    public AipmsDbContext CreateContext() => rubricDatabase.CreateContext();

    public async Task InitializeAsync()
    {
        await rubricDatabase.InitializeAsync();
        try { await Migrate(); }
        catch { await rubricDatabase.DisposeAsync(); throw; }
    }
    public Task DisposeAsync() => rubricDatabase.DisposeAsync();
    public Task Migrate() => FinalSubmissions.FinalSubmissionTestMigration.Apply(ConnectionString);

    public async Task<EvaluationScenario> Seed(bool lockedSubmission = true)
    {
        var s = await rubricDatabase.Seed();
        await using var db = CreateContext();
        M.RubricCriterion Criterion(string name, decimal weight, decimal max, int order, bool required) => new()
        {
            Criterion = new() { Code = Guid.NewGuid().ToString("N"), Name = name, IsActive = true },
            WeightPercent = weight, MaxScore = max, SortOrder = order, IsRequired = required
        };
        var rubric = new M.Rubric { DepartmentId = s.Users.DepartmentId, AcademicSemesterId = s.SemesterId,
            Code = Guid.NewGuid().ToString("N"), Name = "Evaluation rubric", IsActive = true, CreatedBy = s.Users.Staff,
            RubricCriteria = [Criterion("Design", 60, 10, 0, true), Criterion("Presentation", 40, 20, 1, false)] };
        var period = new M.ProjectPeriod { AcademicSemesterId = s.SemesterId, Code = Guid.NewGuid().ToString("N"),
            Name = "Evaluation window", PeriodType = "EVALUATION", Status = "ACTIVE", StartAt = Now.AddDays(-1),
            EndAt = Now.AddDays(1), Rubric = rubric };
        var project = new M.Project { Code = Guid.NewGuid().ToString("N"), Title = "Final project",
            CreatedBy = s.Users.Student, Status = "FINAL_SUBMISSION", RegisteredAt = Now.AddDays(-30),
            Team = new() { Code = Guid.NewGuid().ToString("N"), Name = "Team", CreatedBy = s.Users.Student,
                AcademicSemesterId = s.SemesterId, Status = "LOCKED" },
            ProjectMajors = [new() { Major = new() { DepartmentId = s.Users.DepartmentId,
                Code = Guid.NewGuid().ToString("N"), Name = "Software Engineering", IsActive = true } }] };
        var request = new M.SupervisorRequest { Project = project, SupervisorProfileId = s.Users.ProfileId,
            RequestedBy = s.Users.Student, Status = "ACCEPTED", RequestedAt = Now.AddDays(-25), RespondedAt = Now.AddDays(-24) };
        var supervisor = new M.SupervisorAssignment { Project = project, SupervisorRequest = request,
            SupervisorProfileId = s.Users.ProfileId, IsPrimary = true, AssignedAt = Now.AddDays(-24) };
        db.ProjectPeriods.Add(period);
        db.SupervisorAssignments.Add(supervisor);
        await db.SaveChangesAsync();
        if (lockedSubmission)
        {
            var finalPeriod = new M.ProjectPeriod { AcademicSemesterId = s.SemesterId, Code = Guid.NewGuid().ToString("N"),
                Name = "Final submission", PeriodType = "FINAL_SUBMISSION", Status = "ACTIVE",
                StartAt = Now.AddDays(-3), EndAt = Now.AddDays(-1) };
            var version = new M.DeliverableVersion { Deliverable = new() { ProjectId = project.Id,
                Title = "Final report", Status = "OPEN", CreatedBy = s.Users.Student }, VersionNumber = 1,
                Status = "SUBMITTED", SubmittedBy = s.Users.Student, SubmittedAt = Now.AddDays(-2) };
            db.ProjectPeriods.Add(finalPeriod);
            db.DeliverableVersions.Add(version);
            await db.SaveChangesAsync();
            db.Set<FinalSubmission>().Add(new() { ProjectId = project.Id, ProjectPeriodId = finalPeriod.Id,
                SubmittedBy = s.Users.Student, SubmittedAt = Now.AddDays(-2), Deadline = finalPeriod.EndAt,
                DraftConcurrencyToken = Guid.NewGuid(), RequirementsConcurrencyToken = Guid.NewGuid(),
                Items = [new() { DeliverableVersionId = version.Id, DeliverableId = version.DeliverableId,
                    Title = "Final report", VersionNumber = 1, StatusAtSubmission = "SUBMITTED", WasRequired = true,
                    FilesJson = System.Text.Json.JsonSerializer.Serialize(new[] {
                        new AIPMS.Application.Features.FinalSubmissions.Models.FinalSnapshotFile(
                            new(1, "DELIVERABLE_VERSION", version.Id, "report.txt", "text/plain", 4, new string('a', 64), s.Users.Student, Now.AddDays(-2)), "fixture-object") }) }] });
            await db.SaveChangesAsync();
        }
        db.Set<RubricVersion>().Add(new() { RubricId = rubric.Id, RootRubricId = rubric.Id, VersionNumber = 1,
            Status = "PUBLISHED", ConcurrencyToken = Guid.NewGuid() });
        await db.SaveChangesAsync();
        return new(s, project.Id, period.Id, rubric.Id, rubric.RubricCriteria.OrderBy(c => c.SortOrder).Select(c => c.Id).ToArray());
    }
}

public sealed record EvaluationScenario(RubricScenario Scope, long ProjectId, long PeriodId, long RubricId, long[] Criteria);
