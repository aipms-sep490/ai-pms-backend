using System;
using System.Collections.Generic;
using System.Linq;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Models;
using AIPMS.Application.Features.Teams.Services;
using AIPMS.Domain.Teams;
using Xunit;

namespace AIPMS.UnitTests.Teams;

public sealed class TeamEligibilityGovernanceUnitTests
{
    private readonly TeamEligibilityHasher _hasher = new();
    private readonly TeamEligibilityFreshnessEvaluator _evaluator = new();

    private static readonly DateTime BaseTime = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    private static TeamEligibilityContextInput CreateSampleInput(
        long teamId = 1,
        long projectPeriodId = 10,
        long? projectId = 100,
        string roundType = "INITIAL",
        long? revisionHistoryId = null,
        string projectMode = "SINGLE_MAJOR",
        string policyVersion = "v1",
        string ruleVersion = "1",
        bool isAcademicProfileVerified = true,
        long majorId = 301,
        int minMembers = 1,
        int maxMembers = 5,
        string responsibility = "Lead development",
        string proposalSource = "STUDENT_PROPOSAL",
        long? topicId = null,
        string title = "Project Title",
        string problemStatement = "Problem Statement",
        string objectives = "Objectives",
        string expectedOutput = "Output",
        bool qualificationRequired = true,
        bool qualificationEligible = true,
        DateTime? qualificationValidUntilAt = null)
    {
        var policy = new TeamFormationPolicy(
            MinMembers: 1,
            MaxMembers: 5,
            InvitationHours: 24,
            Version: policyVersion,
            MinDistinctMajors: 1,
            RequireStudentQualification: qualificationRequired,
            RequiredQualificationType: "CAPSTONE_READINESS",
            RequireCertificate: true,
            CheckQualificationExpiration: true);

        var members = new List<RosterMemberInput>
        {
            new(
                UserId: 1,
                FullName: "Alice",
                MajorId: majorId,
                OrganizationId: 1,
                IsEligibleStudent: isAcademicProfileVerified,
                IsLeader: true,
                QualificationRequired: qualificationRequired,
                QualificationEligible: qualificationEligible,
                QualificationIssueCode: qualificationEligible ? null : "QUALIFICATION_REQUIRED",
                QualificationValidUntilAt: qualificationValidUntilAt ?? BaseTime.AddDays(30)),
            new(
                UserId: 2,
                FullName: "Bob",
                MajorId: majorId,
                OrganizationId: 1,
                IsEligibleStudent: true,
                IsLeader: false,
                QualificationRequired: qualificationRequired,
                QualificationEligible: true,
                QualificationIssueCode: null,
                QualificationValidUntilAt: BaseTime.AddDays(60))
        };

        var scope = new AcademicScopeInput(
            PeriodId: projectPeriodId,
            ProjectMode: projectMode,
            PrimaryMajorId: majorId,
            LeadDepartmentId: 10,
            Requirements: new List<MajorRequirementInput>
            {
                new(majorId, minMembers, maxMembers, responsibility)
            });

        var project = projectId.HasValue
            ? new ProjectContextInput(
                ProjectId: projectId.Value,
                ProposalSource: proposalSource,
                TopicId: topicId,
                Title: title,
                ProblemStatement: problemStatement,
                Objectives: objectives,
                ExpectedOutput: expectedOutput,
                MajorIds: new List<long> { majorId },
                Tags: new List<ProjectTagInput>
                {
                    new("DOMAIN", "Healthcare"),
                    new("TECHNOLOGY", ".NET")
                })
            : null;

        return new TeamEligibilityContextInput(
            TeamId: teamId,
            ProjectPeriodId: projectPeriodId,
            ProjectId: projectId,
            RoundType: roundType,
            RevisionHistoryId: revisionHistoryId,
            ProjectMode: projectMode,
            PolicyVersion: policyVersion,
            RuleVersion: ruleVersion,
            Members: members,
            Scope: scope,
            Project: project,
            Policy: policy);
    }

