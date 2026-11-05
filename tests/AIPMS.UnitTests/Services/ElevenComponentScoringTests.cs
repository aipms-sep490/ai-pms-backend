using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Services;
using Xunit;

namespace AIPMS.UnitTests.Services;

public sealed class ElevenComponentScoringTests
{
    private const long MajorSe = 101;
    private const long MajorMkt = 102;
    private const long MajorGd = 103;

    private static SchemePolicyInput ComponentWeightsPolicy(bool isDemo = false, string approvalStatus = "APPROVED", string? approvalRef = "QĐ 123/QĐ-ĐHX") =>
        new(IsDemo: isDemo, ApprovalStatus: approvalStatus, ApprovalReference: approvalRef, Formula: "COMPONENT_WEIGHTS");

    private static ResultComponentScoreDto Component(long id, string name, string scope, long? majorId, decimal weight, decimal? score, int evaluators = 2, int finalized = 2, string stage = "DEFENSE") =>
        new(id, name, scope, majorId, weight, score, evaluators, finalized, stage);

    private static SchemeComponentInput SchemeComp(string name, string scope, long? majorId, decimal projWeight, decimal stuWeight, int evaluators = 2, string stage = "DEFENSE") =>
        new(name, scope, majorId, 1, projWeight, stuWeight, evaluators, null, stage);

    // ==========================================
    // 1. 5 component đã lọc đúng cho SV ngành SE, đủ phiếu => tổng đúng 100, trả số thật
    // ==========================================
    [Fact]
    public void Test01_Filtered5Components_For_SE_Student_Calculates_Total_Correctly()
    {
        var policy = ComponentWeightsPolicy();
        var seStudentComponents = new List<ResultComponentScoreDto>
        {
            Component(1, "Hồ sơ chung", "COMMON", null, 15m, 8.0m, stage: "COLD"),
            Component(2, "Bảo vệ giải pháp chung", "COMMON", null, 35m, 9.0m, stage: "DEFENSE"),
            Component(3, "Hồ sơ ngành CNTT", "MAJOR_SPECIFIC", MajorSe, 15m, 8.5m, stage: "COLD"),
            Component(4, "Bảo vệ chuyên môn CNTT", "MAJOR_SPECIFIC", MajorSe, 15m, 7.5m, stage: "DEFENSE"),
            Component(9, "Vấn đáp cá nhân SV CNTT", "INDIVIDUAL", MajorSe, 20m, 9.5m, stage: "DEFENSE")
        };

        var total = VersionedResultCalculator.Total(seStudentComponents, policy, student: true);

        Assert.NotNull(total);
        // 8.0 * 0.15 + 9.0 * 0.35 + 8.5 * 0.15 + 7.5 * 0.15 + 9.5 * 0.20
        // = 1.20 + 3.15 + 1.275 + 1.125 + 1.90 = 8.65
        Assert.Equal(8.65m, total.Value);
    }

