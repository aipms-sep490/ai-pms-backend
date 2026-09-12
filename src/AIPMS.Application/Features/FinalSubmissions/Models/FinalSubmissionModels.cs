using AIPMS.Application.Features.Deliverables.DTOs;

namespace AIPMS.Application.Features.FinalSubmissions.Models;

public sealed record FinalDraftProject(long Id, long SemesterId, string Status,
    bool AcademicScopeActive, bool IsMember, bool IsLeader);

public sealed record FinalDraftPeriod(long Id, long SemesterId, string Name, string Type,
    string Status, DateTime StartAt, DateTime EndAt, bool SemesterOpen);

public sealed record FinalDraftRecord(long Id, long ProjectId, long ProjectPeriodId, string? Notes,
    long CreatedBy, long UpdatedBy, DateTime CreatedAt, DateTime UpdatedAt, string ConcurrencyToken,
    IReadOnlyList<long> VersionIds);

public sealed record FinalDraftVersion(long Id, long ProjectId, long DeliverableId, string Title,
    int VersionNumber, string Status, bool FilesValid, IReadOnlyList<ProjectFileDto> Files);
