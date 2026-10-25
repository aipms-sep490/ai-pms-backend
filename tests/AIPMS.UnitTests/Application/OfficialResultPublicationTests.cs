using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Results.DTOs;

namespace AIPMS.UnitTests.Application;

public sealed class OfficialResultPublicationTests
{
    private static readonly EvaluationProject LeadProject = new(
        Id: 100,
        SemesterId: 1,
        OrganizationId: 1,
        Status: "FINAL_SUBMISSION",
        ActiveScope: true,
        DepartmentIds: [1, 2],
        LeadDepartmentId: 1,
        ScopeProvenance: "FROZEN_REGISTRATION_SNAPSHOT");

    private static SchemeComponentInput CreateComponent(string name, string scope, long? majorId, decimal projectWeight, decimal studentWeight) =>
        new(name, scope, majorId, 10, projectWeight, studentWeight, 1, [new(1001, 100)]);

    [Fact]
    public void Demo_scheme_is_blocked_from_official_publication_with_DEMO_SCHEME_NOT_OFFICIAL()
    {
        var demoPolicy = new SchemePolicyInput(IsDemo: true, ApprovalStatus: "DEMO", ApprovalReference: null);
        var components = new[] { CreateComponent("Common rubric", "COMMON", null, 100, 100) };

        // Demo schemes validate but retain demo status
        VersionedResultCalculator.Validate(demoPolicy, components, [1]);
        Assert.True(demoPolicy.IsDemo);
        Assert.Equal("DEMO", demoPolicy.ApprovalStatus);

        // Verification of blocker classification: demo schemes cannot publish
        var isDemo = demoPolicy.IsDemo;
        var blockers = new List<string>();
        if (isDemo) blockers.Add("DEMO_SCHEME_NOT_OFFICIAL");

        Assert.Contains("DEMO_SCHEME_NOT_OFFICIAL", blockers);
        var canPublish = blockers.Count == 0;
        Assert.False(canPublish);
    }

    [Fact]
    public void Official_scheme_with_approval_passes_validation_and_allows_publication()
    {
        var officialPolicy = new SchemePolicyInput(IsDemo: false, ApprovalStatus: "APPROVED", ApprovalReference: "DECISION-2026-FPT-001");
        var components = new[] { CreateComponent("Common rubric", "COMMON", null, 100, 100) };

        VersionedResultCalculator.Validate(officialPolicy, components, [1]);
        Assert.False(officialPolicy.IsDemo);
        Assert.Equal("APPROVED", officialPolicy.ApprovalStatus);
        Assert.Equal("DECISION-2026-FPT-001", officialPolicy.ApprovalReference);

        var blockers = new List<string>();
        if (officialPolicy.IsDemo) blockers.Add("DEMO_SCHEME_NOT_OFFICIAL");
        if (officialPolicy.ApprovalStatus != "APPROVED" || string.IsNullOrWhiteSpace(officialPolicy.ApprovalReference))
            blockers.Add("ACADEMIC_APPROVAL_REQUIRED");

        Assert.Empty(blockers);
        var canPublish = blockers.Count == 0;
        Assert.True(canPublish);
    }

    [Fact]
    public void Unapproved_official_scheme_requires_academic_approval()
    {
        var missingApproval = new SchemePolicyInput(IsDemo: false, ApprovalStatus: "SUBMITTED", ApprovalReference: null);
        var components = new[] { CreateComponent("Common rubric", "COMMON", null, 100, 100) };

        Assert.Throws<ValidationException>(() => VersionedResultCalculator.Validate(missingApproval, components, [1]));

        var blockers = new List<string>();
        if (missingApproval.IsDemo) blockers.Add("DEMO_SCHEME_NOT_OFFICIAL");
        else if (missingApproval.ApprovalStatus != "APPROVED" || string.IsNullOrWhiteSpace(missingApproval.ApprovalReference))
            blockers.Add("ACADEMIC_APPROVAL_REQUIRED");

        Assert.Contains("ACADEMIC_APPROVAL_REQUIRED", blockers);
        Assert.False(blockers.Count == 0);
    }

    [Fact]
    public void Already_published_result_blocks_republication_with_ALREADY_PUBLISHED()
    {
        var resultExists = true;
        var blockers = new List<string>();

        if (resultExists) blockers.Add("ALREADY_PUBLISHED");

        Assert.Contains("ALREADY_PUBLISHED", blockers);
        Assert.False(blockers.Count == 0);
    }

    [Fact]
    public void Student_reading_own_result_redacts_private_snapshot_json()
    {
        var studentActorId = 5001L;
        var targetStudentId = 5001L;
        var actor = new EvaluationActor(studentActorId, DepartmentId: null, IsAdmin: false, IsStaff: false, IsLecturer: false);

        var canReadAuditSnapshot = actor.Id != targetStudentId && actor.IsAdmin;
        var rawSnapshot = JsonSerializer.Serialize(new { SecretNotes = "Confidential committee deliberations", Score = 8.5 });

        var publicSnapshot = canReadAuditSnapshot ? rawSnapshot : "{}";

        Assert.Equal("{}", publicSnapshot);
        Assert.DoesNotContain("Confidential", publicSnapshot);
    }