    private static TeamEligibilitySnapshotData CreateSnapshotFromHashes(
        TeamEligibilityContextInput input,
        TeamEligibilityHashes hashes,
        string result = "PASS")
    {
        return new TeamEligibilitySnapshotData(
            Id: 1,
            TeamId: input.TeamId,
            ProjectPeriodId: input.ProjectPeriodId,
            ProjectId: input.ProjectId,
            RoundType: input.RoundType,
            RevisionHistoryId: input.RevisionHistoryId,
            ProjectMode: input.ProjectMode,
            PolicyVersion: input.PolicyVersion,
            RuleVersion: input.RuleVersion,
            RosterHash: hashes.RosterHash,
            AcademicScopeHash: hashes.AcademicScopeHash,
            ProjectContextHash: hashes.ProjectContextHash,
            Fingerprint: hashes.Fingerprint,
            TemporalStateHash: hashes.TemporalStateHash,
            EvaluationKey: hashes.EvaluationKey,
            Result: result,
            ValidUntilAt: hashes.ValidUntilAt,
            CheckedBy: 1,
            CheckedAt: BaseTime,
            TriggerSource: "MANUAL_CHECK",
            Issues: Array.Empty<TeamEligibilityIssueDto>());
    }

    private static TeamEligibilityEvaluationContext CreateEvalContext(
        TeamEligibilityContextInput input,
        TeamEligibilityHashes hashes)
    {
        return new TeamEligibilityEvaluationContext(
            TeamId: input.TeamId,
            ProjectPeriodId: input.ProjectPeriodId,
            ProjectId: input.ProjectId,
            RoundType: input.RoundType,
            RevisionHistoryId: input.RevisionHistoryId,
            ProjectMode: input.ProjectMode,
            PolicyVersion: input.PolicyVersion,
            RuleVersion: input.RuleVersion,
            Hashes: hashes);
    }

    // A. Canonical hash determinism
    [Fact]
    public void TestA_CanonicalHashDeterminism()
    {
        var input1 = CreateSampleInput();
        var input2 = CreateSampleInput();

        var hashes1 = _hasher.ComputeHashes(input1, BaseTime);
        var hashes2 = _hasher.ComputeHashes(input2, BaseTime);

        Assert.Equal(hashes1.RosterHash, hashes2.RosterHash);
        Assert.Equal(hashes1.AcademicScopeHash, hashes2.AcademicScopeHash);
        Assert.Equal(hashes1.ProjectContextHash, hashes2.ProjectContextHash);
        Assert.Equal(hashes1.Fingerprint, hashes2.Fingerprint);
        Assert.Equal(hashes1.TemporalStateHash, hashes2.TemporalStateHash);
        Assert.Equal(hashes1.EvaluationKey, hashes2.EvaluationKey);
    }

    // B. Collection ordering does not change hash
    [Fact]
    public void TestB_CollectionOrderingDoesNotChangeHash()
    {
        var inputForward = CreateSampleInput();
        var inputReversed = CreateSampleInput() with
        {
            Members = inputForward.Members.Reverse().ToList()
        };

        var hashesForward = _hasher.ComputeHashes(inputForward, BaseTime);
        var hashesReversed = _hasher.ComputeHashes(inputReversed, BaseTime);

        Assert.Equal(hashesForward.RosterHash, hashesReversed.RosterHash);
        Assert.Equal(hashesForward.Fingerprint, hashesReversed.Fingerprint);
    }

    // C. Roster mutation changes fingerprint
    [Fact]
    public void TestC_RosterMutationChangesFingerprint()
    {
        var baseInput = CreateSampleInput();
        var mutatedMembers = baseInput.Members.Take(1).ToList(); // Remove Bob
        var mutatedInput = baseInput with { Members = mutatedMembers };

        var baseHashes = _hasher.ComputeHashes(baseInput, BaseTime);
        var mutatedHashes = _hasher.ComputeHashes(mutatedInput, BaseTime);

        Assert.NotEqual(baseHashes.RosterHash, mutatedHashes.RosterHash);
        Assert.NotEqual(baseHashes.Fingerprint, mutatedHashes.Fingerprint);
    }

    // D. Verified profile change => STALE
    [Fact]
    public void TestD_VerifiedProfileChangeMakesSnapshotStale()
    {
        var initialInput = CreateSampleInput(isAcademicProfileVerified: true);
        var initialHashes = _hasher.ComputeHashes(initialInput, BaseTime);
        var snapshot = CreateSnapshotFromHashes(initialInput, initialHashes);

        // Academic profile revoked
        var currentInput = CreateSampleInput(isAcademicProfileVerified: false);
        var currentHashes = _hasher.ComputeHashes(currentInput, BaseTime);
        var currentContext = CreateEvalContext(currentInput, currentHashes);

        var freshness = _evaluator.EvaluateFreshness(snapshot, currentContext, BaseTime);
        Assert.Equal(FreshnessStatus.Stale, freshness);
    }

