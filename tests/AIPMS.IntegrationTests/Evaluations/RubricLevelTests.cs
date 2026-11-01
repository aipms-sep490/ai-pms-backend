using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Domain.Exceptions;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class RubricLevelTests(RubricDatabaseFixture database) : IClassFixture<RubricDatabaseFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlyList<RubricCriterionLevelInput> FourLevels() =>
    [
        new(1, "Poor", "Does not meet baseline standard", 0m, 4.99m),
        new(2, "Satisfactory", "Meets minimum standard", 5.0m, 6.99m),
        new(3, "Good", "Exceeds standard with minor gaps", 7.0m, 8.49m),
        new(4, "Excellent", "Outstanding demonstration of mastery", 8.5m, 10.0m)
    ];

    [Fact]
    public async Task Migration_RunsTwiceWithoutError_Idempotent()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "db", "changes")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var scriptPath = Path.Combine(directory.FullName, "db", "changes", "20261030_add_rubric_criterion_levels.sql");
        Assert.True(File.Exists(scriptPath), $"Migration file not found at {scriptPath}");
        var script = await File.ReadAllTextAsync(scriptPath);

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        for (int run = 1; run <= 2; run++)
        {
            foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var command = new SqlCommand(batch, connection);
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    [Fact]
    public async Task Draft_CanBeSaved_WithNoLevelsOnLeafCriteria()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var request = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId,
            "R_NO_LEVELS_" + Guid.NewGuid().ToString("N")[..8], "No Levels Draft", "Draft without levels",
            [
                new("Criterion 1", "Desc 1", 50m, 10m, 0, true),
                new("Criterion 2", "Desc 2", 50m, 10m, 1, false)
            ]);

        var response = await staff.PostAsJsonAsync("/api/v1/rubrics", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var rubric = (await response.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Equal("DRAFT", rubric.Status);
        Assert.Equal(2, rubric.Criteria.Count);
        Assert.All(rubric.Criteria, c => Assert.Empty(c.Levels));
    }

    [Fact]
    public async Task Draft_CanBeSaved_WithLevelsOnLeafCriteria()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var request = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId,
            "R_WITH_LEVELS_" + Guid.NewGuid().ToString("N")[..8], "With Levels Draft", "Draft with 4 levels",
            [
                new("Implementation", "Software implementation quality", 50m, 10m, 0, true)
                {
                    LearningOutcome = "PLO-1: Code quality and modularity",
                    EvidenceGuidance = "Inspect git repository commits and PR reviews",
                    Levels = FourLevels()
                },
                new("Testing", "Automated test coverage", 50m, 10m, 1, false)
                {
                    LearningOutcome = "PLO-2: Verification and validation",
                    EvidenceGuidance = "Inspect CI test results and coverage reports",
                    Levels = FourLevels()
                }
            ]);

        var response = await staff.PostAsJsonAsync("/api/v1/rubrics", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var rubric = (await response.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Equal(2, rubric.Criteria.Count);

        var crit1 = rubric.Criteria.First(c => c.Name == "Implementation");
        Assert.NotNull(crit1.CriterionCode);
        Assert.Equal("PLO-1: Code quality and modularity", crit1.LearningOutcome);
        Assert.Equal("Inspect git repository commits and PR reviews", crit1.EvidenceGuidance);
        Assert.Equal(4, crit1.Levels.Count);
        Assert.Equal(1, crit1.Levels[0].LevelOrder);
        Assert.Equal("Poor", crit1.Levels[0].Label);
        Assert.Equal(4, crit1.Levels[3].LevelOrder);
        Assert.Equal("Excellent", crit1.Levels[3].Label);

        // Verify GET /api/v1/rubrics/{id} also returns the levels
        var getRes = await staff.GetAsync($"/api/v1/rubrics/{rubric.Id}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var fetched = (await getRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Equal(2, fetched.Criteria.Count);
        var fetchedCrit1 = fetched.Criteria.First(c => c.Name == "Implementation");
        Assert.Equal(4, fetchedCrit1.Levels.Count);
    }

    [Fact]
    public async Task Draft_WithParentHavingLevels_Returns422_RubricParentHasLevels()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var request = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId,
            "R_PARENT_LEVELS_" + Guid.NewGuid().ToString("N")[..8], "Parent with Levels", null,
            [
                new("Parent Criterion", null, 100m, null, 0, false)
                {
                    Levels = FourLevels(), // Parent criteria cannot have levels!
                    Children =
                    [
                        new("Child 1", null, 50m, 10m, 0, true) { Levels = FourLevels() },
                        new("Child 2", null, 50m, 10m, 1, false) { Levels = FourLevels() }
                    ]
                }
            ]);

        var response = await staff.PostAsJsonAsync("/api/v1/rubrics", request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        string? errorCode = null;
        if (root.TryGetProperty("errorCode", out var ec)) errorCode = ec.GetString();
        else if (root.TryGetProperty("code", out var c)) errorCode = c.GetString();

        Assert.Equal("RUBRIC_PARENT_HAS_LEVELS", errorCode);
    }

    [Fact]
    public async Task Publish_WithIncompleteLevelsOnLeaf_Returns422_RubricLeafLevelsIncomplete()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var request = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId,
            "R_INCOMPLETE_LEVELS_" + Guid.NewGuid().ToString("N")[..8], "Incomplete Levels Draft", null,
            [
                new("Criterion 1", "Desc 1", 50m, 10m, 0, true)
                {
                    Levels = FourLevels()
                },
                new("Criterion 2", "Desc 2", 50m, 10m, 1, false)
                {
                    // Only 2 levels out of 4 required!
                    Levels =
                    [
                        new(1, "Poor", "Low", 0m, 4.99m),
                        new(2, "Satisfactory", "Avg", 5.0m, 6.99m)
                    ]
                }
            ]);

        var createRes = await staff.PostAsJsonAsync("/api/v1/rubrics", request);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var draft = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        // Publishing must fail because Criterion 2 only has 2 levels
        var pubRes = await staff.PostAsJsonAsync($"/api/v1/rubrics/{draft.Id}/publish", new ChangeRubricStatusRequest(draft.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, pubRes.StatusCode);

        var doc = await JsonDocument.ParseAsync(await pubRes.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        string? errorCode = null;
        if (root.TryGetProperty("errorCode", out var ec)) errorCode = ec.GetString();
        else if (root.TryGetProperty("code", out var c)) errorCode = c.GetString();

        Assert.Equal("RUBRIC_LEAF_LEVELS_INCOMPLETE", errorCode);
        Assert.Contains("Criterion 2", root.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Publish_WithCompleteLevels_Returns200_PublishesSuccessfully()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var request = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId,
            "R_COMPLETE_LEVELS_" + Guid.NewGuid().ToString("N")[..8], "Complete Levels Rubric", null,
            [
                new("Criterion 1", "Desc 1", 50m, 10m, 0, true) { Levels = FourLevels() },
                new("Criterion 2", "Desc 2", 50m, 10m, 1, false) { Levels = FourLevels() }
            ]);

        var createRes = await staff.PostAsJsonAsync("/api/v1/rubrics", request);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var draft = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var pubRes = await staff.PostAsJsonAsync($"/api/v1/rubrics/{draft.Id}/publish", new ChangeRubricStatusRequest(draft.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, pubRes.StatusCode);

        var published = (await pubRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Equal("PUBLISHED", published.Status);
        Assert.True(published.IsActive);
        Assert.False(published.CanEdit);
    }

    [Fact]
    public async Task LegacyRubric_WithoutLevels_CanBeReadWithoutError()
    {
        var s = await database.Seed();
        await using var ctx = database.CreateContext();

        // Seed a legacy rubric published without any levels in the levels table
        var rubric = new M.Rubric
        {
            DepartmentId = s.Users.DepartmentId,
            AcademicSemesterId = s.SemesterId,
            Code = "LEGACY_R_" + Guid.NewGuid().ToString("N")[..8],
            Name = "Legacy Rubric",
            Description = "Legacy published rubric without levels",
            IsActive = true,
            CreatedBy = s.Users.Staff,
            CreatedAt = DateTime.UtcNow.AddMonths(-1),
            UpdatedAt = DateTime.UtcNow.AddMonths(-1),
            RubricCriteria =
            [
                new M.RubricCriterion
                {
                    WeightPercent = 100m,
                    MaxScore = 10m,
                    SortOrder = 0,
                    IsRequired = true,
                    CreatedAt = DateTime.UtcNow.AddMonths(-1),
                    UpdatedAt = DateTime.UtcNow.AddMonths(-1),
                    Criterion = new M.EvaluationCriterion
                    {
                        Code = "EC_LEGACY_" + Guid.NewGuid().ToString("N")[..8],
                        Name = "Legacy Criterion",
                        Description = "No levels",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow.AddMonths(-1),
                        UpdatedAt = DateTime.UtcNow.AddMonths(-1)
                    }
                }
            ]
        };
        ctx.Rubrics.Add(rubric);
        await ctx.SaveChangesAsync();

        ctx.Set<RubricVersion>().Add(new RubricVersion
        {
            RubricId = rubric.Id,
            RootRubricId = rubric.Id,
            VersionNumber = 1,
            Status = RubricStatuses.Published,
            ConcurrencyToken = Guid.NewGuid()
        });
        await ctx.SaveChangesAsync();

        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var response = await staff.GetAsync($"/api/v1/rubrics/{rubric.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = (await response.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Equal(rubric.Id, dto.Id);
        Assert.Equal("PUBLISHED", dto.Status);
        Assert.Single(dto.Criteria);
        Assert.Empty(dto.Criteria[0].Levels);
    }

    [Fact]
    public void ConfiguredLevelCount_ValidatesAgainstCustomCount()
    {
        var criteria = new RubricCriterionRecord[]
        {
            new(1, 10, "Crit 1", null, 100m, 10m, 0, true)
            {
                Levels = FourLevels().Select(l => new RubricCriterionLevelDto(l.LevelOrder, l.Label, l.Descriptor, l.ScoreFrom, l.ScoreTo)).ToArray()
            }
        };

        // 4 levels passes with requiredLevelCount = 4
        RubricRules.EnsurePublishable(criteria, requiredLevelCount: 4, requireLevels: true);

        // 4 levels fails with requiredLevelCount = 5
        var ex = Assert.Throws<DomainException>(() =>
            RubricRules.EnsurePublishable(criteria, requiredLevelCount: 5, requireLevels: true));
        Assert.Equal("RUBRIC_LEAF_LEVELS_INCOMPLETE", ex.Code);
        Assert.Contains("requires 5 levels", ex.Message);
    }
}
