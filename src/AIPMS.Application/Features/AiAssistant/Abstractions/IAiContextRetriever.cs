using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.AiAssistant.Models;

namespace AIPMS.Application.Features.AiAssistant.Abstractions;

public interface IAiContextRetriever
{
    Task<ProjectBoundedContext> RetrieveProjectContextAsync(
        long projectId,
        string query,
        CancellationToken cancellationToken = default);

    Task<ReportBoundedContext> RetrieveReportContextAsync(
        long projectId,
        long reportId,
        CancellationToken cancellationToken = default);
}
