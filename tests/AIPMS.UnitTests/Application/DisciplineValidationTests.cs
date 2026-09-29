using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Disciplines.DTOs;
using AIPMS.Application.Features.Disciplines.Validators;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Application.Features.Teams.Services;
using AIPMS.Domain.Teams;

namespace AIPMS.UnitTests.Application;

public sealed class DisciplineValidationTests
{
    [Fact]
    public void Responsibility_validation_rejects_blank_duplicate_order_and_long_content()
    {
        Assert.Throws<ValidationException>(() => DisciplineValidation.Responsibilities([new(" ", 0)]));
        Assert.Throws<ValidationException>(() => DisciplineValidation.Responsibilities([new("A", 0), new("B", 0)]));
        Assert.Throws<ValidationException>(() => DisciplineValidation.Responsibilities([new(new string('a', 2001), 0)]));
        Assert.Throws<ValidationException>(() => DisciplineValidation.Responsibilities([new("A", -1)]));
        DisciplineValidation.Responsibilities([]);
    }
    [Fact]
    public void Disciplines_require_distinct_majors_and_one_primary_for_interdisciplinary()
    {
        Assert.Throws<ValidationException>(() => DisciplineValidation.Disciplines([], true));
        Assert.Throws<ValidationException>(() => DisciplineValidation.Disciplines([new(1, "SUPPORTING")], true));
        Assert.Throws<ValidationException>(() => DisciplineValidation.Disciplines([new(1, "PRIMARY"), new(2, "PRIMARY")], true));
        Assert.Throws<ValidationException>(() => DisciplineValidation.Disciplines([new(1, "PRIMARY"), new(1, "SUPPORTING")], true));
        DisciplineValidation.Disciplines([new(1, "PRIMARY"), new(2, "SUPPORTING")], true);
        DisciplineValidation.Disciplines([], false);
    }
    [Theory]
    [InlineData("FILE", -1, 1)]
    [InlineData("FILE", 1, -1)]
    [InlineData("OTHER", 1, 1)]
    public void Evidence_validates_source_and_identifiers(string type, long source, long major) =>
        Assert.Throws<ValidationException>(() => DisciplineValidation.Evidence(new(type, source, major)));

    [Fact]
    public void Responsibility_revision_changes_scope_fingerprint_without_changing_legacy_hash()
    {
        var hasher = new TeamEligibilityHasher(); var now = DateTime.UtcNow;
        var scope = new AcademicScopeInput(1, "SINGLE_MAJOR", 1, 1, [new(1, 1, 3, "Work")]);
        var input = new TeamEligibilityContextInput(1, 1, null, "FORMATION", null, "SINGLE_MAJOR", "v1", "1", [], scope, null, new TeamFormationPolicy(1, 3, 24, "v1"));
        var legacy = hasher.ComputeHashes(input, now).AcademicScopeHash;
        Assert.Equal(legacy, hasher.ComputeHashes(input with { Scope = scope with { ResponsibilityVersion = null } }, now).AcademicScopeHash);
        var revision = scope with { ResponsibilityVersion = Guid.NewGuid() };
        var changed = hasher.ComputeHashes(input with { Scope = revision }, now).AcademicScopeHash;
        Assert.NotEqual(legacy, changed);
        Assert.NotEqual(changed, hasher.ComputeHashes(input with { Scope = revision with { ResponsibilityVersion = Guid.NewGuid() } }, now).AcademicScopeHash);
    }
}
