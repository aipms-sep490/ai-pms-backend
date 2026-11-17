using AIPMS.Application.Features.AcademicTracking;

namespace AIPMS.Application.Features.AcademicTracking.Abstractions;

public interface IDefenseCatalogService
{
    Task<IReadOnlyList<RoomDto>> Rooms(string? building, string? status, int? minCapacity, CancellationToken ct);
    Task<RoomDto> CreateRoom(SaveRoomRequest input, CancellationToken ct);
    Task<RoomDto> UpdateRoom(long id, SaveRoomRequest input, CancellationToken ct);
    Task<RoomDto> DeactivateRoom(long id, string concurrencyToken, CancellationToken ct);
    Task<IReadOnlyList<PeriodDefenseSessionDto>> PeriodSessions(long periodId, DateOnly? date, CancellationToken ct);
    Task<IReadOnlyList<RoomDto>> AvailableRooms(long periodId, DateOnly date, TimeOnly startAt, TimeOnly endAt, CancellationToken ct);
}
