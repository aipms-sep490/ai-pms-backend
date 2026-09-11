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
        var version = ResolveVersion(registrationPeriodId, minMembers, maxMembers, minDistinctMajors, period.UpdatedAt);

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

    private static string ResolveVersion(
        long registrationPeriodId, int minMembers, int maxMembers, int minDistinctMajors, DateTime updatedAt)
    {
        // Deterministic BE-12 effective policy fingerprint.
        // Derived strictly from effective DB policy values and UpdatedAt to ensure any policy mutation produces a distinct version.
        // Configured labels (e.g. TeamFormation:Periods:{id}:Version) are NOT used to override, preventing stale version masking.
        // InvitationHours is Teams-owned and excluded to keep BE-12 policy versioning isolated to BE-12 owned attributes.
        return $"v-{registrationPeriodId}-{minMembers}-{maxMembers}-{minDistinctMajors}-{updatedAt:yyyyMMddHHmmssfff}";
    }
}