    // ==========================================
    // 2. Truyền cả 11 component => null (tổng 215)
    // ==========================================
    [Fact]
    public void Test02_Passing_All_11_Components_Returns_Null_Due_To_Weight_215()
    {
        var policy = ComponentWeightsPolicy();
        var all11Components = new List<ResultComponentScoreDto>
        {
            Component(1, "Hồ sơ chung", "COMMON", null, 15m, 8.0m, stage: "COLD"),
            Component(2, "Bảo vệ giải pháp chung", "COMMON", null, 35m, 9.0m, stage: "DEFENSE"),
            Component(3, "Hồ sơ ngành CNTT", "MAJOR_SPECIFIC", MajorSe, 15m, 8.5m, stage: "COLD"),
            Component(4, "Bảo vệ chuyên môn CNTT", "MAJOR_SPECIFIC", MajorSe, 15m, 7.5m, stage: "DEFENSE"),
            Component(5, "Hồ sơ ngành Marketing", "MAJOR_SPECIFIC", MajorMkt, 15m, 8.0m, stage: "COLD"),
            Component(6, "Bảo vệ chuyên môn Marketing", "MAJOR_SPECIFIC", MajorMkt, 15m, 8.0m, stage: "DEFENSE"),
            Component(7, "Hồ sơ ngành Thiết kế", "MAJOR_SPECIFIC", MajorGd, 15m, 8.0m, stage: "COLD"),
            Component(8, "Bảo vệ chuyên môn Thiết kế", "MAJOR_SPECIFIC", MajorGd, 15m, 8.0m, stage: "DEFENSE"),
            Component(9, "Vấn đáp cá nhân SV CNTT", "INDIVIDUAL", MajorSe, 20m, 9.5m, stage: "DEFENSE"),
            Component(10, "Vấn đáp cá nhân SV Marketing", "INDIVIDUAL", MajorMkt, 20m, 9.0m, stage: "DEFENSE"),
            Component(11, "Vấn đáp cá nhân SV Thiết kế", "INDIVIDUAL", MajorGd, 20m, 9.0m, stage: "DEFENSE")
        };

        // Note: Sum of student weights across all 11 components is 200% (or 215% if project weights are used), strictly != 100%
        Assert.NotEqual(100m, all11Components.Sum(c => c.WeightPercent));
        Assert.Equal(200m, all11Components.Sum(c => c.WeightPercent));

        var total = VersionedResultCalculator.Total(all11Components, policy, student: true);

        Assert.Null(total);
    }

    // ==========================================
    // 3. Lọc thiếu #9 INDIVIDUAL (còn 80) => null
    // ==========================================
    [Fact]
    public void Test03_Filtering_Omits_Individual_Component_Returns_Null_Due_To_Weight_80()
    {
        var policy = ComponentWeightsPolicy();
        var omittedIndividual = new List<ResultComponentScoreDto>
        {
            Component(1, "Hồ sơ chung", "COMMON", null, 15m, 8.0m, stage: "COLD"),
            Component(2, "Bảo vệ giải pháp chung", "COMMON", null, 35m, 9.0m, stage: "DEFENSE"),
            Component(3, "Hồ sơ ngành CNTT", "MAJOR_SPECIFIC", MajorSe, 15m, 8.5m, stage: "COLD"),
            Component(4, "Bảo vệ chuyên môn CNTT", "MAJOR_SPECIFIC", MajorSe, 15m, 7.5m, stage: "DEFENSE")
            // Missing Component 9: INDIVIDUAL (weight 20)
        };

        Assert.Equal(80m, omittedIndividual.Sum(c => c.WeightPercent));

        var total = VersionedResultCalculator.Total(omittedIndividual, policy, student: true);

        Assert.Null(total);
    }

    // ==========================================
    // 4. 5 component đúng nhưng một component Score = null => null — N6
    // ==========================================
    [Fact]
    public void Test04_Valid5Components_With_One_Score_Null_Returns_Null_N6()
    {
        var policy = ComponentWeightsPolicy();
        var withNullScore = new List<ResultComponentScoreDto>
        {
            Component(1, "Hồ sơ chung", "COMMON", null, 15m, 8.0m, stage: "COLD"),
            Component(2, "Bảo vệ giải pháp chung", "COMMON", null, 35m, 9.0m, stage: "DEFENSE"),
            Component(3, "Hồ sơ ngành CNTT", "MAJOR_SPECIFIC", MajorSe, 15m, null, stage: "COLD"), // Score is null!
            Component(4, "Bảo vệ chuyên môn CNTT", "MAJOR_SPECIFIC", MajorSe, 15m, 7.5m, stage: "DEFENSE"),
            Component(9, "Vấn đáp cá nhân SV CNTT", "INDIVIDUAL", MajorSe, 20m, 9.5m, stage: "DEFENSE")
        };

        var total = VersionedResultCalculator.Total(withNullScore, policy, student: true);

        Assert.Null(total);
    }

