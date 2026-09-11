using AIPMS.Application.Common.Models;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.DTOs;

public sealed record TeamEligibilityDto(bool CanRegister, bool RosterLocked,
    long? RegistrationPeriodId, string? PolicyVersion, IReadOnlyList<string> Reasons);
