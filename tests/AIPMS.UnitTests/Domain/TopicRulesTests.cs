using AIPMS.Domain.Topics;

namespace AIPMS.UnitTests.Domain;

public sealed class TopicRulesTests
{
    [Theory]
    [InlineData("  Hệ thống Quản lý   Đồ án AI  ", "he thong quan ly do an ai")]
    [InlineData("Đánh giá điểm ĐỒ ÁN tốt nghiệp", "danh gia diem do an tot nghiep")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void NormalizeTitle_strips_vietnamese_diacritics_collapses_spaces_and_lowercases(string? raw, string expected)
    {
        Assert.Equal(expected, TopicRules.NormalizeTitle(raw));
    }

    [Fact]
    public void CalculateSimilarity_computes_jaccard_over_normalized_keywords_and_domain()
    {
        var score = TopicRules.CalculateSimilarity(
            ["Parking", "Sensor", "Camera"],
            "IoT",
            ["parking", "sensor"],
            "IoT");

        Assert.True(score >= TopicRules.SimilarityWarningThreshold);
        Assert.Equal(0.75, score);
    }

    [Fact]
    public void ProposalCompletenessIssues_flags_each_missing_field_for_student_proposal_only()
    {
        var issues = TopicRules.ProposalCompletenessIssues(
            "STUDENT_PROPOSAL",
            " ",
            "Objectives",
            null,
            "Education",
            [],
            ["AI"]);

        Assert.Equal(
            [
                "PROPOSAL_CONTENT_INCOMPLETE:problemStatement",
                "PROPOSAL_CONTENT_INCOMPLETE:expectedOutput",
                "PROPOSAL_CONTENT_INCOMPLETE:technologies"
            ],
            issues);

        Assert.Empty(TopicRules.ProposalCompletenessIssues(
            "STUDENT_PROPOSAL",
            "Problem",
            "Objectives",
            "Expected output",
            "Education",
            [".NET"],
            ["AI"]));

        Assert.Empty(TopicRules.ProposalCompletenessIssues(
            "PUBLISHED_TOPIC",
            null,
            null,
            null,
            null,
            null,
            null));
    }
}
