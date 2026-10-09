namespace AIPMS.Application.Features.StudentRosters.Abstractions;

public interface ITeamRosterExportService
{
    Task<RosterFile> ExportAsync(RosterExportQuery query, CancellationToken ct);
}
