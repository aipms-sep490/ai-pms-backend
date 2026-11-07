namespace AIPMS.Application.Features.AcademicTracking;

public sealed record RoomDto(long Id, string Code, string Building, int Floor, int Capacity, string Status, string ConcurrencyToken);
public sealed record SaveRoomRequest(string Code, string Building, int Floor, int Capacity, string Status, string? ConcurrencyToken = null);
public sealed record PeriodDefenseSessionDto(long Id, long ProjectId, string ProjectCode, string ProjectTitle,
    long CommitteeId, string CommitteeName, long? RoomId, string RoomCode, int? SlotNumber,
    DateTimeOffset StartAt, DateTimeOffset EndAt, string Status, string ConcurrencyToken, bool CanCancel);
