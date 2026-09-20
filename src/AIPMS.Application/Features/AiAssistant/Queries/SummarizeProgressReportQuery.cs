using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.DTOs;
using MediatR;

namespace AIPMS.Application.Features.AiAssistant.Queries;

public sealed record SummarizeProgressReportQuery(long ProjectId, long ReportId) : IRequest<ReportSummaryDto>;

public sealed class SummarizeProgressReportQueryHandler(IAiAssistantService aiAssistantService)
    : IRequestHandler<SummarizeProgressReportQuery, ReportSummaryDto>
{
    public Task<ReportSummaryDto> Handle(SummarizeProgressReportQuery request, CancellationToken cancellationToken) =>
        aiAssistantService.SummarizeReportAsync(request.ProjectId, request.ReportId, cancellationToken);
}