    // ==========================================
    // 5. 5 component đúng, một component FinalizedEvaluators = 2 / RequiredEvaluators = 3 => null, không chia lại trọng số — N6
    // ==========================================
    [Fact]
    public void Test05_Component_With_Incomplete_Evaluators_Returns_Null_No_Reweighting_N6()
    {
        var policy = ComponentWeightsPolicy();
        var incompleteEvaluators = new List<ResultComponentScoreDto>
        {
            Component(1, "Hồ sơ chung", "COMMON", null, 15m, 8.0m, evaluators: 2, finalized: 2, stage: "COLD"),
            // Component 2 requires 3 evaluators, but only 2 have finalized:
            Component(2, "Bảo vệ giải pháp chung", "COMMON", null, 35m, 9.0m, evaluators: 3, finalized: 2, stage: "DEFENSE"),
            Component(3, "Hồ sơ ngành CNTT", "MAJOR_SPECIFIC", MajorSe, 15m, 8.5m, evaluators: 2, finalized: 2, stage: "COLD"),
            Component(4, "Bảo vệ chuyên môn CNTT", "MAJOR_SPECIFIC", MajorSe, 15m, 7.5m, evaluators: 2, finalized: 2, stage: "DEFENSE"),
            Component(9, "Vấn đáp cá nhân SV CNTT", "INDIVIDUAL", MajorSe, 20m, 9.5m, evaluators: 2, finalized: 2, stage: "DEFENSE")
        };

        var total = VersionedResultCalculator.Total(incompleteEvaluators, policy, student: true);

        // N6 invariant: Must return null, NEVER re-weight or calculate from partial evaluators
        Assert.Null(total);
    }

    // ==========================================
    // 6. Một component Score = 0 (không phải null) => tính được, 0 là điểm thật — N6
    // ==========================================
    [Fact]
    public void Test06_Component_With_Score_Zero_Calculates_Properly_Zero_Is_Real_Score_N6()
    {
        var policy = ComponentWeightsPolicy();
        var withZeroScore = new List<ResultComponentScoreDto>
        {
            Component(1, "Hồ sơ chung", "COMMON", null, 15m, 8.0m, stage: "COLD"),
            Component(2, "Bảo vệ giải pháp chung", "COMMON", null, 35m, 9.0m, stage: "DEFENSE"),
            Component(3, "Hồ sơ ngành CNTT", "MAJOR_SPECIFIC", MajorSe, 15m, 0.0m, stage: "COLD"), // Score is 0 (real score)
            Component(4, "Bảo vệ chuyên môn CNTT", "MAJOR_SPECIFIC", MajorSe, 15m, 7.5m, stage: "DEFENSE"),
            Component(9, "Vấn đáp cá nhân SV CNTT", "INDIVIDUAL", MajorSe, 20m, 9.5m, stage: "DEFENSE")
        };

        var total = VersionedResultCalculator.Total(withZeroScore, policy, student: true);

        Assert.NotNull(total);
        // 8.0 * 0.15 + 9.0 * 0.35 + 0.0 * 0.15 + 7.5 * 0.15 + 9.5 * 0.20
        // = 1.20 + 3.15 + 0.00 + 1.125 + 1.90 = 7.375
        Assert.Equal(7.375m, total.Value);
    }

