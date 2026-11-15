namespace AIPMS.Application.Features.DepartmentDocuments;

public sealed record DepartmentDocumentDto(long Id, long DepartmentId, long MajorId, long? ProjectPeriodId,
    string Code, string Title, int Version, string Status, string FileName, string ContentType,
    long SizeBytes, string Sha256, string ConcurrencyToken, DateTime UploadedAt, DateTime? PublishedAt);

public sealed record ChangeDepartmentDocumentStatusRequest(string ConcurrencyToken);
