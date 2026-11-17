using AIPMS.Application.Features.AcademicTracking;

namespace AIPMS.Application.Features.AcademicTracking.Abstractions;

public interface IDefenseChangeRequestService
{
    Task<DefenseChangeRequestDto> Create(long sessionId, CreateDefenseChangeRequest input, CancellationToken ct);
    Task<DefenseChangeRequestDto> Decide(long id, DecideDefenseChangeRequest input, CancellationToken ct);
    Task<DefenseChangeRequestDto> Withdraw(long id, WithdrawDefenseChangeRequest input, CancellationToken ct);
    Task<IReadOnlyList<DefenseChangeRequestDto>> ListForSession(long sessionId, CancellationToken ct);
    Task<IReadOnlyList<RoomDto>> RoomsForSession(long sessionId, CancellationToken ct);
    Task<IReadOnlyList<DefenseChangeRequestDto>> List(long periodId, string? status, CancellationToken ct);
}
