using AIPMS.Infrastructure.Persistence.Generated;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class RubricScopeTests(RubricDatabaseFixture database) : IClassFixture<RubricDatabaseFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly RubricCriterionInput[] ValidCriteria =
        [new("Design", "Design evidence", 60m, 10m, 0, true), new("Presentation", null, 40m, 20m, 1, false)];

    private static async Task<M.Major> GetOrCreateMajorAsync(AipmsDbContext db, long departmentId)
    {
        var major = await db.Majors.FirstOrDefaultAsync(m => m.DepartmentId == departmentId);
        if (major is not null) return major;
        major = new M.Major
        {
            DepartmentId = departmentId,
            Code = "SE_" + Guid.NewGuid().ToString("N")[..8],
            Name = "Software Engineering",
            IsActive = true
        };
        db.Majors.Add(major);
        await db.SaveChangesAsync();
        return major;
    }

    [Fact]
    public async Task Migration_RunsTwiceWithoutError_Idempotent()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "db", "changes")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var scriptPath = Path.Combine(directory.FullName, "db", "changes", "20261030_add_rubric_scope_fields.sql");
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
    public async Task Create_NineRubrics_SameDepartmentAndSemester_DistinguishedByScope()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();
        var major = await GetOrCreateMajorAsync(db, s.Users.DepartmentId);

        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        // 9 combinations as defined in RUBRIC_DESIGN_2026-10-30.md §6.3:
        // 1. CMN-COLD: COMMON x COLD
        // 2. CMN-DEF: COMMON x DEFENSE
        // 3. SE-COLD: MAJOR_SPECIFIC x COLD
        // 4. SE-DEF: MAJOR_SPECIFIC x DEFENSE
        // 5. MKT-COLD: MAJOR_SPECIFIC x COLD (simulated with major)
        // 6. MKT-DEF: MAJOR_SPECIFIC x DEFENSE
        // 7. GD-COLD: MAJOR_SPECIFIC x COLD
        // 8. GD-DEF: MAJOR_SPECIFIC x DEFENSE
        // 9. IND-DEF: INDIVIDUAL x DEFENSE
        var configs = new[]
        {
            ("CMN_COLD", "COMMON", "COLD", (long?)null, "INTERDISCIPLINARY"),
            ("CMN_DEF", "COMMON", "DEFENSE", (long?)null, "INTERDISCIPLINARY"),
            ("SE_COLD", "MAJOR_SPECIFIC", "COLD", (long?)major.Id, "INTERDISCIPLINARY"),
            ("SE_DEF", "MAJOR_SPECIFIC", "DEFENSE", (long?)major.Id, "INTERDISCIPLINARY"),
            ("MKT_COLD", "MAJOR_SPECIFIC", "COLD", (long?)major.Id, "INTERDISCIPLINARY"),
            ("MKT_DEF", "MAJOR_SPECIFIC", "DEFENSE", (long?)major.Id, "INTERDISCIPLINARY"),
            ("GD_COLD", "MAJOR_SPECIFIC", "COLD", (long?)major.Id, "INTERDISCIPLINARY"),
            ("GD_DEF", "MAJOR_SPECIFIC", "DEFENSE", (long?)major.Id, "INTERDISCIPLINARY"),
            ("IND_DEF", "INDIVIDUAL", "DEFENSE", (long?)major.Id, "INTERDISCIPLINARY"),
        };

        var createdList = new List<RubricDto>();
        foreach (var (codePrefix, scope, stage, majorId, mode) in configs)
        {
            var code = $"R_{codePrefix}_{Guid.NewGuid():N}"[..24];
            var request = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, $"Rubric {codePrefix}", null, ValidCriteria)
            {
                ProjectMode = mode,
                EvaluationStage = stage,
                EvaluationScope = scope,
                MajorId = majorId,
                EffectiveFrom = new DateOnly(2026, 9, 1),
                EffectiveTo = new DateOnly(2026, 12, 31)
            };

            var res = await staff.PostAsJsonAsync("/api/v1/rubrics", request);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            var dto = (await res.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
            Assert.Equal(scope, dto.EvaluationScope);
            Assert.Equal(stage, dto.EvaluationStage);
            Assert.Equal(majorId, dto.MajorId);
            Assert.Equal(mode, dto.ProjectMode);
            Assert.Null(dto.ApprovalReference); // Client cannot set approvalReference
            createdList.Add(dto);
        }

        Assert.Equal(9, createdList.Count);

        // Verify filtering by EvaluationScope and EvaluationStage
        var filterRes = await staff.GetAsync($"/api/v1/rubrics?academicSemesterId={s.SemesterId}&evaluationScope=COMMON&evaluationStage=COLD");
        Assert.Equal(HttpStatusCode.OK, filterRes.StatusCode);
        var page = (await filterRes.Content.ReadFromJsonAsync<AIPMS.Application.Common.Models.PagedResult<RubricDto>>(JsonOpts))!;
        Assert.True(page.Items.Count >= 1);
        Assert.All(page.Items, r =>
        {
            Assert.Equal("COMMON", r.EvaluationScope);
            Assert.Equal("COLD", r.EvaluationStage);
        });
    }

    [Fact]
    public async Task Create_CommonScope_WithMajorId_Returns422_RubricCommonHasMajor()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();
        var major = await GetOrCreateMajorAsync(db, s.Users.DepartmentId);

        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var request = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId,
            $"R_ERR_{Guid.NewGuid():N}"[..20], "Invalid Common", null, ValidCriteria)
        {
            EvaluationScope = "COMMON",
            MajorId = major.Id // COMMON must not have major!
        };

        var response = await staff.PostAsJsonAsync("/api/v1/rubrics", request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        string? errorCode = null;
        if (root.TryGetProperty("errorCode", out var ec)) errorCode = ec.GetString();
        else if (root.TryGetProperty("code", out var c)) errorCode = c.GetString();

        Assert.Equal("RUBRIC_COMMON_HAS_MAJOR", errorCode);
    }

    [Fact]
    public async Task Create_MajorSpecificScope_WithoutMajorId_Returns422_RubricMajorRequired()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var request = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId,
            $"R_ERR_{Guid.NewGuid():N}"[..20], "Invalid Major Specific", null, ValidCriteria)
        {
            EvaluationScope = "MAJOR_SPECIFIC",
            MajorId = null // MAJOR_SPECIFIC requires a major!
        };

        var response = await staff.PostAsJsonAsync("/api/v1/rubrics", request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        string? errorCode = null;
        if (root.TryGetProperty("errorCode", out var ec)) errorCode = ec.GetString();
        else if (root.TryGetProperty("code", out var c)) errorCode = c.GetString();

        Assert.Equal("RUBRIC_MAJOR_REQUIRED", errorCode);
    }

    [Fact]
    public async Task Create_EffectiveFromAfterEffectiveTo_Returns422_RubricEffectiveRangeInvalid()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var request = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId,
            $"R_ERR_{Guid.NewGuid():N}"[..20], "Invalid Date Range", null, ValidCriteria)
        {
            EffectiveFrom = new DateOnly(2026, 12, 31),
            EffectiveTo = new DateOnly(2026, 9, 1) // from > to
        };

        var response = await staff.PostAsJsonAsync("/api/v1/rubrics", request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        string? errorCode = null;
        if (root.TryGetProperty("errorCode", out var ec)) errorCode = ec.GetString();
        else if (root.TryGetProperty("code", out var c)) errorCode = c.GetString();

        Assert.Equal("RUBRIC_EFFECTIVE_RANGE_INVALID", errorCode);
    }

    [Fact]
    public async Task Update_ApprovalReference_CannotBeSetDirectlyByClient()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var request = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId,
            $"R_APP_{Guid.NewGuid():N}"[..20], "Approval Test", null, ValidCriteria);

        var createRes = await staff.PostAsJsonAsync("/api/v1/rubrics", request);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Null(created.ApprovalReference);

        // UpdateRubricRequest does not even expose approvalReference, keeping it null
        var updateReq = new UpdateRubricRequest("Updated Name", "Updated Desc", ValidCriteria, created.ConcurrencyToken)
        {
            EvaluationScope = "COMMON"
        };
        var updateRes = await staff.PutAsJsonAsync($"/api/v1/rubrics/{created.Id}", updateReq);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        var updated = (await updateRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Null(updated.ApprovalReference);

        // Verify in DB directly
        await using var db = database.CreateContext();
        var row = await db.Rubrics.FindAsync(created.Id);
        Assert.NotNull(row);
        Assert.Null(row.ApprovalReference);
    }

    [Fact]
    public async Task LegacyRubric_WithNullScopeFields_CanBeReadAndPublished()
    {
        var s = await database.Seed();
        await using var ctx = database.CreateContext();

        var rubric = new M.Rubric
        {
            DepartmentId = s.Users.DepartmentId,
            AcademicSemesterId = s.SemesterId,
            Code = $"LEGACY_{Guid.NewGuid():N}"[..20],
            Name = "Legacy Rubric",
            Description = "Historical rubric with NULL scope fields",
            IsActive = false,
            CreatedBy = s.Users.Staff,
            CreatedAt = DateTime.UtcNow.AddMonths(-2),
            UpdatedAt = DateTime.UtcNow.AddMonths(-2),
            MajorId = null,
            ProjectMode = null,
            EvaluationStage = null,
            EvaluationScope = null,
            EffectiveFrom = null,
            EffectiveTo = null,
            ApprovalReference = null,
            RubricCriteria =
            [
                new M.RubricCriterion
                {
                    WeightPercent = 100m,
                    MaxScore = 10m,
                    SortOrder = 0,
                    IsRequired = true,
                    CreatedAt = DateTime.UtcNow.AddMonths(-2),
                    UpdatedAt = DateTime.UtcNow.AddMonths(-2),
                    Criterion = new M.EvaluationCriterion
                    {
                        Code = $"EC_{Guid.NewGuid():N}"[..20],
                        Name = "Criterion 1",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow.AddMonths(-2),
                        UpdatedAt = DateTime.UtcNow.AddMonths(-2)
                    }
                }
            ]
        };
        ctx.Rubrics.Add(rubric);
        await ctx.SaveChangesAsync();

        var versionToken = Guid.NewGuid();
        ctx.Set<RubricVersion>().Add(new RubricVersion
        {
            RubricId = rubric.Id,
            RootRubricId = rubric.Id,
            VersionNumber = 1,
            Status = RubricStatuses.Draft,
            ConcurrencyToken = versionToken
        });
        await ctx.SaveChangesAsync();

        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var getRes = await staff.GetAsync($"/api/v1/rubrics/{rubric.Id}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var dto = (await getRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Null(dto.EvaluationScope);
        Assert.Null(dto.EvaluationStage);
        Assert.Null(dto.MajorId);
        Assert.Null(dto.ProjectMode);
        Assert.Null(dto.EffectiveFrom);
        Assert.Null(dto.EffectiveTo);
        Assert.Null(dto.ApprovalReference);

        // Publish legacy rubric directly
        var pubRes = await staff.PostAsJsonAsync($"/api/v1/rubrics/{rubric.Id}/publish", new ChangeRubricStatusRequest(versionToken.ToString("N")));
        Assert.Equal(HttpStatusCode.OK, pubRes.StatusCode);
        var pubDto = (await pubRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Equal("PUBLISHED", pubDto.Status);
        Assert.True(pubDto.IsActive);
    }
}
