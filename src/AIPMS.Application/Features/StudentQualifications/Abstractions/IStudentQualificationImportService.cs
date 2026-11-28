using AIPMS.Application.Features.StudentQualifications.DTOs;

namespace AIPMS.Application.Features.StudentQualifications.Abstractions;

public interface IStudentQualificationImportService
{
    Task<StudentQualificationImportPreview> PreviewAsync(Stream file, string fileName, CancellationToken ct = default);
    Task<StudentQualificationImportResult> CommitAsync(StudentQualificationImportCommit request, CancellationToken ct = default);
}
