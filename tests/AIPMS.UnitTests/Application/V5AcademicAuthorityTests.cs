using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;

namespace AIPMS.UnitTests.Application;

public sealed class V5AcademicAuthorityTests
{
    private static readonly EvaluationProject Project = new(1, 1, 1, "FINAL_SUBMISSION", true,
        [4, 99], 99, "FROZEN_REGISTRATION_SNAPSHOT");

    [Theory]
    [InlineData(false, true, 99, true, true, true)]
    [InlineData(false, true, 4, false, false, true)]
    [InlineData(true, false, 99, false, false, false)]
    [InlineData(false, false, 99, false, false, false)]
    [InlineData(false, true, 7, false, false, false)]
    public void Authority_uses_frozen_lead_and_component_scope(bool admin, bool staff, long department,
        bool lead, bool managesLeadComponent, bool managesParticipatingComponent)
    {
        var actor = new EvaluationActor(1, department, admin, staff, false);
        Assert.Equal(lead, AcademicAuthority.IsLead(actor, Project));
        Assert.Equal(managesLeadComponent, AcademicAuthority.ManagesComponent(actor, Project, 99));
        Assert.Equal(managesParticipatingComponent, AcademicAuthority.ManagesComponent(actor, Project, 4));
        Assert.False(AcademicAuthority.ManagesComponent(actor, Project, 7));
    }

    [Theory]
    [InlineData("UNKNOWN", true)]
    [InlineData("CURRENT_CONFIGURATION", true)]
    [InlineData("FROZEN_REGISTRATION_SNAPSHOT", false)]
    public void Unknown_or_inactive_scope_never_grants_mutation(string provenance, bool active)
    {
        var actor = new EvaluationActor(1, 99, true, true, true);
        var invalid = Project with { ScopeProvenance = provenance, ActiveScope = active };
        Assert.False(AcademicAuthority.IsLead(actor, invalid));
        Assert.False(AcademicAuthority.ManagesComponent(actor, invalid, 4));
    }
}
