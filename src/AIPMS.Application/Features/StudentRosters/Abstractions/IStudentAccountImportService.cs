namespace AIPMS.Application.Features.StudentRosters.Abstractions;

public interface IStudentAccountImportService
{
    Task<StudentAccountImportPreview> PreviewAsync(Stream file, string fileName, long majorId, CancellationToken ct);
    Task<StudentAccountImportResult> CommitAsync(StudentAccountImportCommit request, CancellationToken ct);
}
