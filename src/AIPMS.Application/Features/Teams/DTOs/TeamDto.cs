namespace AIPMS.Application.Features.Teams.DTOs;

public sealed record TeamDto(long Id, long AcademicSemesterId, string Code, string Name,
    string? Description, string Status, IReadOnlyList<TeamMemberDto> Members,
    TeamEligibilityDto Eligibility, TeamAcademicScopeDto? AcademicScope = null);
