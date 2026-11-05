using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class EvaluationDraftContractTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlyList<RubricCriterionLevelInput> StandardFourLevels() =>
    [
        new(1, "Poor", "Does not meet baseline standard", 0m, 4.99m),
        new(2, "Satisfactory", "Meets minimum standard", 5.0m, 6.99m),
        new(3, "Good", "Exceeds standard with minor gaps", 7.0m, 8.49m),
        new(4, "Excellent", "Outstanding demonstration of mastery", 8.5m, 10.0m)
    ];

    private async Task<(EvaluationScenario Scenario, EvaluationAssignment Assignment, EvaluationScheme Scheme, EvaluationSchemeComponent Component, string ArchCode, string TestCode)> SeedModernAssignment(
        bool isDemo = false, string approvalStatus = "APPROVED")
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();

        var archCode = $"RC_ARCH_{Guid.NewGuid():N}";
        var testCode = $"RC_TEST_{Guid.NewGuid():N}";

        // 1. Update criteria with modern metadata (criterion code, learning outcome, evidence guidance, levels)
        var rubricCriteria = await db.RubricCriteria
            .Include(c => c.Criterion)
            .Where(c => c.RubricId == s.RubricId)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        rubricCriteria[0].Criterion.Code = archCode;
        rubricCriteria[0].LearningOutcome = "PLO-1: Ability to architect scalable and modular software solutions";
        rubricCriteria[0].EvidenceGuidance = "Inspect C4 diagrams, ADR records, and module boundaries in repo";

        rubricCriteria[1].Criterion.Code = testCode;
        rubricCriteria[1].LearningOutcome = "PLO-2: Ability to apply testing techniques ensuring software reliability";
        rubricCriteria[1].EvidenceGuidance = "Inspect unit and integration test suites and CI pipeline execution logs";

        // Add 4 levels for each criterion in scrambled order to verify DB ordering by LevelOrder
        foreach (var c in rubricCriteria)
        {
            var levels = StandardFourLevels();
            db.Set<RubricCriterionLevel>().AddRange(
                new RubricCriterionLevel { RubricCriterionId = c.Id, LevelOrder = 3, Label = levels[2].Label, Descriptor = levels[2].Descriptor, ScoreFrom = levels[2].ScoreFrom, ScoreTo = levels[2].ScoreTo, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new RubricCriterionLevel { RubricCriterionId = c.Id, LevelOrder = 1, Label = levels[0].Label, Descriptor = levels[0].Descriptor, ScoreFrom = levels[0].ScoreFrom, ScoreTo = levels[0].ScoreTo, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new RubricCriterionLevel { RubricCriterionId = c.Id, LevelOrder = 4, Label = levels[3].Label, Descriptor = levels[3].Descriptor, ScoreFrom = levels[3].ScoreFrom, ScoreTo = levels[3].ScoreTo, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new RubricCriterionLevel { RubricCriterionId = c.Id, LevelOrder = 2, Label = levels[1].Label, Descriptor = levels[1].Descriptor, ScoreFrom = levels[1].ScoreFrom, ScoreTo = levels[1].ScoreTo, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            );
        }

        // 2. Policy Version
        var policy = new PeriodPolicyVersion
        {
            ProjectPeriodId = s.PeriodId,
            Version = 1,
            Status = "PUBLISHED",
            EffectiveFrom = DateTime.UtcNow.AddDays(-10),
            EffectiveTo = DateTime.UtcNow.AddDays(10),
            ConcurrencyToken = Guid.NewGuid(),
            CreatedBy = s.Scope.Users.Staff,
            CreatedAt = DateTime.UtcNow,
            SnapshotJson = "{}"
        };
        db.Set<PeriodPolicyVersion>().Add(policy);
        await db.SaveChangesAsync();

        // 3. EvaluationScheme and Component
        var scheme = new EvaluationScheme
        {
            ProjectId = s.ProjectId,
            ProjectPeriodId = s.PeriodId,
            Name = "Final Defense Scheme",
            PassThreshold = 5.0m,
            Status = "PUBLISHED",
            CreatedBy = s.Scope.Users.Staff,
            CreatedAt = DateTime.UtcNow,
            PublishedBy = s.Scope.Users.Staff,
            PublishedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid(),
            PolicyVersionId = policy.Id,
            GovernanceJson = JsonSerializer.Serialize(new SchemePolicyInput(isDemo, approvalStatus, null))
        };
        db.Set<EvaluationScheme>().Add(scheme);
        await db.SaveChangesAsync();

        var component = new EvaluationSchemeComponent
        {
            SchemeId = scheme.Id,
            Name = "Final Defense Component",
            Scope = "COMMON",
            RubricId = s.RubricId,
            ProjectWeightPercent = 30.0m,
            StudentWeightPercent = 30.0m,
            RequiredEvaluators = 1,
            Stage = "DEFENSE"
        };
        db.Set<EvaluationSchemeComponent>().Add(component);
        await db.SaveChangesAsync();

        // 4. Scoped EvaluationAssignment
        var assignment = new EvaluationAssignment
        {
            ProjectId = s.ProjectId,
            EvaluatorId = s.Scope.Users.Lecturer,
            ProjectPeriodId = s.PeriodId,
            DepartmentId = s.Scope.Users.DepartmentId,
            EvaluationType = "LECTURER",
            RubricId = s.RubricId,
            Status = "ACTIVE",
            AssignedBy = s.Scope.Users.Staff,
            AssignedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid(),
            Scope = "COMMON",
            ComponentId = component.Id,
            PolicyVersionId = policy.Id,
            ScopeSnapshotJson = "{}",
            Stage = "DEFENSE"
        };
        db.Set<EvaluationAssignment>().Add(assignment);
        await db.SaveChangesAsync();

        return (s, assignment, scheme, component, archCode, testCode);
    }

    [Fact]
    public async Task GetAssignmentDetail_WithQualityLevels_ReturnsAllNewHeaderAndCriterionFields_InAscendingOrder()
    {
        var (s, assignment, _, component, archCode, _) = await SeedModernAssignment(isDemo: false, approvalStatus: "APPROVED");

        using var app = new EvaluationFactory(database);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        var response = await lecturer.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<EvaluationAssignmentDetailDto>(JsonOpts);
        Assert.NotNull(detail);

        // Header assertions
        Assert.Equal("DEFENSE", detail.Stage);
        Assert.Equal(30.0m, detail.ComponentStudentWeightPercent);
        Assert.Equal("APPROVED", detail.SchemeApprovalStatus);
        Assert.False(detail.SchemeIsDemo);

        // Criteria assertions
        Assert.NotNull(detail.Criteria);
        Assert.Equal(2, detail.Criteria.Count);

        var first = detail.Criteria[0];
        Assert.Equal(archCode, first.CriterionCode);
        Assert.Equal("PLO-1: Ability to architect scalable and modular software solutions", first.LearningOutcome);
        Assert.Equal("Inspect C4 diagrams, ADR records, and module boundaries in repo", first.EvidenceGuidance);
        Assert.Equal(60.0m, first.EffectiveWeightPercent);
        Assert.Equal(60.0m, first.WeightPercent);

        // Quality levels assertions: ordered by level_order ascending
        Assert.NotNull(first.Levels);
        Assert.Equal(4, first.Levels.Count);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(i + 1, first.Levels[i].LevelOrder);
        }
        Assert.Equal("Poor", first.Levels[0].Label);
        Assert.Equal("Satisfactory", first.Levels[1].Label);
        Assert.Equal("Good", first.Levels[2].Label);
        Assert.Equal("Excellent", first.Levels[3].Label);

        // Weight rule assertion: effectiveWeightPercent and componentStudentWeightPercent are distinct, NOT pre-multiplied
        Assert.Equal(60.0m, first.EffectiveWeightPercent);
        Assert.Equal(30.0m, detail.ComponentStudentWeightPercent);
        Assert.NotEqual(first.EffectiveWeightPercent * detail.ComponentStudentWeightPercent / 100m, first.EffectiveWeightPercent);
        Assert.NotEqual(first.EffectiveWeightPercent * detail.ComponentStudentWeightPercent / 100m, detail.ComponentStudentWeightPercent);
    }

    [Fact]
    public async Task GetAssignmentDetail_LegacyRubric_ReturnsEmptyLevelsAndNullStage()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();

        // 1. Policy Version
        var policy = new PeriodPolicyVersion
        {
            ProjectPeriodId = s.PeriodId,
            Version = 1,
            Status = "PUBLISHED",
            EffectiveFrom = DateTime.UtcNow.AddDays(-10),
            EffectiveTo = DateTime.UtcNow.AddDays(10),
            ConcurrencyToken = Guid.NewGuid(),
            CreatedBy = s.Scope.Users.Staff,
            CreatedAt = DateTime.UtcNow,
            SnapshotJson = "{}"
        };
        db.Set<PeriodPolicyVersion>().Add(policy);
        await db.SaveChangesAsync();

        // 2. EvaluationScheme with a component having Stage = null on legacy rubric (no levels)
        var scheme = new EvaluationScheme
        {
            ProjectId = s.ProjectId,
            ProjectPeriodId = s.PeriodId,
            Name = "Legacy Defense Scheme",
            PassThreshold = 5.0m,
            Status = "PUBLISHED",
            CreatedBy = s.Scope.Users.Staff,
            CreatedAt = DateTime.UtcNow,
            PublishedBy = s.Scope.Users.Staff,
            PublishedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid(),
            PolicyVersionId = policy.Id,
            GovernanceJson = null
        };
        db.Set<EvaluationScheme>().Add(scheme);
        await db.SaveChangesAsync();

        var component = new EvaluationSchemeComponent
        {
            SchemeId = scheme.Id,
            Name = "Legacy Component",
            Scope = "COMMON",
            RubricId = s.RubricId,
            ProjectWeightPercent = 100.0m,
            StudentWeightPercent = 100.0m,
            RequiredEvaluators = 1,
            Stage = null
        };
        db.Set<EvaluationSchemeComponent>().Add(component);
        await db.SaveChangesAsync();

        // 3. Scoped assignment with Stage = null
        var assignment = new EvaluationAssignment
        {
            ProjectId = s.ProjectId,
            EvaluatorId = s.Scope.Users.Lecturer,
            ProjectPeriodId = s.PeriodId,
            DepartmentId = s.Scope.Users.DepartmentId,
            EvaluationType = "LECTURER",
            RubricId = s.RubricId,
            Status = "ACTIVE",
            AssignedBy = s.Scope.Users.Staff,
            AssignedAt = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid(),
            Scope = "COMMON",
            ComponentId = component.Id,
            PolicyVersionId = policy.Id,
            ScopeSnapshotJson = "{}",
            Stage = null
        };
        db.Set<EvaluationAssignment>().Add(assignment);
        await db.SaveChangesAsync();

        using var app = new EvaluationFactory(database);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        var response = await lecturer.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<EvaluationAssignmentDetailDto>(JsonOpts);
        Assert.NotNull(detail);

        // Null semantics assertions: stage is null, NOT backfilled to "DEFENSE"
        Assert.Null(detail.Stage);
        Assert.Equal(100.0m, detail.ComponentStudentWeightPercent);
        Assert.Equal("DEMO", detail.SchemeApprovalStatus);
        Assert.True(detail.SchemeIsDemo);

        // Levels on legacy rubric are empty, NOT null
        Assert.NotNull(detail.Criteria);
        Assert.NotEmpty(detail.Criteria);
        Assert.All(detail.Criteria, c => Assert.Empty(c.Levels));

        // Legacy assignment can create draft, save, and score without error
        var draftRes = await lecturer.PostAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evaluation", null);
        Assert.Equal(HttpStatusCode.Created, draftRes.StatusCode);
        var draft = await draftRes.Content.ReadFromJsonAsync<EvaluationDraftDto>(JsonOpts);
        Assert.NotNull(draft);
        Assert.Null(draft.Stage);
        Assert.Equal(100.0m, draft.ComponentStudentWeightPercent);
        Assert.All(draft.Scores, score => Assert.Empty(score.Levels ?? []));

        // Save scores
        var saveRes = await lecturer.PutAsJsonAsync($"/api/v1/evaluations/{draft.Id}/draft", new SaveEvaluationDraftRequest(
            draft.ConcurrencyToken,
            "Legacy scoring comment",
            [new(s.Criteria[0], 8.0m, "Solid"), new(s.Criteria[1], 16.0m, "Clear")]
        ));
        Assert.Equal(HttpStatusCode.OK, saveRes.StatusCode);
        var saved = await saveRes.Content.ReadFromJsonAsync<EvaluationDraftDto>(JsonOpts);
        Assert.NotNull(saved);
        Assert.Equal(8.0m, saved.TotalScore);
    }

    [Fact]
    public async Task EvaluationDraft_CreateAndSaveDraft_ReflectsNewContractFields()
    {
        var (s, assignment, _, _, archCode, _) = await SeedModernAssignment(isDemo: false, approvalStatus: "APPROVED");

        using var app = new EvaluationFactory(database);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        // Create draft
        var createRes = await lecturer.PostAsync($"/api/v1/evaluation-assignments/{assignment.Id}/evaluation", null);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var draft = await createRes.Content.ReadFromJsonAsync<EvaluationDraftDto>(JsonOpts);
        Assert.NotNull(draft);

        // Verify draft header fields
        Assert.Equal("DEFENSE", draft.Stage);
        Assert.Equal(30.0m, draft.ComponentStudentWeightPercent);
        Assert.Equal("APPROVED", draft.SchemeApprovalStatus);
        Assert.False(draft.SchemeIsDemo);

        // Verify draft score item fields
        Assert.Equal(2, draft.Scores.Count);
        var scoreItem = draft.Scores[0];
        Assert.Equal(archCode, scoreItem.CriterionCode);
        Assert.Equal("PLO-1: Ability to architect scalable and modular software solutions", scoreItem.LearningOutcome);
        Assert.Equal("Inspect C4 diagrams, ADR records, and module boundaries in repo", scoreItem.EvidenceGuidance);
        Assert.Equal(60.0m, scoreItem.EffectiveWeightPercent);
        Assert.NotNull(scoreItem.Levels);
        Assert.Equal(4, scoreItem.Levels.Count);
        Assert.Equal(1, scoreItem.Levels[0].LevelOrder);
        Assert.Equal(4, scoreItem.Levels[3].LevelOrder);

        // Save draft with score
        var saveRes = await lecturer.PutAsJsonAsync($"/api/v1/evaluations/{draft.Id}/draft", new SaveEvaluationDraftRequest(
            draft.ConcurrencyToken,
            "Evaluated against 4 levels",
            [new(s.Criteria[0], 9.0m, "Excellent architecture"), new(s.Criteria[1], 18.0m, "Good testing")]
        ));
        Assert.Equal(HttpStatusCode.OK, saveRes.StatusCode);
        var savedDraft = await saveRes.Content.ReadFromJsonAsync<EvaluationDraftDto>(JsonOpts);
        Assert.NotNull(savedDraft);
        Assert.Equal("DEFENSE", savedDraft.Stage);
        Assert.Equal(30.0m, savedDraft.ComponentStudentWeightPercent);
        Assert.Equal("APPROVED", savedDraft.SchemeApprovalStatus);
        Assert.False(savedDraft.SchemeIsDemo);
        Assert.Equal(9.0m, savedDraft.TotalScore);

        // GET draft directly
        var getRes = await lecturer.GetAsync($"/api/v1/evaluations/{draft.Id}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var getDraft = await getRes.Content.ReadFromJsonAsync<EvaluationDraftDto>(JsonOpts);
        Assert.NotNull(getDraft);
        Assert.Equal("DEFENSE", getDraft.Stage);
        Assert.Equal(30.0m, getDraft.ComponentStudentWeightPercent);
        Assert.Equal("APPROVED", getDraft.SchemeApprovalStatus);
        Assert.False(getDraft.SchemeIsDemo);
        Assert.Equal(4, getDraft.Scores[0].Levels!.Count);
        Assert.Equal(archCode, getDraft.Scores[0].CriterionCode);
    }

    [Fact]
    public async Task GetAssignmentDetail_UnauthorizedEvaluator_ReturnsForbidden()
    {
        var (s, assignment, _, _, _, _) = await SeedModernAssignment();

        using var app = new EvaluationFactory(database);
        using var outsider = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer);

        var response = await outsider.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EvaluationScheme_DemoMode_ReflectedAccurately()
    {
        var (s, assignment, _, _, _, _) = await SeedModernAssignment(isDemo: true, approvalStatus: "DEMO");

        using var app = new EvaluationFactory(database);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        var response = await lecturer.GetAsync($"/api/v1/evaluation-assignments/{assignment.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<EvaluationAssignmentDetailDto>(JsonOpts);
        Assert.NotNull(detail);

        Assert.True(detail.SchemeIsDemo);
        Assert.Equal("DEMO", detail.SchemeApprovalStatus);
        Assert.True(detail.Assignment.SchemeIsDemo);
        Assert.Equal("DEMO", detail.Assignment.SchemeApprovalStatus);
    }
}
