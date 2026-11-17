namespace AIPMS.Application.Features.AcademicTracking.Abstractions;

public interface IReviewGate3ScheduleService
{
    Task<ReviewGate3SchedulePreviewDto> Preview(long periodId, AutoSchedulePreviewRequest input, CancellationToken ct);
    Task<IReadOnlyList<ReviewGate3ScheduleDto>> Apply(long periodId, ReviewGate3ScheduleApplyRequest input, CancellationToken ct);
    Task<IReadOnlyList<ReviewGate3ScheduleDto>> List(long periodId, CancellationToken ct);
    Task<ReviewGate3ScheduleDto> Change(long periodId, long gateInstanceId, ReviewGate3ScheduleChangeRequest input, CancellationToken ct);
}