    // ==========================================
    // 7. Formula = "DEMO_50_30_20" với 11 component, 3 ngành => Validate throw — :31
    // ==========================================
    [Fact]
    public void Test07_Validate_Throws_When_DemoFormula_Used_With_11_Components_And_3_Majors()
    {
        var demoPolicy = new SchemePolicyInput(IsDemo: true, ApprovalStatus: "DEMO", Formula: "DEMO_50_30_20");
        var components = new List<SchemeComponentInput>
        {
            SchemeComp("Hồ sơ chung", "COMMON", null, 18.75m, 15m, stage: "COLD"),
            SchemeComp("Bảo vệ chung", "COMMON", null, 31.25m, 35m, stage: "DEFENSE"),
            SchemeComp("Hồ sơ CNTT", "MAJOR_SPECIFIC", MajorSe, 8.33m, 15m, stage: "COLD"),
            SchemeComp("Bảo vệ CNTT", "MAJOR_SPECIFIC", MajorSe, 8.34m, 15m, stage: "DEFENSE"),
            SchemeComp("Hồ sơ MKT", "MAJOR_SPECIFIC", MajorMkt, 8.33m, 15m, stage: "COLD"),
            SchemeComp("Bảo vệ MKT", "MAJOR_SPECIFIC", MajorMkt, 8.33m, 15m, stage: "DEFENSE"),
            SchemeComp("Hồ sơ GD", "MAJOR_SPECIFIC", MajorGd, 8.33m, 15m, stage: "COLD"),
            SchemeComp("Bảo vệ GD", "MAJOR_SPECIFIC", MajorGd, 8.34m, 15m, stage: "DEFENSE"),
            SchemeComp("Cá nhân CNTT", "INDIVIDUAL", MajorSe, 0m, 20m, stage: "DEFENSE"),
            SchemeComp("Cá nhân MKT", "INDIVIDUAL", MajorMkt, 0m, 20m, stage: "DEFENSE"),
            SchemeComp("Cá nhân GD", "INDIVIDUAL", MajorGd, 0m, 20m, stage: "DEFENSE")
        };
        var majors = new[] { MajorSe, MajorMkt, MajorGd };

        // DEMO_50_30_20 requires exactly 1 + 3 * 2 = 7 components. 11 components must fail validation!
        Assert.Throws<ValidationException>(() => VersionedResultCalculator.Validate(demoPolicy, components, majors));
    }

    // ==========================================
    // 8. Formula = "COMPONENT_WEIGHTS", IsDemo = true, ApprovalStatus = "DEMO" => Validate pass
    // ==========================================
    [Fact]
    public void Test08_Validate_Succeeds_For_ComponentWeights_With_IsDemo_True_And_ApprovalStatus_Demo()
    {
        var demoPolicy = new SchemePolicyInput(IsDemo: true, ApprovalStatus: "DEMO", Formula: "COMPONENT_WEIGHTS");
        var components = new List<SchemeComponentInput>
        {
            SchemeComp("Hồ sơ chung", "COMMON", null, 18.75m, 15m, stage: "COLD"),
            SchemeComp("Bảo vệ chung", "COMMON", null, 31.25m, 35m, stage: "DEFENSE")
        };
        var majors = new[] { MajorSe, MajorMkt, MajorGd };

        // Does not throw
        VersionedResultCalculator.Validate(demoPolicy, components, majors);
    }

    // ==========================================
    // 9. IsDemo = false, ApprovalStatus = "APPROVED", ApprovalReference rỗng => Validate throw — N7, :17
    // ==========================================
    [Fact]
    public void Test09_Validate_Throws_When_IsDemo_False_Approved_And_ApprovalReference_Is_Empty_N7()
    {
        var policyEmptyRef = new SchemePolicyInput(IsDemo: false, ApprovalStatus: "APPROVED", ApprovalReference: "   ", Formula: "COMPONENT_WEIGHTS");
        var components = new List<SchemeComponentInput>
        {
            SchemeComp("Hồ sơ chung", "COMMON", null, 18.75m, 15m, stage: "COLD")
        };
        var majors = new[] { MajorSe };

        Assert.Throws<ValidationException>(() => VersionedResultCalculator.Validate(policyEmptyRef, components, majors));
    }

    // ==========================================
    // 10. IsDemo = false, ApprovalStatus = "APPROVED", ApprovalReference = "QĐ 123/QĐ-ĐHX" => Validate pass
    // ==========================================
    [Fact]
    public void Test10_Validate_Succeeds_When_IsDemo_False_Approved_With_Valid_ApprovalReference()
    {
        var policyValidRef = new SchemePolicyInput(IsDemo: false, ApprovalStatus: "APPROVED", ApprovalReference: "QĐ 123/QĐ-ĐHX", Formula: "COMPONENT_WEIGHTS");
        var components = new List<SchemeComponentInput>
        {
            SchemeComp("Hồ sơ chung", "COMMON", null, 18.75m, 15m, stage: "COLD")
        };
        var majors = new[] { MajorSe };

        // Does not throw
        VersionedResultCalculator.Validate(policyValidRef, components, majors);
    }

