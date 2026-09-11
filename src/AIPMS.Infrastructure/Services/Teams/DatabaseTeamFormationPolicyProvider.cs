using System.Threading.Tasks;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AIPMS.Infrastructure.Services.Teams;

internal sealed class DatabaseTeamFormationPolicyProvider(
    AipmsDbContext context,
    IConfiguration configuration)
    : ITeamFormationPolicyProvider
{
    public async Task<TeamFormationPolicy?> GetAsync(long registrationPeriodId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var period = await context.ProjectPeriods
            .AsNoTracking()
            .Where(p => p.Id == registrationPeriodId && p.PeriodType == "REGISTRATION")
            .Select(p => new
            {
                p.MinTeamSize,
                p.MaxTeamSize,
                p.MinDistinctMajors,
                p.UpdatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (period is null)
            return null;

        if (!period.MinTeamSize.HasValue && !period.MaxTeamSize.HasValue)
            return null;

        var minMembers = period.MinTeamSize ?? 3;
        var maxMembers = period.MaxTeamSize ?? 5;
        var minDistinctMajors = period.MinDistinctMajors ?? 1;

        var invitationHours = ResolveInvitationHours(registrationPeriodId);
        var version = ResolveVersion(registrationPeriodId, period.UpdatedAt);

        var policy = new TeamFormationPolicy(
            minMembers,
            maxMembers,
            invitationHours,
            version,
            minDistinctMajors);

        return policy.IsValid ? policy : null;
    }

    private int ResolveInvitationHours(long registrationPeriodId)
    {
        var key = $"TeamFormation:Periods:{registrationPeriodId}:InvitationHours";
        if (int.TryParse(configuration[key], out var periodHours) && periodHours is >= 1 and <= 720)
            return periodHours;

        if (int.TryParse(configuration["TeamFormation:DefaultInvitationHours"], out var defaultHours) && defaultHours is >= 1 and <= 720)
            return defaultHours;

        return 24;
    }

    private string ResolveVersion(long registrationPeriodId, DateTime updatedAt)
    {
        var configuredVersion = configuration[$"TeamFormation:Periods:{registrationPeriodId}:Version"];
        return !string.IsNullOrWhiteSpace(configuredVersion)
            ? configuredVersion
            : updatedAt.ToString("O");
    }
}
