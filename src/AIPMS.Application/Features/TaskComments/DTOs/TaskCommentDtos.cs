namespace AIPMS.Application.Features.TaskComments.DTOs;
public sealed record TaskCommentDto(long Id, long TaskId, long AuthorId, string AuthorName, string Content, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record CreateTaskCommentRequest(string Content);
