using AIPMS.AI.Services;
using AIPMS.Application.Features.Projects.Queries.GetProjectLifecycle;
using AIPMS.Domain.Entities;
using AIPMS.Infrastructure.Persistence.Generated;

namespace AIPMS.UnitTests.Architecture;

public sealed class DependencyRuleTests
{
    [Fact]
    public void Domain_DoesNotReferenceOtherAipmsProjects()
    {
        var references = GetAipmsReferences(typeof(Project).Assembly);

        Assert.Empty(references);
    }

    [Fact]
    public void Application_ReferencesOnlyDomain()
    {
        var references = GetAipmsReferences(typeof(GetProjectLifecycleQuery).Assembly);

        Assert.Equal(["AIPMS.Domain"], references);
    }

    [Fact]
    public void Ai_ReferencesOnlyApplication()
    {
        var references = GetAipmsReferences(typeof(RuleBasedProgressAnalysisService).Assembly);

        Assert.Equal(["AIPMS.Application"], references);
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceApiOrAi()
    {
        var references = GetAipmsReferences(typeof(AipmsDbContext).Assembly);

        Assert.All(
            references,
            reference => Assert.Contains(reference, new[] { "AIPMS.Application", "AIPMS.Domain" }));
    }

    private static string[] GetAipmsReferences(System.Reflection.Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith("AIPMS.", StringComparison.Ordinal))
            .OrderBy(name => name)
            .Cast<string>()
            .ToArray();
}
