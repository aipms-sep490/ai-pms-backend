using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.DTOs;

public sealed record TeamAcademicScopeRequest(string ProjectMode, long? PrimaryMajorId,
    long LeadDepartmentId, IReadOnlyList<MajorRequirementDto> Requirements, Guid? ConcurrencyToken = null)
{
    public TeamAcademicScope ToScope() => new(ProjectMode, PrimaryMajorId, LeadDepartmentId, Requirements.Select(r => new MajorRequirement(r.MajorId, r.MinMembers, r.MaxMembers, r.Responsibility)).ToArray(), Guid.Empty);
}