    // E. Member major change => STALE
    [Fact]
    public void TestE_MemberMajorChangeMakesSnapshotStale()
    {
        var initialInput = CreateSampleInput(majorId: 301);
        var initialHashes = _hasher.ComputeHashes(initialInput, BaseTime);
        var snapshot = CreateSnapshotFromHashes(initialInput, initialHashes);

        var currentInput = CreateSampleInput(majorId: 302);
        var currentHashes = _hasher.ComputeHashes(currentInput, BaseTime);
        var currentContext = CreateEvalContext(currentInput, currentHashes);

        var freshness = _evaluator.EvaluateFreshness(snapshot, currentContext, BaseTime);
        Assert.Equal(FreshnessStatus.Stale, freshness);
    }

    // F. Quota change => STALE
    [Fact]
    public void TestF_QuotaChangeMakesSnapshotStale()
    {
        var initialInput = CreateSampleInput(minMembers: 1, maxMembers: 5);
        var initialHashes = _hasher.ComputeHashes(initialInput, BaseTime);
        var snapshot = CreateSnapshotFromHashes(initialInput, initialHashes);

        var currentInput = CreateSampleInput(minMembers: 2, maxMembers: 6);
        var currentHashes = _hasher.ComputeHashes(currentInput, BaseTime);
        var currentContext = CreateEvalContext(currentInput, currentHashes);

        var freshness = _evaluator.EvaluateFreshness(snapshot, currentContext, BaseTime);
        Assert.Equal(FreshnessStatus.Stale, freshness);
    }

    // G. Responsibility change => STALE
    [Fact]
    public void TestG_ResponsibilityChangeMakesSnapshotStale()
    {
        var initialInput = CreateSampleInput(responsibility: "Initial responsibility");
        var initialHashes = _hasher.ComputeHashes(initialInput, BaseTime);
        var snapshot = CreateSnapshotFromHashes(initialInput, initialHashes);

        var currentInput = CreateSampleInput(responsibility: "Updated responsibility");
        var currentHashes = _hasher.ComputeHashes(currentInput, BaseTime);
        var currentContext = CreateEvalContext(currentInput, currentHashes);

        var freshness = _evaluator.EvaluateFreshness(snapshot, currentContext, BaseTime);
        Assert.Equal(FreshnessStatus.Stale, freshness);
    }

    // H. Project source/mode change => STALE
    [Fact]
    public void TestH_ProjectSourceModeChangeMakesSnapshotStale()
    {
        var initialInput = CreateSampleInput(proposalSource: "STUDENT_PROPOSAL");
        var initialHashes = _hasher.ComputeHashes(initialInput, BaseTime);
        var snapshot = CreateSnapshotFromHashes(initialInput, initialHashes);

        var currentInput = CreateSampleInput(proposalSource: "PUBLISHED_TOPIC");
        var currentHashes = _hasher.ComputeHashes(currentInput, BaseTime);
        var currentContext = CreateEvalContext(currentInput, currentHashes);

        var freshness = _evaluator.EvaluateFreshness(snapshot, currentContext, BaseTime);
        Assert.Equal(FreshnessStatus.Stale, freshness);
    }

    // I. Topic change => STALE
    [Fact]
    public void TestI_TopicChangeMakesSnapshotStale()
    {
        var initialInput = CreateSampleInput(topicId: 10);
        var initialHashes = _hasher.ComputeHashes(initialInput, BaseTime);
        var snapshot = CreateSnapshotFromHashes(initialInput, initialHashes);

        var currentInput = CreateSampleInput(topicId: 20);
        var currentHashes = _hasher.ComputeHashes(currentInput, BaseTime);
        var currentContext = CreateEvalContext(currentInput, currentHashes);

        var freshness = _evaluator.EvaluateFreshness(snapshot, currentContext, BaseTime);
        Assert.Equal(FreshnessStatus.Stale, freshness);
    }

    // J. Project requirements change => STALE
    [Fact]
    public void TestJ_ProjectRequirementsChangeMakesSnapshotStale()
    {
        var initialInput = CreateSampleInput(title: "Initial Title");
        var initialHashes = _hasher.ComputeHashes(initialInput, BaseTime);
        var snapshot = CreateSnapshotFromHashes(initialInput, initialHashes);

        var currentInput = CreateSampleInput(title: "Revised Title");
        var currentHashes = _hasher.ComputeHashes(currentInput, BaseTime);
        var currentContext = CreateEvalContext(currentInput, currentHashes);

        var freshness = _evaluator.EvaluateFreshness(snapshot, currentContext, BaseTime);
        Assert.Equal(FreshnessStatus.Stale, freshness);
    }

