namespace AIPMS.Application.Features.StudentRosters;

public sealed record CurriculumPreviewRow(int RowNumber, string StudentCode, long? UserId, string? FullName,
    string? CurrentCurriculumCode, string? CurriculumCode, string? ExpectedConcurrencyToken, string Status,
    IReadOnlyList<string> Errors);
public sealed record CurriculumPreviewDto(IReadOnlyList<CurriculumPreviewRow> Rows, bool CanCommit);
public sealed record CurriculumUpdate(long UserId, string StudentCode, string CurriculumCode, string ExpectedConcurrencyToken);
public sealed record CurriculumCommitRequest(IReadOnlyList<CurriculumUpdate> Rows);
public sealed record CurriculumCommitDto(int Updated, int Unchanged);

