namespace AIPMS.Application.Features.StudentRosters;

public sealed record StudentAccountImportRow(int RowNumber, string StudentCode, string FullName, string Email,
    string? Phone, string? CurriculumCode, string? ClassCode = null, string? SpecializationCode = null);
public sealed record StudentAccountImportPreviewRow(StudentAccountImportRow Account, IReadOnlyList<string> Errors);
public sealed record StudentAccountImportPreview(long MajorId, string MajorName, string DepartmentName,
    IReadOnlyList<StudentAccountImportPreviewRow> Rows, bool CanCommit);
public sealed record StudentImportedCredential(
    string StudentCode,
    string FullName,
    string Email,
    string InitialPassword);

public sealed record StudentAccountImportCommit(
    long MajorId,
    IReadOnlyList<StudentAccountImportRow> Rows,
    bool RequireGoogleSso = false,
    bool MarkProfilesVerified = false);

public sealed record StudentAccountImportResult(
    int Created,
    IReadOnlyList<StudentImportedCredential>? InitialCredentials = null,
    bool ProfilesMarkedVerified = false);
