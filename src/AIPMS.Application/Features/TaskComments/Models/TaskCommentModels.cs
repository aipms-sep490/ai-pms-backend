using AIPMS.Application.Features.TaskComments.DTOs;
namespace AIPMS.Application.Features.TaskComments.Models;
public sealed record TaskCommentAccess(long TaskId, long ProjectId, string ProjectStatus, bool IsActiveMember, bool IsLeader, bool IsMentor);
public sealed record TaskCommentSearch(long TaskId, int Page, int PageSize);
