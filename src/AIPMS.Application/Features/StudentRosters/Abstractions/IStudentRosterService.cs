namespace AIPMS.Application.Features.StudentRosters.Abstractions;

public interface IStudentRosterService
{
    Task<CurriculumPreviewDto> PreviewAsync(Stream file, string fileName, CancellationToken ct);
    Task<CurriculumCommitDto> CommitAsync(CurriculumCommitRequest request, CancellationToken ct);
}
