namespace AIPMS.Application.Features.StudentRosters;

public sealed record StudentAccountImportRow(int RowNumber, string StudentCode, string FullName, string Email,
    string? Phone, string? CurriculumCode);
public sealed record StudentAccountImportPreviewRow(StudentAccountImportRow Account, IReadOnlyList<string> Errors);
public sealed record StudentAccountImportPreview(long MajorId, string MajorName, string DepartmentName,
    IReadOnlyList<StudentAccountImportPreviewRow> Rows, bool CanCommit);
public sealed record StudentAccountImportCommit(long MajorId, IReadOnlyList<StudentAccountImportRow> Rows);
public sealed record StudentAccountImportResult(int Created);