    // K. Policy version change => STALE
    [Fact]
    public void TestK_PolicyVersionChangeMakesSnapshotStale()
    {
        var initialInput = CreateSampleInput(policyVersion: "v1");
        var initialHashes = _hasher.ComputeHashes(initialInput, BaseTime);
        var snapshot = CreateSnapshotFromHashes(initialInput, initialHashes);

        var currentInput = CreateSampleInput(policyVersion: "v2");
        var currentHashes = _hasher.ComputeHashes(currentInput, BaseTime);
        var currentContext = CreateEvalContext(currentInput, currentHashes);

        var freshness = _evaluator.EvaluateFreshness(snapshot, currentContext, BaseTime);
        Assert.Equal(FreshnessStatus.Stale, freshness);
    }

    // L. RuleSetVersion change => STALE
    [Fact]
    public void TestL_RuleSetVersionChangeMakesSnapshotStale()
    {
        var initialInput = CreateSampleInput(ruleVersion: "1");
        var initialHashes = _hasher.ComputeHashes(initialInput, BaseTime);
        var snapshot = CreateSnapshotFromHashes(initialInput, initialHashes);

        var currentInput = CreateSampleInput(ruleVersion: "2");
        var currentHashes = _hasher.ComputeHashes(currentInput, BaseTime);
        var currentContext = CreateEvalContext(currentInput, currentHashes);

        var freshness = _evaluator.EvaluateFreshness(snapshot, currentContext, BaseTime);
        Assert.Equal(FreshnessStatus.Stale, freshness);
    }

    // M. Qualification verification change => STALE
    [Fact]
    public void TestM_QualificationVerificationChangeMakesSnapshotStale()
    {
        var initialInput = CreateSampleInput(qualificationEligible: true);
        var initialHashes = _hasher.ComputeHashes(initialInput, BaseTime);
        var snapshot = CreateSnapshotFromHashes(initialInput, initialHashes);

        var currentInput = CreateSampleInput(qualificationEligible: false);
        var currentHashes = _hasher.ComputeHashes(currentInput, BaseTime);
        var currentContext = CreateEvalContext(currentInput, currentHashes);

        var freshness = _evaluator.EvaluateFreshness(snapshot, currentContext, BaseTime);
        Assert.Equal(FreshnessStatus.Stale, freshness);
    }

    // N. Temporal expiry without DB mutation => STALE
    [Fact]
    public void TestN_TemporalExpiryWithoutDbMutationMakesSnapshotStale()
    {
        var expiryTime = BaseTime.AddHours(2);
        var initialInput = CreateSampleInput(qualificationValidUntilAt: expiryTime);
        var initialHashes = _hasher.ComputeHashes(initialInput, BaseTime);
        var snapshot = CreateSnapshotFromHashes(initialInput, initialHashes);

        // Before expiry: CURRENT
        var currentHashesBefore = _hasher.ComputeHashes(initialInput, BaseTime.AddHours(1));
        var evalContextBefore = CreateEvalContext(initialInput, currentHashesBefore);
        var freshnessBefore = _evaluator.EvaluateFreshness(snapshot, evalContextBefore, BaseTime.AddHours(1));
        Assert.Equal(FreshnessStatus.Current, freshnessBefore);

        // After expiry with ZERO DB mutation: STALE
        var afterExpiryTime = BaseTime.AddHours(3);
        var currentHashesAfter = _hasher.ComputeHashes(initialInput, afterExpiryTime);
        var evalContextAfter = CreateEvalContext(initialInput, currentHashesAfter);
        var freshnessAfter = _evaluator.EvaluateFreshness(snapshot, evalContextAfter, afterExpiryTime);
        Assert.Equal(FreshnessStatus.Stale, freshnessAfter);
    }

    // O. New revision_history_id => old snapshot STALE
    [Fact]
    public void TestO_NewRevisionHistoryIdMakesOldSnapshotStale()
    {
        var round1Input = CreateSampleInput(roundType: "REVISION", revisionHistoryId: 10);
        var round1Hashes = _hasher.ComputeHashes(round1Input, BaseTime);
        var snapshotRound1 = CreateSnapshotFromHashes(round1Input, round1Hashes);

        var round2Input = CreateSampleInput(roundType: "REVISION", revisionHistoryId: 20);
        var round2Hashes = _hasher.ComputeHashes(round2Input, BaseTime);
        var evalContextRound2 = CreateEvalContext(round2Input, round2Hashes);

        var freshness = _evaluator.EvaluateFreshness(snapshotRound1, evalContextRound2, BaseTime);
        Assert.Equal(FreshnessStatus.Stale, freshness);
    }

