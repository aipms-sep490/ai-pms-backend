using AIPMS.Application.Features.AcademicTracking;

namespace AIPMS.Application.Features.AcademicTracking.Abstractions;

public interface IAutoScheduleService
{
    Task<AutoSchedulePreviewDto> Preview(long periodId, AutoSchedulePreviewRequest input, CancellationToken ct);
    Task<AutoScheduleApplyDto> Apply(long periodId, AutoScheduleApplyRequest input, CancellationToken ct);
}
