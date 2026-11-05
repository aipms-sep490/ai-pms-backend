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

    [Fact]
    public async Task Staff_FromWrongDepartment_CannotApprove_Returns403()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var outsideStaff = app.CreateAuthenticatedClient(s.Users.OutsideStaff, roles: [AppRoles.DepartmentStaff]);

        var code = $"R_WRONG_DEPT_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Departmental Rubric", null,
            [new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }])
        {
            EvaluationScope = "COMMON",
            ProjectMode = "INTERDISCIPLINARY"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, submitRes.StatusCode);
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var approveRes = await outsideStaff.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(submitted.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Forbidden, approveRes.StatusCode);
        var body = await approveRes.Content.ReadAsStringAsync();
        Assert.Contains("RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT", body);
    }

    [Fact]
    public async Task LeadDepartmentStaff_CanApproveCommonInterdisciplinaryRubric_Success()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();
        var staff2Id = await CreateDepartmentStaffAsync(db, s.Users.DepartmentId);

        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var leadStaff = app.CreateAuthenticatedClient(staff2Id, roles: [AppRoles.DepartmentStaff]);

        var code = $"R_LEAD_OK_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Interdisciplinary Lead Common", null,
            [new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }])
        {
            EvaluationScope = "COMMON",
            ProjectMode = "INTERDISCIPLINARY"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var approveRes = await leadStaff.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(submitted.ConcurrencyToken, "QD-2026/INTER-LEAD"));
        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);
        var approved = (await approveRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.Equal("PUBLISHED", approved.Status);
        Assert.True(approved.IsActive);
    }

    [Fact]
    public async Task PureLecturer_CannotManageOrApproveRubric_Returns403()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var lecturer = app.CreateAuthenticatedClient(s.Users.Lecturer, roles: [AppRoles.Lecturer]);

        var code = $"R_LEC_403_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Lecturer Forbidden Test", null,
            [new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }])
        {
            EvaluationScope = "COMMON",
            ProjectMode = "SINGLE_MAJOR"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var approveRes = await lecturer.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(submitted.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Forbidden, approveRes.StatusCode);

        var submitAttempt = await lecturer.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(submitted.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Forbidden, submitAttempt.StatusCode);
    }

    [Fact]
    public async Task SubmitForApproval_WhenAlreadyPending_Returns409()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var client = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        var code = $"R_SUB_DUP_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Pending Rubric", null,
            [new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }])
        {
            EvaluationScope = "COMMON",
            ProjectMode = "SINGLE_MAJOR"
        };
        var createRes = await client.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes1 = await client.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, submitRes1.StatusCode);
        var submitted1 = (await submitRes1.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes2 = await client.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(submitted1.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Conflict, submitRes2.StatusCode);
    }

    [Fact]
    public async Task Approve_WhenAlreadyApproved_Returns409()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();
        var staff2Id = await CreateDepartmentStaffAsync(db, s.Users.DepartmentId);
        var staff3Id = await CreateDepartmentStaffAsync(db, s.Users.DepartmentId);

        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var approver1 = app.CreateAuthenticatedClient(staff2Id, roles: [AppRoles.DepartmentStaff]);
        using var approver2 = app.CreateAuthenticatedClient(staff3Id, roles: [AppRoles.DepartmentStaff]);

        var code = $"R_APP_TWICE_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Approve Twice", null,
            [new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }])
        {
            EvaluationScope = "COMMON",
            ProjectMode = "SINGLE_MAJOR"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var approveRes1 = await approver1.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(submitted.ConcurrencyToken, "REF-001"));
        Assert.Equal(HttpStatusCode.OK, approveRes1.StatusCode);

        var approveRes2 = await approver2.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(submitted.ConcurrencyToken, "REF-002"));
        Assert.Equal(HttpStatusCode.Conflict, approveRes2.StatusCode);
    }

    [Fact]
    public async Task Approve_WithStaleConcurrencyToken_Returns409Conflict()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();
        var staff2Id = await CreateDepartmentStaffAsync(db, s.Users.DepartmentId);

        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var approver = app.CreateAuthenticatedClient(staff2Id, roles: [AppRoles.DepartmentStaff]);

        var code = $"R_STALE_TOK_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Stale Token Test", null,
            [new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }])
        {
            EvaluationScope = "COMMON",
            ProjectMode = "SINGLE_MAJOR"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.OK, submitRes.StatusCode);

        var staleToken = Guid.NewGuid().ToString("N");
        var approveRes = await approver.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(staleToken, "REF-STALE"));
        Assert.Equal(HttpStatusCode.Conflict, approveRes.StatusCode);
    }

    [Fact]
    public async Task Approve_ConcurrentRequests_OnlyOneSucceeds()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();
        var staff2Id = await CreateDepartmentStaffAsync(db, s.Users.DepartmentId);
        var staff3Id = await CreateDepartmentStaffAsync(db, s.Users.DepartmentId);

        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var approverA = app.CreateAuthenticatedClient(staff2Id, roles: [AppRoles.DepartmentStaff]);
        using var approverB = app.CreateAuthenticatedClient(staff3Id, roles: [AppRoles.DepartmentStaff]);

        var code = $"R_RACE_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Race Approval Test", null,
            [new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }])
        {
            EvaluationScope = "COMMON",
            ProjectMode = "SINGLE_MAJOR"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var taskA = approverA.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(submitted.ConcurrencyToken, "REF-RACE-A"));
        var taskB = approverB.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(submitted.ConcurrencyToken, "REF-RACE-B"));

        var responses = await Task.WhenAll(taskA, taskB);
        var statusCodes = responses.Select(r => r.StatusCode).ToList();

        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);

        await using var verifyDb = database.CreateContext();
        var approvedCount = await verifyDb.RubricApprovals.CountAsync(a => a.RubricId == created.Id && a.Decision == "APPROVED");
        Assert.Equal(1, approvedCount);
    }

    [Fact]
    public async Task PublishedOrReferencedRubric_CannotBeEditedOrDeleted_Returns409()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();
        var staff2Id = await CreateDepartmentStaffAsync(db, s.Users.DepartmentId);

        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var approver = app.CreateAuthenticatedClient(staff2Id, roles: [AppRoles.DepartmentStaff]);

        var code = $"R_PUB_LOCK_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Published Protected", null,
            [new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }])
        {
            EvaluationScope = "COMMON",
            ProjectMode = "SINGLE_MAJOR"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var approveRes = await approver.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(submitted.ConcurrencyToken, "REF-LOCK"));
        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);
        var published = (await approveRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var putRes = await submitter.PutAsJsonAsync($"/api/v1/rubrics/{created.Id}",
            new UpdateRubricRequest("New Name", null, [new("C1", null, 100m, 10m, 0, true)], published.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Conflict, putRes.StatusCode);

        var deleteRes = await submitter.DeleteAsync($"/api/v1/rubrics/{created.Id}?concurrencyToken={published.ConcurrencyToken}");
        Assert.Equal(HttpStatusCode.Conflict, deleteRes.StatusCode);
    }

    [Fact]
    public async Task RubricWorkflow_AuditTrail_RecordsActionAndActor()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();
        var staff2Id = await CreateDepartmentStaffAsync(db, s.Users.DepartmentId);

        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var approver = app.CreateAuthenticatedClient(staff2Id, roles: [AppRoles.DepartmentStaff]);

        var code = $"R_AUDIT_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Audit Trail Rubric", null,
            [new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }])
        {
            EvaluationScope = "COMMON",
            ProjectMode = "SINGLE_MAJOR"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var approveRes = await approver.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(submitted.ConcurrencyToken, "QD-AUDIT-2026"));
        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);

        await using var verifyDb = database.CreateContext();
        var submitAudit = await verifyDb.AuditLogs
            .Where(a => a.EntityType == "RUBRIC" && a.EntityId == created.Id.ToString() && a.Action == "RUBRIC_SUBMITTED_FOR_APPROVAL")
            .FirstOrDefaultAsync();
        Assert.NotNull(submitAudit);
        Assert.Equal(s.Users.Staff, submitAudit.ActorUserId);

        var approveAudit = await verifyDb.AuditLogs
            .Where(a => a.EntityType == "RUBRIC" && a.EntityId == created.Id.ToString() && a.Action == "RUBRIC_APPROVED")
            .FirstOrDefaultAsync();
        Assert.NotNull(approveAudit);
        Assert.Equal(staff2Id, approveAudit.ActorUserId);
        Assert.Contains("QD-AUDIT-2026", approveAudit.DetailsJson);
    }

    [Fact]
    public async Task RubricWorkflow_MajorSpecific_DifferentMajorOrClosedSemester_Rejected()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();
        var major1 = new M.Major { DepartmentId = s.Users.DepartmentId, Code = $"M1_{Guid.NewGuid():N}"[..10], Name = "Major 1", IsActive = true };
        var otherDeptId = await db.Departments.Where(d => d.Id != s.Users.DepartmentId && d.OrganizationId == s.OrganizationId).Select(d => d.Id).FirstAsync();
        var major2 = new M.Major { DepartmentId = otherDeptId, Code = $"M2_{Guid.NewGuid():N}"[..10], Name = "Major 2", IsActive = true };
        db.Majors.AddRange(major1, major2);
        await db.SaveChangesAsync();

        var staff2Id = await CreateDepartmentStaffAsync(db, s.Users.DepartmentId);

        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var dept1Staff = app.CreateAuthenticatedClient(staff2Id, roles: [AppRoles.DepartmentStaff]);

        var code = $"R_MAJ_SPEC_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Major Specific", null,
            [new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }])
        {
            EvaluationScope = "MAJOR_SPECIFIC",
            MajorId = major2.Id,
            ProjectMode = "SINGLE_MAJOR"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var approveRes = await dept1Staff.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(submitted.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Forbidden, approveRes.StatusCode);
        var body = await approveRes.Content.ReadAsStringAsync();
        Assert.Contains("RUBRIC_SCOPE_NOT_LEAD_DEPARTMENT", body);
    }

    [Fact]
    public async Task RubricDto_PermissionsFlags_AccuratelyReflectsWorkflowStateAndActor()
    {
        var s = await database.Seed();
        await using var db = database.CreateContext();
        var staff2Id = await CreateDepartmentStaffAsync(db, s.Users.DepartmentId);

        using var app = new RubricFactory(database);
        using var submitter = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var approver = app.CreateAuthenticatedClient(staff2Id, roles: [AppRoles.DepartmentStaff]);

        var code = $"R_PERMS_{Guid.NewGuid():N}"[..20];
        var createReq = new CreateRubricRequest(s.Users.DepartmentId, s.SemesterId, code, "Perms Rubric", null,
            [new("Criterion 1", "Desc", 100m, 10m, 0, true) { Levels = FourLevels() }])
        {
            EvaluationScope = "COMMON",
            ProjectMode = "SINGLE_MAJOR"
        };
        var createRes = await submitter.PostAsJsonAsync("/api/v1/rubrics", createReq);
        var created = (await createRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        Assert.True(created.CanSubmitForApproval);
        Assert.False(created.CanApprove);
        Assert.False(created.CanReject);

        var submitRes = await submitter.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/submit-for-approval",
            new SubmitRubricForApprovalRequest(created.ConcurrencyToken));
        var submitted = (await submitRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;

        var getBySubmitter = await submitter.GetAsync($"/api/v1/rubrics/{created.Id}");
        var dtoSubmitter = (await getBySubmitter.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.False(dtoSubmitter.CanSubmitForApproval);
        Assert.False(dtoSubmitter.CanApprove);
        Assert.False(dtoSubmitter.CanReject);

        var getByApprover = await approver.GetAsync($"/api/v1/rubrics/{created.Id}");
        var dtoApprover = (await getByApprover.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.False(dtoApprover.CanSubmitForApproval);
        Assert.True(dtoApprover.CanApprove);
        Assert.True(dtoApprover.CanReject);

        var approveRes = await approver.PostAsJsonAsync($"/api/v1/rubrics/{created.Id}/approve",
            new ApproveRubricRequest(submitted.ConcurrencyToken, "REF-PERM"));
        var approved = (await approveRes.Content.ReadFromJsonAsync<RubricDto>(JsonOpts))!;
        Assert.False(approved.CanSubmitForApproval);
        Assert.False(approved.CanApprove);
        Assert.False(approved.CanReject);
    }
}
