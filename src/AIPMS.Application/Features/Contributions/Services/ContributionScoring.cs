using System;
using System.Collections.Generic;
using System.Linq;
using AIPMS.Application.Features.Contributions.DTOs;

namespace AIPMS.Application.Features.Contributions.Services;

public static class ContributionScoring
{
    public const string RuleVersion = "activity-v2";
    // Three credited source events and at least two members are required for a team comparison.
    public const int MinimumActivity = 3;
    public static ContributionSummaryDto Summarize(IReadOnlyList<ContributionMemberDto> members)
    {
        var values = members.Select(m => m.ActivityScore).ToArray();
        if (values.Length < 2 || values.Sum() < MinimumActivity)
            return new("INSUFFICIENT_DATA", null, members, TotalCount: members.Count);
        var average = values.Average();
        return new("SUFFICIENT", values.Select(v => Math.Pow(v - average, 2)).Average(), members, TotalCount: members.Count);
    }

    public static ContributionSummaryDto Page(ContributionSummaryDto summary, int page, int pageSize) => summary with
    {
        Members = summary.Members.OrderBy(m => m.UserId).Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
        Page = page, PageSize = pageSize, TotalCount = summary.Members.Count
    };
}