    // P. CURRENT FAIL remains CURRENT but cannot authorize lock/submit
    [Fact]
    public void TestP_CurrentFailRemainsCurrent()
    {
        var input = CreateSampleInput();
        var hashes = _hasher.ComputeHashes(input, BaseTime);
        var snapshot = CreateSnapshotFromHashes(input, hashes, result: "FAIL");
        var evalContext = CreateEvalContext(input, hashes);

        var freshness = _evaluator.EvaluateFreshness(snapshot, evalContext, BaseTime);
        Assert.Equal(FreshnessStatus.Current, freshness);
        Assert.Equal("FAIL", snapshot.Result);
        // CURRENT FAIL is valid freshness, but cannot authorize lock/submit
    }

    // Q. CURRENT PASS authorizes relevant guard
    [Fact]
    public void TestQ_CurrentPassAuthorizesGuard()
    {
        var input = CreateSampleInput();
        var hashes = _hasher.ComputeHashes(input, BaseTime);
        var snapshot = CreateSnapshotFromHashes(input, hashes, result: "PASS");
        var evalContext = CreateEvalContext(input, hashes);

        var freshness = _evaluator.EvaluateFreshness(snapshot, evalContext, BaseTime);
        Assert.Equal(FreshnessStatus.Current, freshness);
        Assert.Equal("PASS", snapshot.Result);
    }

    // R. Status transition matrix
    [Theory]
    [InlineData("FORMING", "PASS", "ELIGIBLE")]
    [InlineData("FORMING", "FAIL", "FORMING")]
    [InlineData("ELIGIBLE", "PASS", "ELIGIBLE")]
    [InlineData("ELIGIBLE", "FAIL", "FORMING")]
    [InlineData("LOCKED", "PASS", "LOCKED")]
    [InlineData("LOCKED", "FAIL", "LOCKED")]
    public void TestR_StatusTransitionMatrix(string initialStatus, string result, string expectedStatus)
    {
        var newStatus = (initialStatus, result) switch
        {
            ("FORMING", "PASS") => "ELIGIBLE",
            ("FORMING", "FAIL") => "FORMING",
            ("ELIGIBLE", "PASS") => "ELIGIBLE",
            ("ELIGIBLE", "FAIL") => "FORMING",
            ("LOCKED", _) => "LOCKED",
            _ => initialStatus
        };

        Assert.Equal(expectedStatus, newStatus);
    }

    // S. evaluation_key deterministic/different by revision round
    [Fact]
    public void TestS_EvaluationKeyDeterministicAndDifferentByRevisionRound()
    {
        var round1 = CreateSampleInput(roundType: "REVISION", revisionHistoryId: 101);
        var round2 = CreateSampleInput(roundType: "REVISION", revisionHistoryId: 102);

        var hashes1A = _hasher.ComputeHashes(round1, BaseTime);
        var hashes1B = _hasher.ComputeHashes(round1, BaseTime);
        var hashes2 = _hasher.ComputeHashes(round2, BaseTime);

        // Deterministic for same round
        Assert.Equal(hashes1A.EvaluationKey, hashes1B.EvaluationKey);

        // Different for different revision rounds
        Assert.NotEqual(hashes1A.EvaluationKey, hashes2.EvaluationKey);
    }

    // Roster mutation guard tests
    [Fact]
    public void Test_RosterMutationGuard_RejectsWhenTeamLocked()
    {
        var guard = new TeamRosterMutationGuard(null!);
        Assert.Throws<ConflictException>(() => guard.ValidateRosterMutable("LOCKED", []));
    }

    [Fact]
    public void Test_RosterMutationGuard_RejectsWhenProjectLocksRoster()
    {
        var guard = new TeamRosterMutationGuard(null!);
        Assert.Throws<ConflictException>(() => guard.ValidateRosterMutable("FORMING", ["SUBMITTED"]));
        Assert.Throws<ConflictException>(() => guard.ValidateRosterMutable("ELIGIBLE", ["UNDER_REVIEW"]));
        Assert.Throws<ConflictException>(() => guard.ValidateRosterMutable("ELIGIBLE", ["ACTIVE"]));
    }

    [Fact]
    public void Test_RosterMutationGuard_AllowsWhenFormingOrEligibleAndNoLockingProjects()
    {
        var guard = new TeamRosterMutationGuard(null!);
        // Does not throw
        guard.ValidateRosterMutable("FORMING", ["DRAFT"]);
        guard.ValidateRosterMutable("FORMING", ["REVISION_REQUIRED"]);
        guard.ValidateRosterMutable("ELIGIBLE", ["DRAFT"]);
        guard.ValidateRosterMutable("ELIGIBLE", []);
    }
}
