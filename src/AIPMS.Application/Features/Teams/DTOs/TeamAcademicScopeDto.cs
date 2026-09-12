using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.DTOs;

public sealed record MajorRequirementDto(long MajorId, int MinMembers, int MaxMembers, string Responsibility);

public sealed record TeamAcademicScopeDto(string ProjectMode, long? PrimaryMajorId,
    long LeadDepartmentId, IReadOnlyList<MajorRequirementDto> Requirements, Guid ConcurrencyToken)
{
    public static TeamAcademicScopeDto FromScope(TeamAcademicScope scope) => new(scope.ProjectMode,
        scope.PrimaryMajorId, scope.LeadDepartmentId, scope.Requirements.Select(r =>
            new MajorRequirementDto(r.MajorId, r.MinMembers, r.MaxMembers, r.Responsibility)).ToArray(), scope.ConcurrencyToken);
}
