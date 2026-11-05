using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class RubricApprovalWorkflowTests(RubricDatabaseFixture database) : IClassFixture<RubricDatabaseFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlyList<RubricCriterionLevelInput> FourLevels() =>
    [
        new(1, "Poor", "Does not meet baseline standard", 0m, 4.99m),
        new(2, "Satisfactory", "Meets minimum standard", 5.0m, 6.99m),
        new(3, "Good", "Exceeds standard with minor gaps", 7.0m, 8.49m),
        new(4, "Excellent", "Outstanding demonstration of mastery", 8.5m, 10.0m)
    ];

    private static async Task<long> CreatePureAdminAsync(AipmsDbContext db)
    {
        var adminRole = await db.Roles.SingleAsync(r => r.Code == AppRoles.Admin);
        var admin = new M.User
        {
            Email = $"pure_admin_{Guid.NewGuid():N}@test.local",
            FullName = "Pure Admin",
            PasswordHash = "unused",
            Status = "ACTIVE",
            AcademicProfileStatus = "VERIFIED",
            DepartmentId = null,
            UserRoleUsers = [new() { RoleId = adminRole.Id }]
        };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin.Id;
    }

    private static async Task<long> CreateDepartmentStaffAsync(AipmsDbContext db, long departmentId)
    {
        var staffRole = await db.Roles.SingleAsync(r => r.Code == AppRoles.DepartmentStaff);
        var staff = new M.User
        {
            Email = $"staff2_{Guid.NewGuid():N}@test.local",
            FullName = "Second Department Staff",
            PasswordHash = "unused",
            Status = "ACTIVE",
            AcademicProfileStatus = "VERIFIED",
            DepartmentId = departmentId,
            UserRoleUsers = [new() { RoleId = staffRole.Id }]
        };
        db.Users.Add(staff);
        await db.SaveChangesAsync();
        return staff.Id;
    }

    [Fact]
    public async Task Migration_RunsTwiceWithoutError_Idempotent()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "db", "changes")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var scriptPath = Path.Combine(directory.FullName, "db", "changes", "20261030_add_rubric_approvals.sql");
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
    public async Task SubmitForApproval_WithIncompleteLeafLevels_Returns422()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var client = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var criteriaWithoutLevels = new RubricCriterionInput[]
        {
            new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = [] }
        };

        var code = $"R_SUB_INC_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Draft Incomplete", null, criteriaWithoutLevels)
        {
            ProjectMode = "INTERDISCIPLINARY",
            EvaluationStage = "COLD",
            EvaluationScope = "COMMON"
        };
        var createRes = await client.PostAsJsonAsync("/api/v1/rubrics", createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitReq = new SubmitRubricForApprovalRequest(created.ConcurrencyToken);
        var submitRes = await client.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval", submitReq);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, submitRes.StatusCode);

        var body = await submitRes.Content.ReadAsStringAsync();
        Assert.Contains("RUBRIC_LEAF_LEVELS_INCOMPLETE", body);
        Assert.Contains("Criterion 1", body);
    }

    [Fact]
    public async Task SubmitForApproval_WithCompleteLevels_TransitionsToPendingApproval_AndCanEditIsFalse()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var client = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var criteriaWithLevels = new RubricCriterionInput[]
        {
            new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }
        };

        var code = $"R_SUB_OK_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Draft Complete", null, criteriaWithLevels)
        {
            ProjectMode = "INTERDISCIPLINARY",
            EvaluationStage = "COLD",
            EvaluationScope = "COMMON"
        };
        var createRes = await client.PostAsJsonAsync("/api/v1/rubrics", createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitReq = new SubmitRubricForApprovalRequest(created.ConcurrencyToken);
        var submitRes = await client.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval", submitReq);
        Assert.Equal(HttpStatusCode.OK, submitRes.StatusCode);
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Equal("PENDING_APPROVAL", submitted.Status);
        Assert.False(submitted.CanEdit);

        var getRes = await client.GetAsync($"/api/v1/rubrics/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var fetched = (await getRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Equal("PENDING_APPROVAL", fetched.Status);
        Assert.False(fetched.CanEdit);
    }

    [Fact]
    public async Task RubricInPendingApproval_CallingPut_Returns409()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var client = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var criteriaWithLevels = new RubricCriterionInput[]
        {
            new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }
        };

        var code = $"R_PEND_PUT_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Pending Rubric", null, criteriaWithLevels)
        {
            ProjectMode = "INTERDISCIPLINARY",
            EvaluationStage = "COLD",
            EvaluationScope = "COMMON"
        };
        var createRes = await client.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await client.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var updateReq = new UpdateRubricRequest("Changed Name", null, criteriaWithLevels, submitted.ConcurrencyToken);
        var putRes = await client.PutAsJsonAsync($"/api/v1/rubrics/{created.Id}", updateReq);
        Assert.Equal(HttpStatusCode.Conflict, putRes.StatusCode);
    }

    [Fact]
    public async Task Submitter_CannotApproveOwnRubric_Returns403()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var client = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var criteriaWithLevels = new RubricCriterionInput[]
        {
            new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }
        };

        var code = $"R_SELF_APP_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Self App Test", null, criteriaWithLevels)
        {
            ProjectMode = "INTERDISCIPLINARY",
            EvaluationStage = "COLD",
            EvaluationScope = "COMMON"
        };
        var createRes = await client.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await client.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var approveRes = await client.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(submitted.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Forbidden, approveRes.StatusCode);

        var body = await approveRes.Content.ReadAsStringAsync();
        Assert.Contains("RUBRIC_SELF_APPROVAL_FORBIDDEN", body);
    }

    [Fact]
    public async Task PureAdmin_CannotApproveRubric_Returns403()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();
        var pureAdminId = await CreatePureAdminAsync(db);

        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var pureAdmin = app.CreateAuthenticatedClient(pureAdminId, roles: [AppRoles.Admin]);

        var criteriaWithLevels = new RubricCriterionInput[]
        {
            new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }
        };

        var code = $"R_ADM_NOT_AC_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Admin Test", null, criteriaWithLevels)
        {
            ProjectMode = "INTERDISCIPLINARY",
            EvaluationStage = "COLD",
            EvaluationScope = "COMMON"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var approveRes = await pureAdmin.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(submitted.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Forbidden, approveRes.StatusCode);

        var body = await approveRes.Content.ReadAsStringAsync();
        Assert.Contains("RUBRIC_APPROVAL_ADMIN_NOT_ACADEMIC", body);
    }

    [Fact]
    public async Task ParticipatingDepartmentStaff_CannotApproveCommonInterdisciplinaryRubric_Returns403()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var outsideStaff = app.CreateAuthenticatedClient(s.Users.OutsideStaff, roles: [AppRoles.DepartmentStaff]);

        var criteriaWithLevels = new RubricCriterionInput[]
        {
            new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }
        };

        var code = $"R_NOT_LEAD_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Common Lead Test", null, criteriaWithLevels)
        {
            ProjectMode = "INTERDISCIPLINARY",
            EvaluationStage = "COLD",
            EvaluationScope = "COMMON"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var approveRes = await outsideStaff.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(submitted.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Forbidden, approveRes.StatusCode);

        var body = await approveRes.Content.ReadAsStringAsync();
        Assert.Contains("RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT", body);
    }

    [Fact]
    public async Task Reject_WithoutReason_Returns422()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();
        var staff2Id = await CreateDepartmentStaffAsync(db, s.Users.DepartmentId);

        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var approver = app.CreateAuthenticatedClient(staff2Id, roles: [AppRoles.DepartmentStaff]);

        var criteriaWithLevels = new RubricCriterionInput[]
        {
            new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }
        };

        var code = $"R_REJ_NO_RSN_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Rejection No Reason", null, criteriaWithLevels)
        {
            ProjectMode = "INTERDISCIPLINARY",
            EvaluationStage = "COLD",
            EvaluationScope = "COMMON"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var rejectReq = new RejectRubricRequest(submitted.ConcurrencyToken, "   ");
        var rejectRes = await approver.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/reject", rejectReq);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejectRes.StatusCode);

        var body = await rejectRes.Content.ReadAsStringAsync();
        Assert.Contains("RUBRIC_REJECTION_REASON_REQUIRED", body);
    }

    [Fact]
    public async Task Reject_WithReason_ReturnsToDraft_AndRecordsApprovalDecision()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();
        var staff2Id = await CreateDepartmentStaffAsync(db, s.Users.DepartmentId);

        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var approver = app.CreateAuthenticatedClient(staff2Id, roles: [AppRoles.DepartmentStaff]);

        var criteriaWithLevels = new RubricCriterionInput[]
        {
            new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }
        };

        var code = $"R_REJ_OK_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Rejection Test", null, criteriaWithLevels)
        {
            ProjectMode = "INTERDISCIPLINARY",
            EvaluationStage = "COLD",
            EvaluationScope = "COMMON"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var rejectReq = new RejectRubricRequest(submitted.ConcurrencyToken, "Please refine the descriptors in level 2.");
        var rejectRes = await approver.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/reject", rejectReq);
        Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);
        var rejected = (await rejectRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Equal("DRAFT", rejected.Status);
        Assert.True(rejected.CanEdit);

        await using var verifyDb = database.CreateContext();
        var approvalRow = await verifyDb.RubricApprovals.OrderByDescending(a => a.Id).FirstOrDefaultAsync(a => a.RubricId == created.Id);
        Assert.NotNull(approvalRow);
        Assert.Equal("REJECTED", approvalRow.Decision);
        Assert.Equal(staff2Id, approvalRow.DecidedBy);
        Assert.NotNull(approvalRow.DecidedAt);
        Assert.Equal("Please refine the descriptors in level 2.", approvalRow.Reason);
    }

    [Fact]
    public async Task SecondStaff_ApprovesWithApprovalReference_PublishedAndRecordsApproval()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();
        var staff2Id = await CreateDepartmentStaffAsync(db, s.Users.DepartmentId);

        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var approver = app.CreateAuthenticatedClient(staff2Id, roles: [AppRoles.DepartmentStaff]);

        var criteriaWithLevels = new RubricCriterionInput[]
        {
            new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }
        };

        var code = $"R_APP_OK_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Approval Success Test", null, criteriaWithLevels)
        {
            ProjectMode = "INTERDISCIPLINARY",
            EvaluationStage = "COLD",
            EvaluationScope = "COMMON"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var approveReq = new ApproveRubricRequest(submitted.ConcurrencyToken, "QD-2026/10/DH-CNTT");
        var approveRes = await approver.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve", approveReq);
        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);
        var approved = (await approveRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Equal("PUBLISHED", approved.Status);
        Assert.True(approved.IsActive);
        Assert.False(approved.CanEdit);
        Assert.Equal("QD-2026/10/DH-CNTT", approved.ApprovalReference);

        await using var verifyDb = database.CreateContext();
        var approvalRow = await verifyDb.RubricApprovals.OrderByDescending(a => a.Id).FirstOrDefaultAsync(a => a.RubricId == created.Id);
        Assert.NotNull(approvalRow);
        Assert.Equal("APPROVED", approvalRow.Decision);
        Assert.Equal(staff2Id, approvalRow.DecidedBy);
        Assert.NotNull(approvalRow.DecidedAt);
        Assert.Equal(s.Users.Staff, approvalRow.SubmittedBy);
    }

    [Fact]
    public async Task ScopedRubric_CallingDirectPublish_Returns409_RequiresApprovalFlow()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var client = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var criteriaWithLevels = new RubricCriterionInput[]
        {
            new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }
        };

        var code = $"R_DIR_PUB_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Direct Publish Test", null, criteriaWithLevels)
        {
            ProjectMode = "INTERDISCIPLINARY",
            EvaluationStage = "COLD",
            EvaluationScope = "COMMON"
        };
        var createRes = await client.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var pubRes = await client.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/publish",
            new ChangeRubricStatusRequest(created.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Conflict, pubRes.StatusCode);

        var body = await pubRes.Content.ReadAsStringAsync();
        Assert.Contains("RUBRIC_REQUIRES_APPROVAL_FLOW", body);
    }

    [Fact]
    public async Task LegacyRubric_WithoutScope_DirectPublish_Returns200()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var client = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var criteria = new RubricCriterionInput[]
        {
            new("Legacy Criterion", "Desc", 100m, 10m, 0, true) { Levels = [] }
        };

        // Legacy rubric: EvaluationScope = null
        var code = $"R_LEG_PUB_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Legacy Rubric", null, criteria)
        {
            EvaluationScope = null
        };
        var createRes = await client.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var pubRes = await client.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/publish",
            new ChangeRubricStatusRequest(created.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, pubRes.StatusCode);
        var published = (await pubRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Equal("PUBLISHED", published.Status);
        Assert.True(published.IsActive);
    }

    [Fact]
    public async Task ScopedRubric_WithProjectMode_EvenIfScopeNull_CallingDirectPublish_Returns409()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var client = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var code = $"R_EVADE_{Guid.NewGuid():N}"[..20];
        await using var db = database.CreateContext();
        var rubric = new M.Rubric
        {
            DepartmentId = s.Users.DepartmentId,
            AcademicSemesterId = s.SemesterId,
            Code = code,
            Name = "Evade Scope Rubric",
            IsActive = false,
            ProjectMode = "INTERDISCIPLINARY",
            EvaluationScope = null,
            CreatedBy = s.Users.Staff,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            RubricCriteria = [
                new() {
                    Criterion = new() { Code = Guid.NewGuid().ToString("N"), Name = "C1", IsActive = true },
                    WeightPercent = 100m, MaxScore = 10m, SortOrder = 0, IsRequired = true
                }
            ]
        };
        db.Rubrics.Add(rubric);
        await db.SaveChangesAsync();
        db.Set<RubricVersion>().Add(new RubricVersion
        {
            RubricId = rubric.Id,
            RootRubricId = rubric.Id,
            VersionNumber = 1,
            Status = RubricStatuses.Draft,
            ConcurrencyToken = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        var getRes = await client.GetAsync($"/api/v1/rubrics/{rubric.Id}");
        var dto = (await getRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var pubRes = await client.PostAsJsonAsync($"/api/v1/rubrics/{rubric.Id}/publish",
            new ChangeRubricStatusRequest(dto.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Conflict, pubRes.StatusCode);
        var body = await pubRes.Content.ReadAsStringAsync();
        Assert.Contains("RUBRIC_REQUIRES_APPROVAL_FLOW", body);
    }

    [Fact]
    public async Task CreateRubric_WithProjectModeOrStage_WithoutScope_Returns422_RubricScopeRequired()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var client = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var criteria = new RubricCriterionInput[]
        {
            new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = [] }
        };

        var code = $"R_EVADE2_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Scope Missing", null, criteria)
        {
            ProjectMode = "INTERDISCIPLINARY",
            EvaluationScope = null
        };
        var createRes = await client.PostAsJsonAsync("/api/v1/rubrics", createReq);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, createRes.StatusCode);
        var body = await createRes.Content.ReadAsStringAsync();
        Assert.Contains("RUBRIC_SCOPE_REQUIRED", body);
    }
}