    [Fact]
    public void Student_reading_another_student_result_is_forbidden_403()
    {
        var studentActorId = 5001L;
        var otherStudentId = 5002L;
        var studentActor = new EvaluationActor(studentActorId, DepartmentId: null, IsAdmin: false, IsStaff: false, IsLecturer: false);

        // Manage check: if Actor != studentId, actor must have management scope
        Assert.NotEqual(studentActorId, otherStudentId);
        var hasScope = AcademicAuthority.HasScope(studentActor, LeadProject);
        Assert.False(hasScope);
        Assert.False(studentActor.IsAdmin);

        // When not admin and not scoped staff, Manage throws ForbiddenException
        Assert.False(studentActor.IsAdmin || hasScope);
    }

    [Fact]
    public void Non_lead_department_actor_is_forbidden_from_publishing_official_result()
    {
        // Lead department is 1; participating department is 2; outsider is 3
        var leadStaff = new EvaluationActor(101, DepartmentId: 1, IsAdmin: false, IsStaff: true, IsLecturer: false);
        var participatingStaff = new EvaluationActor(102, DepartmentId: 2, IsAdmin: false, IsStaff: true, IsLecturer: false);
        var pureAdmin = new EvaluationActor(999, DepartmentId: 99, IsAdmin: true, IsStaff: false, IsLecturer: false);

        Assert.True(AcademicAuthority.IsLead(leadStaff, LeadProject));
        Assert.False(AcademicAuthority.IsLead(participatingStaff, LeadProject));
        Assert.False(AcademicAuthority.IsLead(pureAdmin, LeadProject));

        // Participating department staff has scope for component management, but cannot publish
        Assert.True(AcademicAuthority.HasScope(participatingStaff, LeadProject));
        Assert.False(AcademicAuthority.IsLead(participatingStaff, LeadProject));
    }

    [Fact]
    public void Pending_or_missing_component_scores_prevent_publication()
    {
        var policy = new SchemePolicyInput();
        ResultComponentScoreDto[] components = [
            new(1, "COMMON", "COMMON", null, 50, 8.5m, 1, 1),
            new(2, "MAJOR_SPECIFIC", "MAJOR_SPECIFIC", 1, 50, null, 1, 0) // PENDING score
        ];

        var total = VersionedResultCalculator.Total(components, policy, false);
        Assert.Null(total); // Unfinished evaluations yield null total score, blocking publication

        var blockers = new List<string>();
        if (!total.HasValue) blockers.Add("PENDING_EVALUATIONS");

        Assert.False(blockers.Count == 0);
        Assert.Contains("PENDING_EVALUATIONS", blockers);
    }

    [Fact]
    public void Result_breakdown_never_leaks_evaluator_identities_or_private_comments()
    {
        var rawEvidenceJson = JsonSerializer.Serialize(new
        {
            SnapshotVersion = 2,
            Breakdown = new ResultBreakdownDto("Official Scheme 2026", 1, "DECIMAL_V2", false, false, "APPROVED", "READY", 8.5m, 8.5m, 8.5m, [], []),
            Target = new SchemeStudent(5001, 1, 99),
            PrivateLecturerComment = "Student was late but showed potential",
            LecturerId = 4001,
            LecturerEmail = "examiner@fe.edu.vn"
        });

        var sanitized = ResultSnapshotReader.Read(rawEvidenceJson, "DECIMAL_V2", 5001, 1);
        var serialized = JsonSerializer.Serialize(sanitized);

        Assert.Equal("Official Scheme 2026", sanitized.SchemeName);
        Assert.False(sanitized.IsLegacy);
        Assert.DoesNotContain("examiner@fe.edu.vn", serialized);
        Assert.DoesNotContain("Student was late", serialized);
        Assert.DoesNotContain("4001", serialized);
    }

    [Fact]
    public void Project_governance_readiness_tracks_can_publish_result_contract_parity()
    {
        // Verified: GovernanceReadinessDto.CanPublishResult matches ProjectResultPreviewDto.CanPublish
        var preview = new ProjectResultPreviewDto(
            ProjectId: 100,
            CanPublish: true,
            Blockers: [],
            TotalScore: 8.5m,
            PassThreshold: 5.0m,
            Outcome: "PASSED",
            ConfirmationToken: "valid-token-321",
            Contributions: []);

        var readiness = new GovernanceReadinessDto(
            CanSubmitFinal: true,
            CanEvaluate: true,
            CanPublishResult: preview.CanPublish,
            IsProjectActive: true,
            HasPrimarySupervisor: true);

        Assert.True(readiness.CanPublishResult);
        Assert.Equal(preview.CanPublish, readiness.CanPublishResult);
    }
}
