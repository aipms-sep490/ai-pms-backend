namespace AIPMS.Application.Features.Teams.DTOs;

public sealed record TeamMemberDto(
    long UserId, string FullName, long? MajorId, long? OrganizationId,
    bool IsEligibleStudent, bool IsLeader,
    bool IsProjectQualificationEligible = true,
    string QualificationStatus = "NOT_REQUIRED");
