using AIPMS.AI.Services;
using AIPMS.Application.Features.Projects.Queries;
using AIPMS.Domain.Projects;
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

    [Fact]
    public void FeatureValidators_StayInValidatorsNamespaces()
    {
        var validators = typeof(GetProjectLifecycleQuery).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && !type.IsNested
                && typeof(FluentValidation.IValidator).IsAssignableFrom(type));

        Assert.NotEmpty(validators);
        Assert.All(validators, type => Assert.EndsWith(".Validators", type.Namespace));
    }

    [Fact]
    public void FeatureInterfaces_StayInAbstractionsNamespaces()
    {
        var interfaces = typeof(GetProjectLifecycleQuery).Assembly.GetTypes()
            .Where(type => type.IsInterface && !type.IsNested
                && type.Namespace?.StartsWith("AIPMS.Application.Features.", StringComparison.Ordinal) == true);

        Assert.NotEmpty(interfaces);
        Assert.All(interfaces, type => Assert.EndsWith(".Abstractions", type.Namespace));
    }

    [Fact]
    public void TaskRules_BelongToDomain()
    {
        Assert.Same(typeof(Project).Assembly, typeof(AIPMS.Domain.Tasks.TaskStateMachine).Assembly);
        Assert.Same(typeof(Project).Assembly, typeof(AIPMS.Domain.Tasks.TaskCycleDetector).Assembly);
    }

    [Fact]
    public void TeamResponseDtos_DoNotExposeDomainOrInternalModels()
    {
        var dtos = typeof(GetProjectLifecycleQuery).Assembly.GetTypes()
            .Where(type => type.Namespace == "AIPMS.Application.Features.Teams.DTOs"
                && type.IsPublic);

        Assert.NotEmpty(dtos);
        foreach (var dto in dtos)
        {
            foreach (var property in dto.GetProperties())
            {
                AssertResponseType(property.PropertyType);
            }
        }
    }

    private static void AssertResponseType(Type type)
    {
        if (type.Namespace?.StartsWith("AIPMS.", StringComparison.Ordinal) == true)
            Assert.Equal("AIPMS.Application.Features.Teams.DTOs", type.Namespace);
        if (type.HasElementType)
            AssertResponseType(type.GetElementType()!);
        foreach (var argument in type.GetGenericArguments())
            AssertResponseType(argument);
    }

    private static string[] GetAipmsReferences(System.Reflection.Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith("AIPMS.", StringComparison.Ordinal))
            .OrderBy(name => name)
            .Cast<string>()
            .ToArray();
}
