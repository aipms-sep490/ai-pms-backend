using AIPMS.Infrastructure.Services.Projects;

namespace AIPMS.UnitTests.AI;

public sealed class WorkflowAssistanceLabelsTests
{
    [Theory]
    [InlineData("PROJECT_NOT_ACTIVE", "Đồ án không ở giai đoạn đang thực hiện")]
    [InlineData("REQUIRED_DELIVERABLE_MISSING:123", "Có hạng mục bắt buộc chưa đủ")]
    [InlineData("MISSING_EVALUATOR:31", "Có cấu phần chưa được phân công")]
    public void Reason_hides_internal_suffixes(string code, string expected)
    {
        var text = WorkflowAssistanceLabels.Reason(code);
        Assert.Contains(expected, text);
        Assert.DoesNotContain("123", text);
        Assert.DoesNotContain("31", text);
    }

    [Fact]
    public void Unknown_reason_is_safe_and_actionable()
    {
        var text = WorkflowAssistanceLabels.Reason("A_NEW_INTERNAL_CODE:42");
        Assert.Contains("điều kiện nghiệp vụ", text);
        Assert.DoesNotContain("42", text);
    }
}
