using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Application.Features.WorkingAgreements;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal static class WorkingAgreementGuard
{
    internal sealed record RosterMember(long UserId, long TeamMemberId);
    internal static async Task<RosterMember[]> Roster(AipmsDbContext db, long projectId, CancellationToken ct)
    {
        var teamId = await db.Projects.Where(p => p.Id == projectId).Select(p => p.TeamId).SingleAsync(ct);
        var members = db.Database.CurrentTransaction is null ? db.TeamMembers.AsNoTracking()
            : db.TeamMembers.FromSqlInterpolated($"SELECT * FROM dbo.team_members WITH (UPDLOCK,HOLDLOCK) WHERE team_id={teamId}").AsNoTracking();
        return await members.Where(m => m.TeamId == teamId && m.LeftAt == null)
            .OrderBy(m => m.UserId).Select(m => new RosterMember(m.UserId, m.Id)).ToArrayAsync(ct);
    }
    internal static bool Matches(WorkingAgreement agreement, RosterMember[] roster) => agreement.Members
        .Select(m => new RosterMember(m.UserId, m.TeamMemberId)).OrderBy(m => m.UserId).SequenceEqual(roster);
    internal static async Task EnsureActivation(AipmsDbContext db, long projectId, CancellationToken ct)
    {
        var readiness = await Read(db, projectId, ct);
        if (readiness.Required && readiness.Status != "READY")
            throw new AIPMS.Domain.Exceptions.DomainException("WORKING_AGREEMENT_INCOMPLETE: All current members must accept the active agreement before activation.");
    }

    internal static async Task<WorkingAgreementReadiness> Read(AipmsDbContext db, long projectId, CancellationToken ct)
    {
        // Bind to the policy captured for registration, never today's editable period.
        var snapshot = await db.Set<ProjectRegistrationSnapshot>().Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Id).Select(s => new { s.Id, s.SnapshotJson }).FirstOrDefaultAsync(ct);
        var policy = snapshot is not null ? await (from usage in db.Set<PeriodPolicyUsage>()
            join version in db.Set<PeriodPolicyVersion>() on usage.PolicyVersionId equals version.Id
            where usage.EntityType == "PROJECT_SUBMISSION" && usage.EntityId == snapshot.Id
            select version).SingleOrDefaultAsync(ct) : null;
        var required = policy is not null && JsonSerializer.Deserialize<PeriodPolicyFields>(policy.SnapshotJson)!.RequireWorkingAgreement;
        var row = await db.Set<WorkingAgreement>().Include(a => a.Members)
            .SingleOrDefaultAsync(a => a.ProjectId == projectId && a.Status == "ACTIVE", ct);
        var roster = await Roster(db, projectId, ct);
        var reasons = new List<string>();
        if (required && ProjectAcademicScopeReader.ParseEvidence(snapshot!.SnapshotJson) is null) reasons.Add("ACADEMIC_SCOPE_UNKNOWN");
        if (required && policy!.Status != "LOCKED") reasons.Add("WORKING_AGREEMENT_POLICY_NOT_LOCKED");
        if (row is null) reasons.Add("WORKING_AGREEMENT_MISSING");
        else if (!Matches(row, roster)) reasons.Add("WORKING_AGREEMENT_ROSTER_CHANGED");
        else if (roster.Length == 0 || row.Members.Any(m => !m.AcceptedAt.HasValue)) reasons.Add("WORKING_AGREEMENT_INCOMPLETE");
        return new(required, policy?.Id, reasons.Count == 0 ? "READY" : "AWAITING_ACCEPTANCE", reasons);
    }
}
