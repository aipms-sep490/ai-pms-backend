using AIPMS.Application.Common.Models;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.Models;

public sealed record TeamSnapshot(long Id, long SemesterId, string Code, string Name,
    string? Description, string Status, IReadOnlyList<TeamParticipant> Members,
    IReadOnlyList<string> ProjectStatuses);
