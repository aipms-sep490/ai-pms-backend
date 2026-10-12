using System.ComponentModel.DataAnnotations;

namespace AIPMS.Application.Features.Industry;

public sealed class IndustrySettings
{
    public bool Enabled { get; set; }
    public string InvitationUrl { get; set; } = "";
}
public sealed record InviteExpertRequest([Required, EmailAddress, StringLength(255)] string Email,
    [Required, StringLength(255)] string FullName, [Required, StringLength(255)] string Organization);
public sealed record ExpertInvitationDto(long Id, long ExpertId, string Email, string Status, DateTime ExpiresAt);
public sealed record IndustryTokenRequest([Required] string ConcurrencyToken);
public sealed record AssignIndustryRequest(long ExpertId, [Required] string Scope, long? MajorId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record IndustryAssignmentDto(long Id, long ProjectId, string ProjectCode, string ProjectTitle, long ExpertId,
    string Scope, long? MajorId, DateTime StartsAt, DateTime EndsAt, string Status, string ConcurrencyToken,
    IReadOnlyList<string> AllowedActions, bool AdvisoryOnly = true);
public sealed record ShareIndustryFileRequest([Required] string ConcurrencyToken, long DeliverableVersionId, long FileId);
public sealed record IndustryFileDto(long Id, long FileId, long DeliverableVersionId, string FileName, string ContentType,
    long SizeBytes, string Sha256, string DownloadUrl);
public sealed record IndustryFeedbackRequest([Required] string ConcurrencyToken, int ExpectedVersion,
    [Required, StringLength(10000)] string Content);
public sealed record IndustryFeedbackDto(long Id, int Version, int? SupersedesVersion, string Content, long SubmittedBy,
    DateTime SubmittedAt, bool AdvisoryOnly = true);
public sealed record IndustryEnrollmentInput(Guid ChallengeId, [Required, StringLength(16384)] string IdToken,
    [Required, StringLength(64, MinimumLength = 64)] string InvitationToken);
