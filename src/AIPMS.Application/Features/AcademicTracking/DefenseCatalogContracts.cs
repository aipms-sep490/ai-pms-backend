namespace AIPMS.Application.Features.AcademicTracking;

public sealed record RoomDto(long Id, string Code, string Building, int Floor, int Capacity, string Status, string ConcurrencyToken);
public sealed record SaveRoomRequest(string Code, string Building, int Floor, int Capacity, string Status, string? ConcurrencyToken = null);
public sealed record PeriodDefenseSessionDto(long Id, long ProjectId, string ProjectCode, string ProjectTitle,
    long CommitteeId, string CommitteeName, long? RoomId, string RoomCode, int? SlotNumber,
    DateTimeOffset StartAt, DateTimeOffset EndAt, string Status, string ConcurrencyToken, bool CanCancel);

public interface IDefenseCatalogService
{
    Task<IReadOnlyList<RoomDto>> Rooms(string? building, string? status, int? minCapacity, CancellationToken ct);
    Task<RoomDto> CreateRoom(SaveRoomRequest input, CancellationToken ct);
    Task<RoomDto> UpdateRoom(long id, SaveRoomRequest input, CancellationToken ct);
    Task<RoomDto> DeactivateRoom(long id, string concurrencyToken, CancellationToken ct);
    Task<IReadOnlyList<PeriodDefenseSessionDto>> PeriodSessions(long periodId, DateOnly? date, CancellationToken ct);
    Task<IReadOnlyList<RoomDto>> AvailableRooms(long periodId, DateOnly date, TimeOnly startAt, TimeOnly endAt, CancellationToken ct);
}
