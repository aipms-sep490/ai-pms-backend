using System.Threading.Tasks;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
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

        var period = await (
            from p in context.ProjectPeriods.AsNoTracking()
            where p.Id == registrationPeriodId && p.PeriodType == "REGISTRATION"
            join qp in context.Set<ProjectPeriodQualificationPolicy>().AsNoTracking()
                on p.Id equals qp.ProjectPeriodId into policies
            from qp in policies.DefaultIfEmpty()
            select new
            {
                p.PolicyVersion,
                p.MinTeamSize,
                p.MaxTeamSize,
                p.MinDistinctMajors,
                RequireStudentQualification = qp != null && qp.RequireStudentQualification,
                QualificationType = qp != null ? qp.QualificationType : "CAPSTONE_READINESS",
                RequireCertificate = qp == null || qp.RequireCertificate,
                CheckExpiration = qp == null || qp.CheckExpiration
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
        var version = ResolveVersion(registrationPeriodId, minMembers, maxMembers, minDistinctMajors);
        if (period.PolicyVersion > 1) version += $"-g{period.PolicyVersion}";

        var policy = new TeamFormationPolicy(
            minMembers,
            maxMembers,
            invitationHours,
            version,
            minDistinctMajors,
            period.RequireStudentQualification,
            period.QualificationType,
            period.RequireCertificate,
            period.CheckExpiration);

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
        long registrationPeriodId, int minMembers, int maxMembers, int minDistinctMajors)
    {
        // Keep the legacy fingerprint at governance version 1; append the persisted
        // governance revision in GetAsync so policy edits cannot reuse an old snapshot.
        return $"v-{registrationPeriodId}-{minMembers}-{maxMembers}-{minDistinctMajors}";
    }
}
