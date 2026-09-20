using System;
using System.Collections.Generic;
using System.Linq;

namespace AIPMS.Domain.Topics;

public sealed record TopicMajorRequirementRule(long MajorId, int MinMembers, int MaxMembers);

public sealed record TeamMemberEvidence(
    long? MajorId,
    bool IsVerifiedActive = true,
    bool IsFormer = false);

public static class TopicSelectionRules
{
    public const string StatusPublished = "PUBLISHED";
    public const string StatusDraft = "DRAFT";
    public const string StatusClosed = "CLOSED";

    public static IReadOnlyList<string> ValidateSelection(
        string topicStatus,
        string projectStatus,
        long topicPeriodId,
        long teamPeriodId,
        string? topicProjectMode,
        long? topicPrimaryMajorId,
        IReadOnlyList<long> topicMajorIds,
        string? teamProjectMode,
        long? teamPrimaryMajorId,
        IReadOnlyList<long> teamMajorIds)
    {
        var topicRequirements = topicMajorIds?
            .Select(id => new TopicMajorRequirementRule(id, 1, int.MaxValue))
            .ToList() ?? [];

        var memberEvidences = teamMajorIds?
            .Select(id => new TeamMemberEvidence(id, IsVerifiedActive: true, IsFormer: false))
            .ToList() ?? [];

        return ValidateSelection(
            topicStatus,
            projectStatus,
            topicPeriodId,
            teamPeriodId,
            topicProjectMode,
            topicPrimaryMajorId,
            topicRequirements,
            teamProjectMode,
            teamPrimaryMajorId,
            memberEvidences);
    }

    public static IReadOnlyList<string> ValidateSelection(
        string topicStatus,
        string projectStatus,
        long topicPeriodId,
        long teamPeriodId,
        string? topicProjectMode,
        long? topicPrimaryMajorId,
        IReadOnlyList<TopicMajorRequirementRule> topicRequirements,
        string? teamProjectMode,
        long? teamPrimaryMajorId,
        IReadOnlyList<TeamMemberEvidence> memberEvidences)
    {
        var issues = new List<string>();

        // 1. Topic status
        if (!string.Equals(topicStatus, StatusPublished, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("TOPIC_NOT_PUBLISHED");
        }

        // 2. Project editable
        if (!string.Equals(projectStatus, "DRAFT", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(projectStatus, "REVISION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("PROJECT_NOT_EDITABLE");
        }

        // 3. Period window (P1 #1: Fail closed if window/period is missing)
        if (topicPeriodId <= 0 || teamPeriodId <= 0)
        {
            issues.Add("REGISTRATION_WINDOW_UNAVAILABLE");
        }
        else if (topicPeriodId != teamPeriodId)
        {
            issues.Add("TOPIC_PERIOD_MISMATCH");
        }

        // 4. Project mode mismatch
        if (!string.IsNullOrWhiteSpace(topicProjectMode) && !string.IsNullOrWhiteSpace(teamProjectMode))
        {
            if (!string.Equals(topicProjectMode, teamProjectMode, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("PROJECT_MODE_MISMATCH");
            }
        }

        // Extract verified active member evidence
        var verifiedActiveMembers = memberEvidences?
            .Where(m => !m.IsFormer && m.IsVerifiedActive && m.MajorId.HasValue)
            .ToList() ?? [];
        var verifiedMajors = verifiedActiveMembers.Select(m => m.MajorId!.Value).ToList();

        // 5. Single Major topic rules (P1 #2)
        if (string.Equals(topicProjectMode, "SINGLE_MAJOR", StringComparison.OrdinalIgnoreCase))
        {
            var effectiveTopicMajorId = topicPrimaryMajorId ?? topicRequirements.FirstOrDefault()?.MajorId;

            if (teamPrimaryMajorId.HasValue)
            {
                if (effectiveTopicMajorId.HasValue && effectiveTopicMajorId.Value != teamPrimaryMajorId.Value)
                {
                    issues.Add("PRIMARY_MAJOR_MISMATCH");
                }

                if (verifiedMajors.Count > 0)
                {
                    if (verifiedMajors.Distinct().Count() > 1)
                    {
                        issues.Add("PRIMARY_MAJOR_MISMATCH");
                    }
                    else if (effectiveTopicMajorId.HasValue && verifiedMajors.Any(m => m != effectiveTopicMajorId.Value))
                    {
                        issues.Add("PRIMARY_MAJOR_MISMATCH");
                    }
                }
            }
            else
            {
                // Legacy team without explicit AcademicScope / PrimaryMajorId
                if (verifiedMajors.Count == 0)
                {
                    issues.Add("MAJOR_EVIDENCE_UNAVAILABLE");
                }
                else if (verifiedMajors.Distinct().Count() > 1)
                {
                    // Mixed majors in legacy team
                    issues.Add("PRIMARY_MAJOR_MISMATCH");
                }
                else
                {
                    // Single major detected from active members
                    if (effectiveTopicMajorId.HasValue && verifiedMajors.Any(m => m != effectiveTopicMajorId.Value))
                    {
                        issues.Add("PRIMARY_MAJOR_MISMATCH");
                    }
                }
            }
        }
        // 6. Interdisciplinary topic rules (P1 #3)
        else if (string.Equals(topicProjectMode, "INTERDISCIPLINARY", StringComparison.OrdinalIgnoreCase))
        {
            if (verifiedMajors.Count == 0)
            {
                issues.Add("MAJOR_EVIDENCE_UNAVAILABLE");
            }
            else
            {
                var requirementsSatisfied = true;
                foreach (var req in topicRequirements)
                {
                    var count = verifiedMajors.Count(m => m == req.MajorId);
                    if (count == 0)
                    {
                        issues.Add("MAJOR_REQUIREMENT_MISSING");
                        issues.Add("MAJOR_MIN_MEMBERS");
                        issues.Add($"MAJOR_MIN_MEMBERS:{req.MajorId}");
                        requirementsSatisfied = false;
                    }
                    else if (count < req.MinMembers)
                    {
                        issues.Add("MAJOR_MIN_MEMBERS");
                        issues.Add($"MAJOR_MIN_MEMBERS:{req.MajorId}");
                        requirementsSatisfied = false;
                    }

                    if (req.MaxMembers > 0 && count > req.MaxMembers)
                    {
                        issues.Add("MAJOR_MAX_MEMBERS");
                        issues.Add($"MAJOR_MAX_MEMBERS:{req.MajorId}");
                        requirementsSatisfied = false;
                    }
                }

                if (topicRequirements.Count > 0 && verifiedMajors.Any(m => !topicRequirements.Any(r => r.MajorId == m)))
                {
                    issues.Add("MEMBER_MAJOR_NOT_ALLOWED");
                    requirementsSatisfied = false;
                }

                if (!requirementsSatisfied)
                {
                    issues.Add("TOPIC_MAJOR_REQUIREMENTS_UNSATISFIED");
                }
            }
        }

        return issues.Distinct().ToList();
    }
}
