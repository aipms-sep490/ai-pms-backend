namespace AIPMS.Application.Features.AcademicKnowledge.Abstractions;

public interface IWorkflowAssistanceService
{
    Task<WorkflowAssistanceDto> GetAsync(long projectId, string mode, long? meetingId, CancellationToken ct);
}
