using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIPMS.Application.Features.Projects.Commands;
using AIPMS.Application.Features.Projects.Queries;
using AIPMS.Application.Features.Projects.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class ProjectValidatorTests
{
    // ── CreateProjectDraftCommandValidator ──────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CreateDraft_EmptyOrWhitespaceTitle_ShouldFail(string? title)
    {
        var validator = new CreateProjectDraftCommandValidator();
        var cmd = MakeCreate(title: title ?? "");
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateDraft_TitleExceeds500Chars_ShouldFail()
    {
        var validator = new CreateProjectDraftCommandValidator();
        var cmd = MakeCreate(title: new string('A', 501));
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateDraft_EmptyDomain_ShouldFail()
    {
        var validator = new CreateProjectDraftCommandValidator();
        var cmd = MakeCreate(domain: "");
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Domain);
    }

    [Fact]
    public void CreateDraft_DuplicateMajorIds_ShouldFail()
    {
        var validator = new CreateProjectDraftCommandValidator();
        var cmd = MakeCreate(majorIds: [1, 1]);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.RequiredMajorIds);
    }

    [Fact]
    public void CreateDraft_ZeroMajorId_ShouldFail()
    {
        var validator = new CreateProjectDraftCommandValidator();
        var cmd = MakeCreate(majorIds: [0]);
        var result = validator.TestValidate(cmd);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateDraft_DuplicateTechnologies_ShouldFail()
    {
        var validator = new CreateProjectDraftCommandValidator();
        var cmd = MakeCreate(technologies: ["react", "REACT"]);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Technologies);
    }

    [Fact]
    public void CreateDraft_TechnologyTagTooLong_ShouldFail()
    {
        var validator = new CreateProjectDraftCommandValidator();
        var cmd = MakeCreate(technologies: [new string('T', 101)]);
        var result = validator.TestValidate(cmd);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateDraft_ValidCommand_ShouldPass()
    {
        var validator = new CreateProjectDraftCommandValidator();
        var cmd = MakeCreate();
        var result = validator.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── UpdateProjectDraftCommandValidator ──────────────────────────────────

    [Fact]
    public void UpdateDraft_InvalidConcurrencyToken_ShouldFail()
    {
        var validator = new UpdateProjectDraftCommandValidator();
        var cmd = MakeUpdate(concurrencyToken: "not-base64!!!");
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.ConcurrencyToken);
    }

    [Fact]
    public void UpdateDraft_ZeroProjectId_ShouldFail()
    {
        var validator = new UpdateProjectDraftCommandValidator();
        var cmd = MakeUpdate(projectId: 0);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.ProjectId);
    }

    [Fact]
    public void UpdateDraft_DuplicateKeywords_ShouldFail()
    {
        var validator = new UpdateProjectDraftCommandValidator();
        var cmd = MakeUpdate(keywords: ["AI", "ai"]);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Keywords);
    }

    // ── RejectProjectCommandValidator ──────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectProject_EmptyOrWhitespaceReason_ShouldFail(string reason)
    {
        var validator = new RejectProjectCommandValidator();
        var cmd = new RejectProjectCommand(1, ValidToken(), reason);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void RejectProject_ReasonExceeds1000Chars_ShouldFail()
    {
        var validator = new RejectProjectCommandValidator();
        var cmd = new RejectProjectCommand(1, ValidToken(), new string('R', 1001));
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void RejectProject_ValidReason_ShouldPass()
    {
        var validator = new RejectProjectCommandValidator();
        var cmd = new RejectProjectCommand(1, ValidToken(), "Incomplete scope definition.");
        var result = validator.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── RequestProjectRevisionCommandValidator ─────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestRevision_EmptyOrWhitespaceReason_ShouldFail(string reason)
    {
        var validator = new RequestProjectRevisionCommandValidator();
        var cmd = new RequestProjectRevisionCommand(1, ValidToken(), reason);
        var result = validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void RequestRevision_ValidReason_ShouldPass()
    {
        var validator = new RequestProjectRevisionCommandValidator();
        var cmd = new RequestProjectRevisionCommand(1, ValidToken(), "Please clarify objectives.");
        var result = validator.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── GetProjectsQueryValidator ──────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetProjects_InvalidPage_ShouldFail(int page)
    {
        var validator = new GetProjectsQueryValidator();
        var query = new GetProjectsQuery(null, null, null, null, null, null, page, 10);
        var result = validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void GetProjects_InvalidPageSize_ShouldFail(int pageSize)
    {
        var validator = new GetProjectsQueryValidator();
        var query = new GetProjectsQuery(null, null, null, null, null, null, 1, pageSize);
        var result = validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void GetProjects_ValidPagination_ShouldPass()
    {
        var validator = new GetProjectsQueryValidator();
        var query = new GetProjectsQuery(null, null, null, null, null, null, 1, 50);
        var result = validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── GetProjectByIdQueryValidator ──────────────────────────────────────

    [Fact]
    public void GetProjectById_ZeroId_ShouldFail()
    {
        var validator = new GetProjectByIdQueryValidator();
        var result = validator.TestValidate(new GetProjectByIdQuery(0));
        result.ShouldHaveValidationErrorFor(x => x.Id);
    }

    [Fact]
    public void GetProjectById_ValidId_ShouldPass()
    {
        var validator = new GetProjectByIdQueryValidator();
        var result = validator.TestValidate(new GetProjectByIdQuery(1));
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static CreateProjectDraftCommand MakeCreate(
        string? title = "My Project",
        string? domain = "Software Engineering",
        IReadOnlyList<long>? majorIds = null,
        IReadOnlyList<string>? technologies = null,
        IReadOnlyList<string>? keywords = null) =>
        new(
            title ?? "My Project",
            "A description",
            "Objectives",
            "Problem",
            "Output",
            majorIds ?? [301L],
            domain ?? "Software Engineering",
            technologies ?? ["React", ".NET"],
            keywords ?? ["AI", "PMS"]);

    private static UpdateProjectDraftCommand MakeUpdate(
        long projectId = 1,
        string? concurrencyToken = null,
        string? title = "Updated Title",
        string? domain = "Engineering",
        IReadOnlyList<long>? majorIds = null,
        IReadOnlyList<string>? technologies = null,
        IReadOnlyList<string>? keywords = null) =>
        new(
            projectId,
            concurrencyToken ?? ValidToken(),
            title ?? "Updated Title",
            "Description",
            "Objectives",
            "Problem",
            "Output",
            majorIds ?? [301L],
            domain ?? "Engineering",
            technologies ?? ["React"],
            keywords ?? keywords ?? ["AI"]);

    /// <summary>Produces a valid 8-byte Base64 token matching the validator rule.</summary>
    private static string ValidToken()
    {
        var bytes = new byte[8];
        new System.Random(42).NextBytes(bytes);
        return System.Convert.ToBase64String(bytes);
    }
}
