using System;
using AIPMS.Application.Features.Teams.Models;

namespace AIPMS.Application.Features.Teams.Abstractions;

public interface ITeamEligibilityHasher
{
    TeamEligibilityHashes ComputeHashes(TeamEligibilityContextInput input, DateTime utcNow);
}
