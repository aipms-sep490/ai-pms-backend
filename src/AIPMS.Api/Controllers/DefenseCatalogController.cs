using AIPMS.Application.Features.AcademicTracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DefenseCatalogController(IDefenseCatalogService catalog) : ControllerBase
{
    [HttpGet("api/v1/rooms")]
    public Task<IReadOnlyList<RoomDto>> Rooms([FromQuery] string? building, [FromQuery] string? status,
        [FromQuery] int? minCapacity, CancellationToken ct) => catalog.Rooms(building, status, minCapacity, ct);

    [HttpPost("api/v1/rooms")]
    public async Task<ActionResult<RoomDto>> CreateRoom(SaveRoomRequest input, CancellationToken ct) =>
        StatusCode(201, await catalog.CreateRoom(input, ct));

    [HttpPut("api/v1/rooms/{id:long}")]
    public Task<RoomDto> UpdateRoom(long id, SaveRoomRequest input, CancellationToken ct) => catalog.UpdateRoom(id, input, ct);

    [HttpDelete("api/v1/rooms/{id:long}")]
    public Task<RoomDto> DeactivateRoom(long id, [FromQuery] string concurrencyToken, CancellationToken ct) =>
        catalog.DeactivateRoom(id, concurrencyToken, ct);

    [HttpGet("api/v1/project-periods/{id:long}/defense-sessions")]
    public Task<IReadOnlyList<PeriodDefenseSessionDto>> PeriodSessions(long id, [FromQuery] DateOnly? date, CancellationToken ct) =>
        catalog.PeriodSessions(id, date, ct);

    [HttpGet("api/v1/project-periods/{id:long}/rooms/availability")]
    public Task<IReadOnlyList<RoomDto>> AvailableRooms(long id, [FromQuery] DateOnly date, [FromQuery] TimeOnly startAt,
        [FromQuery] TimeOnly endAt, CancellationToken ct) => catalog.AvailableRooms(id, date, startAt, endAt, ct);
}
