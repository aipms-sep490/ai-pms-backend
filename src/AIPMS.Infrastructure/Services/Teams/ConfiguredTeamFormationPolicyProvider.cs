using System.Threading.Tasks;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Domain.Teams;
using Microsoft.Extensions.Configuration;

namespace AIPMS.Infrastructure.Services.Teams;

// Temporary adapter until BE-12 provides a persisted, versioned policy implementation.
// No default team sizes: deployment must configure a policy explicitly for the period ID.
internal sealed class ConfiguredTeamFormationPolicyProvider(IConfiguration configuration)
    : ITeamFormationPolicyProvider
{
    public Task<TeamFormationPolicy?> GetAsync(long registrationPeriodId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = $"TeamFormation:Periods:{registrationPeriodId}:";
        if (!int.TryParse(configuration[key + "MinMembers"], out var min)
            || !int.TryParse(configuration[key + "MaxMembers"], out var max)
            || !int.TryParse(configuration[key + "InvitationHours"], out var hours))
            return Task.FromResult<TeamFormationPolicy?>(null);
        var policy = new TeamFormationPolicy(min, max, hours, configuration[key + "Version"] ?? "");
        return Task.FromResult<TeamFormationPolicy?>(policy.IsValid ? policy : null);
    }
}
