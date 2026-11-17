using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Infrastructure.Services.Projects;
using ClosedXML.Excel;

namespace AIPMS.UnitTests.Application;

public sealed class RubricWorkbookTests
{
    private static RubricDto Draft() => new(17, 2, 3, "RUBRIC", "Rubric draft", null, "DRAFT", false,
        17, 1, "token-17", true, DateTime.UtcNow, DateTime.UtcNow,
        [new RubricCriterionDto(1, 1, "Report quality", null, 100, 10, 0, true)]);

    [Fact]
    public void Exported_workbook_previews_into_an_editable_draft_with_current_token()
    {
        var bytes = RubricWorkbookService.Export(Draft());
        var preview = RubricWorkbookService.Preview(Draft(), bytes);
        Assert.Empty(preview.Issues);
        Assert.Equal("token-17", preview.Draft!.ConcurrencyToken);
        Assert.Equal(100m, Assert.Single(preview.Draft.Criteria).WeightPercent);
        Assert.Equal("Report quality", Assert.Single(preview.Draft.Criteria).Name);
    }

    [Fact]
    public void Import_preview_rejects_bad_weights_formulas_and_stale_workbook()
    {
        var bytes = RubricWorkbookService.Export(Draft());
        byte[] Change(Action<XLWorkbook> change)
        {
            using var book = new XLWorkbook(new MemoryStream(bytes));
            change(book);
            using var output = new MemoryStream(); book.SaveAs(output); return output.ToArray();
        }
        var invalidWeight = RubricWorkbookService.Preview(Draft(), Change(b => b.Worksheet("Tiêu chí").Cell("E2").Value = 90));
        Assert.Null(invalidWeight.Draft);
        Assert.Contains(invalidWeight.Issues, issue => issue.Cell == "Tiêu chí!E:G");
        var formula = RubricWorkbookService.Preview(Draft(), Change(b => b.Worksheet("Tiêu chí").Cell("C2").FormulaA1 = "1+1"));
        Assert.Null(formula.Draft);
        Assert.Contains(formula.Issues, issue => issue.Message.Contains("công thức"));
        Assert.Throws<ConflictException>(() => RubricWorkbookService.Preview(Draft(),
            Change(b => b.Worksheet("Thông tin").Cell("B2").Value = "stale")));
    }
}
