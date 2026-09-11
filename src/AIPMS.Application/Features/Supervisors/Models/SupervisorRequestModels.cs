namespace AIPMS.Application.Features.Supervisors.Models;

public sealed record SupervisorRequestModel(long Id, long ProjectId, long SupervisorProfileId,
    long SupervisorUserId, long RequestedBy, string Status, string? RequestMessage,
    string? ResponseMessage, DateTime RequestedAt, DateTime? RespondedAt, long? AssignmentId);

public sealed record SupervisorWorkload(int? ProfileLimit, int ActiveProjects, int SemesterActiveProjects);

public sealed record SupervisorRequestSearch(long? ProjectId, long? SupervisorUserId,
    string? Status, int Page, int PageSize);
