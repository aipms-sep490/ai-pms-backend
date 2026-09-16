using System;
using System.Collections.Generic;
using System.Linq;
using AIPMS.Application.Features.Contributions.DTOs;

namespace AIPMS.Application.Features.Contributions.Services;

public static class ContributionScoring
{
    public static ContributionSummaryDto Summarize(IReadOnlyList<ContributionMemberDto> members)
    {
        var values = members.Select(m => m.ActivityScore).ToArray();
        if (values.Length < 2 || values.Sum() < 3)
            return new("INSUFFICIENT_DATA", null, members);
        var average = values.Average();
        return new("SUFFICIENT", values.Select(v => Math.Pow(v - average, 2)).Average(), members);
    }
}
