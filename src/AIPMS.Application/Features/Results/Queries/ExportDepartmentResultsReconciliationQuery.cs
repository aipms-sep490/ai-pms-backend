using AIPMS.Application.Features.Results.Abstractions;
using AIPMS.Application.Features.StudentRosters;
using MediatR;

namespace AIPMS.Application.Features.Results.Queries;

public sealed record ExportDepartmentResultsReconciliationQuery(
    long? SemesterId,
    long? MajorId,
    string Format,
    string? Search) : IRequest<RosterFile>;

public sealed class ExportDepartmentResultsReconciliationQueryHandler(
    IResultReconciliationExportService service) : IRequestHandler<ExportDepartmentResultsReconciliationQuery, RosterFile>
{
    public Task<RosterFile> Handle(ExportDepartmentResultsReconciliationQuery request, CancellationToken cancellationToken) =>
        service.ExportDepartmentResultsAsync(request.SemesterId, request.MajorId, request.Format, request.Search, cancellationToken);
}
