namespace AIPMS.Application.Features.Teams.Models;

public sealed record TeamInvitationCandidateScope(long TeamId, long SemesterId, long MajorId,
    long OrganizationId, DateTime Now, IReadOnlyList<long>? AllowedMajorIds = null,
    bool RequireStudentQualification = false,
    string RequiredQualificationType = "CAPSTONE_READINESS",
    bool RequireCertificate = true,
    bool CheckQualificationExpiration = true);

public sealed record TeamInvitationCandidate(long UserId, string FullName, string Email,
    string? StudentCode, long MajorId, string MajorCode, string MajorName,
    long? PendingInvitationId, DateTime? PendingInvitationExpiresAt);
