using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.Models;

namespace AIPMS.Application.Features.Teams.Services;

public sealed class TeamEligibilityHasher : ITeamEligibilityHasher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public TeamEligibilityHashes ComputeHashes(TeamEligibilityContextInput input, DateTime utcNow)
    {
        var rosterHash = ComputeRosterHash(input.Members);
        var academicScopeHash = ComputeAcademicScopeHash(input.Scope, input.ProjectPeriodId, input.ProjectMode);
        var projectContextHash = ComputeProjectContextHash(input.Project);

        var fingerprint = ComputeFingerprint(
            rosterHash,
            academicScopeHash,
            projectContextHash,
            input.PolicyVersion,
            input.RuleVersion,
            input.RoundType,
            input.RevisionHistoryId,
            input.ProjectMode);

        var (temporalStateHash, validUntilAt) = ComputeTemporalState(input.Members, input.Policy, utcNow);

        var evaluationKey = ComputeEvaluationKey(
            fingerprint,
            temporalStateHash,
            input.RoundType,
            input.RevisionHistoryId);

        return new TeamEligibilityHashes(
            RosterHash: rosterHash,
            AcademicScopeHash: academicScopeHash,
            ProjectContextHash: projectContextHash,
            Fingerprint: fingerprint,
            TemporalStateHash: temporalStateHash,
            EvaluationKey: evaluationKey,
            ValidUntilAt: validUntilAt);
    }

    private static string ComputeRosterHash(IReadOnlyList<RosterMemberInput> members)
    {
        var orderedMembers = members
            .OrderBy(m => m.UserId)
            .Select(m => new
            {
                userId = m.UserId,
                fullName = m.FullName,
                majorId = m.MajorId,
                organizationId = m.OrganizationId,
                isEligibleStudent = m.IsEligibleStudent,
                isLeader = m.IsLeader,
                qualificationRequired = m.QualificationRequired,
                qualificationEligible = m.QualificationEligible,
                qualificationIssueCode = m.QualificationIssueCode,
                qualificationValidUntilAt = m.QualificationValidUntilAt?.ToString("o")
            })
            .ToList();

        return Sha256(JsonSerializer.Serialize(orderedMembers, JsonOptions));
    }

    private static string ComputeAcademicScopeHash(AcademicScopeInput? scope, long periodId, string projectMode)
    {
        var obj = new
        {
            periodId,
            projectMode = scope?.ProjectMode ?? projectMode,
            primaryMajorId = scope?.PrimaryMajorId,
            leadDepartmentId = scope?.LeadDepartmentId ?? 0,
            requirements = (scope?.Requirements ?? Array.Empty<MajorRequirementInput>())
                .OrderBy(r => r.MajorId)
                .Select(r => new
                {
                    majorId = r.MajorId,
                    minMembers = r.MinMembers,
                    maxMembers = r.MaxMembers,
                    responsibility = r.Responsibility
                })
                .ToList()
        };

        return Sha256(JsonSerializer.Serialize(obj, JsonOptions));
    }

    private static string ComputeProjectContextHash(ProjectContextInput? project)
    {
        if (project is null || project.ProjectId is null)
        {
            return Sha256(JsonSerializer.Serialize(new
            {
                projectId = (long?)null,
                proposalSource = (string?)null,
                topicId = (long?)null,
                title = (string?)null,
                problemStatement = (string?)null,
                objectives = (string?)null,
                expectedOutput = (string?)null,
                majors = Array.Empty<long>(),
                tags = Array.Empty<object>()
            }, JsonOptions));
        }

        var obj = new
        {
            projectId = project.ProjectId,
            proposalSource = project.ProposalSource,
            topicId = project.TopicId,
            title = project.Title,
            problemStatement = project.ProblemStatement,
            objectives = project.Objectives,
            expectedOutput = project.ExpectedOutput,
            majors = (project.MajorIds ?? Array.Empty<long>()).OrderBy(m => m).ToList(),
            tags = (project.Tags ?? Array.Empty<ProjectTagInput>())
                .OrderBy(t => t.TagType, StringComparer.Ordinal)
                .ThenBy(t => t.TagName, StringComparer.Ordinal)
                .Select(t => new { tagType = t.TagType, tagName = t.TagName })
                .ToList()
        };

        return Sha256(JsonSerializer.Serialize(obj, JsonOptions));
    }

    private static string ComputeFingerprint(
        string rosterHash,
        string academicScopeHash,
        string projectContextHash,
        string policyVersion,
        string ruleVersion,
        string roundType,
        long? revisionHistoryId,
        string projectMode)
    {
        var obj = new
        {
            academicScopeHash,
            policyVersion,
            projectContextHash,
            projectMode,
            revisionHistoryId,
            rosterHash,
            roundType,
            ruleVersion
        };

        return Sha256(JsonSerializer.Serialize(obj, JsonOptions));
    }

    private static (string TemporalStateHash, DateTime? ValidUntilAt) ComputeTemporalState(
        IReadOnlyList<RosterMemberInput> members,
        AIPMS.Domain.Teams.TeamFormationPolicy policy,
        DateTime utcNow)
    {
        var checkExpiration = policy.RequireStudentQualification && policy.CheckQualificationExpiration;

        var memberStates = members
            .OrderBy(m => m.UserId)
            .Select(m =>
            {
                var isExpired = checkExpiration
                    && m.QualificationValidUntilAt.HasValue
                    && utcNow >= m.QualificationValidUntilAt.Value;

                return new
                {
                    userId = m.UserId,
                    isExpired,
                    validUntilAt = m.QualificationValidUntilAt?.ToString("o")
                };
            })
            .ToList();

        var temporalHash = Sha256(JsonSerializer.Serialize(memberStates, JsonOptions));

        DateTime? validUntilAt = null;
        if (checkExpiration)
        {
            var futureExpirations = members
                .Where(m => m.QualificationValidUntilAt.HasValue && m.QualificationValidUntilAt.Value > utcNow)
                .Select(m => m.QualificationValidUntilAt!.Value)
                .ToList();

            if (futureExpirations.Count > 0)
            {
                validUntilAt = futureExpirations.Min();
            }
        }

        return (temporalHash, validUntilAt);
    }

    private static string ComputeEvaluationKey(
        string fingerprint,
        string temporalStateHash,
        string roundType,
        long? revisionHistoryId)
    {
        var obj = new
        {
            fingerprint,
            revisionHistoryId,
            roundType,
            temporalStateHash
        };

        return Sha256(JsonSerializer.Serialize(obj, JsonOptions));
    }

    private static string Sha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
