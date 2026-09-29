using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Validators;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Application.Features.Teams.Services;
using AIPMS.Domain.Teams;

namespace AIPMS.UnitTests.Application;

public sealed class ProjectRequirementsTests
{
    [Theory]
    [InlineData(0, 1, 2, "Work")]
    [InlineData(1, 0, 2, "Work")]
    [InlineData(1, 3, 2, "Work")]
    [InlineData(1, 1, 2, " ")]
    public void Rejects_invalid_requirement(long major, int min, int max, string text) =>
        Assert.Throws<ValidationException>(() => ProjectRequirementsValidation.Validate([new(major, min, max, text)]));

    [Fact]
    public void Rejects_empty_duplicate_or_oversized_input()
    {
        Assert.Throws<ValidationException>(() => ProjectRequirementsValidation.Validate([]));
        Assert.Throws<ValidationException>(() => ProjectRequirementsValidation.Validate([new(1, 1, 2, "A"), new(1, 1, 3, "B")]));
        Assert.Throws<ValidationException>(() => ProjectRequirementsValidation.Validate([new(1, 1, 2, new string('a', 2001))]));
        ProjectRequirementsValidation.Validate([new(1, 1, 2, "A"), new(2, 1, 3, "B")]);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public void Rejects_invalid_pagination(int page, int size) =>
        Assert.Throws<ValidationException>(() => ProjectRequirementsValidation.ValidatePage(page, size));

    [Fact]
    public void Requirement_hash_is_order_independent_but_tracks_quota_responsibility_and_revision()
    {
        var hasher = new TeamEligibilityHasher();
        var now = DateTime.UtcNow;
        ProjectMajorRequirementDto a = new(1, 10, 1, 2, "Engineering", "v1"), b = new(2, 20, 1, 2, "Business", "v2");
        var project = new ProjectContextInput(1, "STUDENT_PROPOSAL", null, "Title", "Problem", "Objectives", "Output", [10, 20], [], [a, b]);
        var input = new TeamEligibilityContextInput(1, 1, 1, "INITIAL", null, "INTERDISCIPLINARY", "v1", "1", [], null, project, new TeamFormationPolicy(2, 5, 24, "v1"));
        string Hash(ProjectContextInput p) => hasher.ComputeHashes(input with { Project = p }, now).ProjectContextHash;
        Assert.Equal(Hash(project), Hash(project with { Requirements = [b, a] }));
        Assert.NotEqual(Hash(project), Hash(project with { Requirements = [a with { MinMembers = 2 }, b] }));
        Assert.NotEqual(Hash(project), Hash(project with { Requirements = [a with { Responsibility = "New work" }, b] }));
        Assert.NotEqual(Hash(project), Hash(project with { Requirements = [a with { ConcurrencyToken = "v3" }, b] }));
        Assert.Equal(Hash(project with { Requirements = null }), Hash(project with { Requirements = [] }));
    }
}
