namespace AIPMS.Application.Features.AcademicTracking;

public sealed record CreateDefenseChangeRequest(long DesiredRoomId, DateTimeOffset DesiredStartAt,
    DateTimeOffset DesiredEndAt, string ReasonCategory, string ReasonDetails, string? EvidenceFileUrl);
public sealed record DecideDefenseChangeRequest(string Decision, string? ReviewNote, string ConcurrencyToken);
public sealed record WithdrawDefenseChangeRequest(string ConcurrencyToken);
public sealed record DefenseChangeRequestDto(long Id, long DefenseSessionId, long RequesterId,
    long CurrentRoomId, DateTimeOffset CurrentStartAt, DateTimeOffset CurrentEndAt,
    long DesiredRoomId, DateTimeOffset DesiredStartAt, DateTimeOffset DesiredEndAt,
    string ReasonCategory, string ReasonDetails, string? EvidenceFileUrl, string Status,
    long? ReviewerId, string? ReviewNote, DateTimeOffset? ReviewedAt, string ConcurrencyToken,
    DateTimeOffset CreatedAt);

public interface IDefenseChangeRequestService
{
    Task<DefenseChangeRequestDto> Create(long sessionId, CreateDefenseChangeRequest input, CancellationToken ct);
    Task<DefenseChangeRequestDto> Decide(long id, DecideDefenseChangeRequest input, CancellationToken ct);
    Task<DefenseChangeRequestDto> Withdraw(long id, WithdrawDefenseChangeRequest input, CancellationToken ct);
    Task<IReadOnlyList<DefenseChangeRequestDto>> List(long periodId, string? status, CancellationToken ct);
}