    // ==========================================
    // 11. IsDemo = true nhưng ApprovalStatus = "APPROVED" => Validate throw — N7
    // ==========================================
    [Fact]
    public void Test11_Validate_Throws_When_IsDemo_True_With_ApprovalStatus_Approved_N7()
    {
        // Fake approval on demo policy violates N7
        var policyFakeApproval = new SchemePolicyInput(IsDemo: true, ApprovalStatus: "APPROVED", ApprovalReference: "QĐ 123/QĐ-ĐHX", Formula: "COMPONENT_WEIGHTS");
        var components = new List<SchemeComponentInput>
        {
            SchemeComp("Hồ sơ chung", "COMMON", null, 18.75m, 15m, stage: "COLD")
        };
        var majors = new[] { MajorSe };

        Assert.Throws<ValidationException>(() => VersionedResultCalculator.Validate(policyFakeApproval, components, majors));
    }

    // ==========================================
    // 12. Dự án liên ngành 2 ngành (8 component), SV ngành MKT lọc còn 5 => tổng đúng 100 — chứng minh ma trận không đổi khi bớt ngành
    // ==========================================
    [Fact]
    public void Test12_TwoMajor_Interdisciplinary_8Components_FilteredTo5_SumsTo100()
    {
        var policy = ComponentWeightsPolicy();

        // 8 components for 2 majors (SE and MKT):
        var all8Components = new List<ResultComponentScoreDto>
        {
            Component(1, "Hồ sơ chung", "COMMON", null, 15m, 8.0m, stage: "COLD"),
            Component(2, "Bảo vệ giải pháp chung", "COMMON", null, 35m, 9.0m, stage: "DEFENSE"),
            Component(3, "Hồ sơ ngành CNTT", "MAJOR_SPECIFIC", MajorSe, 15m, 8.5m, stage: "COLD"),
            Component(4, "Bảo vệ chuyên môn CNTT", "MAJOR_SPECIFIC", MajorSe, 15m, 7.5m, stage: "DEFENSE"),
            Component(5, "Hồ sơ ngành Marketing", "MAJOR_SPECIFIC", MajorMkt, 15m, 8.0m, stage: "COLD"),
            Component(6, "Bảo vệ chuyên môn Marketing", "MAJOR_SPECIFIC", MajorMkt, 15m, 8.0m, stage: "DEFENSE"),
            Component(7, "Vấn đáp cá nhân SV CNTT", "INDIVIDUAL", MajorSe, 20m, 9.5m, stage: "DEFENSE"),
            Component(8, "Vấn đáp cá nhân SV Marketing", "INDIVIDUAL", MajorMkt, 20m, 9.0m, stage: "DEFENSE")
        };

        Assert.Equal(150m, all8Components.Sum(c => c.WeightPercent));

        // Filtered for MKT student: #1, #2, #5, #6, #8
        var mktStudentComponents = all8Components
            .Where(c => c.Scope == "COMMON" || (c.MajorId == MajorMkt && (c.Scope == "MAJOR_SPECIFIC" || c.Scope == "INDIVIDUAL")))
            .ToList();

        Assert.Equal(5, mktStudentComponents.Count);
        Assert.Equal(100m, mktStudentComponents.Sum(c => c.WeightPercent));

        var total = VersionedResultCalculator.Total(mktStudentComponents, policy, student: true);

        Assert.NotNull(total);
        // 8.0 * 0.15 + 9.0 * 0.35 + 8.0 * 0.15 + 8.0 * 0.15 + 9.0 * 0.20
        // = 1.20 + 3.15 + 1.20 + 1.20 + 1.80 = 8.55
        Assert.Equal(8.55m, total.Value);
    }
}
