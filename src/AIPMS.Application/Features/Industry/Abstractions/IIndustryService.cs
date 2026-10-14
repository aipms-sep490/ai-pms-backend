using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Auth.DTOs;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Deliverables.DTOs;

namespace AIPMS.Application.Features.Industry.Abstractions;

public interface IIndustryService
{
    Task Cleanup(CancellationToken ct);
    Task<ExpertInvitationDto> Invite(InviteExpertRequest input, CancellationToken ct);
    Task<PagedResult<ExpertInvitationDto>> Invitations(int page, int pageSize, CancellationToken ct);
    Task<ExpertInvitationDto> RevokeInvitation(long id, CancellationToken ct);
    Task<GoogleChallenge> Challenge(string browser, CancellationToken ct);
    Task<LoginResponse> Enroll(IndustryEnrollmentInput input, string browser, CancellationToken ct);
    Task<IndustryAssignmentDto> Assign(long projectId, AssignIndustryRequest input, CancellationToken ct);
    Task<PagedResult<IndustryAssignmentDto>> Assignments(long? projectId, int page, int pageSize, CancellationToken ct);
    Task<IndustryAssignmentDto> Get(long id, CancellationToken ct);
    Task<IndustryAssignmentDto> Revoke(long id, string token, CancellationToken ct);
    Task<IndustryFileDto> Share(long id, ShareIndustryFileRequest input, CancellationToken ct);
    Task<IndustryAssignmentDto> Unshare(long id, long shareId, string token, CancellationToken ct);
    Task<PagedResult<IndustryFileDto>> Evidence(long id, int page, int pageSize, CancellationToken ct);
    Task<FileDownload> Download(long id, long shareId, CancellationToken ct);
    Task<IndustryFeedbackDto> Feedback(long id, IndustryFeedbackRequest input, CancellationToken ct);
    Task<PagedResult<IndustryFeedbackDto>> FeedbackHistory(long id, int page, int pageSize, CancellationToken ct);
}
