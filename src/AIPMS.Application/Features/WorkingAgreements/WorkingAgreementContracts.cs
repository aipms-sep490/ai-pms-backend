using AIPMS.Application.Features.ExecutionCapabilities.DTOs;

namespace AIPMS.Application.Features.WorkingAgreements;

public sealed class WorkingAgreementSettings
{
    public bool Enabled { get; set; }
}
public sealed record AgreementSection(string Title, string Body);
public sealed record SaveWorkingAgreementRequest(int ExpectedLatestVersion, string? ConcurrencyToken,
    IReadOnlyList<AgreementSection> Sections);
public sealed record AgreementTokenRequest(long AgreementId, string ConcurrencyToken);
public sealed record AgreementAcceptanceDto(long UserId, DateTime AcceptedAt);
public sealed record WorkingAgreementReadiness(bool Required, long? PolicyVersionId, string Status,
    IReadOnlyList<string> Reasons);
public sealed record WorkingAgreementDto(long Id, long ProjectId, int Version, string Status, string ContentJson,
    long CreatedBy, DateTime CreatedAt, string ConcurrencyToken, bool Required, int MemberCount,
    bool AcceptedByMe, IReadOnlyList<AgreementAcceptanceDto> Acceptances, WorkingAgreementReadiness Readiness,
    IReadOnlyList<ExecutionActionDto> Actions);
