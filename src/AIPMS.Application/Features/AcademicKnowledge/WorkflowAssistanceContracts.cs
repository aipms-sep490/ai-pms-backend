namespace AIPMS.Application.Features.AcademicKnowledge;

public sealed record WorkflowAssistanceItem(string Code, string Status, string Title, string Detail,
    string Owner, string Source, string? Target = null, long? ResourceId = null);
public sealed record WorkflowMeetingOption(long Id, string Title, DateTime StartAt);
public sealed record WorkflowAssistanceDto(long ProjectId, string Mode, DateTime GeneratedAt, string Status,
    string Summary, IReadOnlyList<WorkflowAssistanceItem> Items, IReadOnlyList<string> Limitations,
    AssistantDraftDto? Draft = null, IReadOnlyList<WorkflowMeetingOption>? Meetings = null);

public interface IWorkflowAssistanceService
{
    Task<WorkflowAssistanceDto> GetAsync(long projectId, string mode, long? meetingId, CancellationToken ct);
}
