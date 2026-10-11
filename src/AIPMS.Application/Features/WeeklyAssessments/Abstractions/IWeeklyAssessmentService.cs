using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.WeeklyAssessments.Abstractions;

public interface IWeeklyAssessmentService
{
    Task<WeeklyAssessmentCalendar> Calendar(long projectId, CancellationToken ct);
    Task<PagedResult<RubricRecord>> AvailableRubrics(long projectId, int page, int pageSize, CancellationToken ct);
    Task<WeeklyAssessmentDto> Save(long projectId, SaveWeeklyAssessmentRequest input, CancellationToken ct);
    Task<WeeklyAssessmentDto> Submit(long id, string token, CancellationToken ct);
    Task<WeeklyAssessmentDto> Get(long id, CancellationToken ct);
    Task<PagedResult<WeeklyAssessmentDto>> History(long projectId, long? studentId, int? week, int page, int pageSize, CancellationToken ct);
}
