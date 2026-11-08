namespace AIPMS.Application.Features.ReviewGates;

public sealed record GatePresence(long UserId, string Name, string Role, bool IsPresent,
    DateTimeOffset? JoinedAt = null, DateTimeOffset? LeftAt = null, int? DurationMinutes = null);
public sealed record GateAgendaStage(string Stage, int DurationMinutes, string Status);
public sealed record GateQaEntry(string Id, string AskerName, string AskerRole, string Question,
    string AnsweredBy, string Evaluation, string? Notes = null);
public sealed record GateRemediationItem(string Id, string Title, string Assignee, DateOnly DueDate,
    bool Required, int? WorkingDaysToComplete = null);
public sealed record SaveGateSessionScriptRequest(string Mode, long? RoomId, string? LiveKitRoomName,
    DateTimeOffset ScheduledStartAt, DateTimeOffset ScheduledEndAt, DateTimeOffset? ActualStartAt,
    DateTimeOffset? ActualEndAt, IReadOnlyList<GatePresence> Presences,
    IReadOnlyList<GateAgendaStage> AgendaStages, IReadOnlyList<GateQaEntry> QaLog,
    IReadOnlyList<GateRemediationItem> RemediationItems, string? GeneralNotes, string Verdict,
    string? SignedBySecretary, string? SignedByChair, string? ConcurrencyToken);
public sealed record GateSessionScriptDto(long Id, long ProjectGateInstanceId, long ProjectId,
    string GateCode, string GateTitle, string Mode, long? RoomId, string? RoomCode,
    string? LiveKitRoomName, string? LiveKitSessionId, DateTimeOffset ScheduledStartAt,
    DateTimeOffset ScheduledEndAt, DateTimeOffset? ActualStartAt, DateTimeOffset? ActualEndAt,
    int? TotalDurationMinutes, IReadOnlyList<GatePresence> Presences,
    IReadOnlyList<GateAgendaStage> AgendaStages, IReadOnlyList<GateQaEntry> QaLog,
    IReadOnlyList<GateRemediationItem> RemediationItems, string? GeneralNotes, string Verdict,
    string? SignedBySecretary, string? SignedByChair, string ConcurrencyToken,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? PublishedAt, long? PublishedBy);
public sealed record LiveKitPresenceParticipant(long UserId, DateTimeOffset JoinedAt,
    DateTimeOffset? LeftAt, int? DurationMinutes);
public sealed record SyncGatePresenceRequest(string SessionId,
    IReadOnlyList<LiveKitPresenceParticipant> Participants, string? ConcurrencyToken);
