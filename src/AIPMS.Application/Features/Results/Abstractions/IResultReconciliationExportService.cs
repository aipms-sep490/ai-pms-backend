using AIPMS.Application.Features.StudentRosters;

namespace AIPMS.Application.Features.Results.Abstractions;

public interface IResultReconciliationExportService
{
    Task<RosterFile> ExportDepartmentResultsAsync(
        long? semesterId,
        long? majorId,
        string format,
        string? search,
        CancellationToken ct = default);
}
