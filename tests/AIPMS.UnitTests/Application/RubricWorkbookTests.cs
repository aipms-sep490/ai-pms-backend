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

    [Fact]
    public void Export_includes_data_validations_and_preview_supports_0001_tolerance_and_staged_rows()
    {
        var rubric = new RubricDto(21, 2, 3, "RUB_V4", "Rubric V4", "Desc", "DRAFT", false, 21, 1, "tok-21", true,
            DateTime.UtcNow, DateTime.UtcNow,
            [
                new RubricCriterionDto(1, 1, "Integration API", null, 50m, 10m, 0, true) { TargetScope = "PROJECT", IsIntegration = true },
                new RubricCriterionDto(2, 2, "Architecture", null, 50m, 10m, 1, true) { TargetScope = "MAJOR", IsIntegration = false }
            ])
        { EvaluationScope = "COMMON", EvaluationStage = "DEFENSE", ProjectMode = "INTERDISCIPLINARY" };

        var bytes = RubricWorkbookService.Export(rubric);
        using (var book = new XLWorkbook(new MemoryStream(bytes)))
        {
            var sheet = book.Worksheet("Tiêu chí");
            Assert.NotEmpty(sheet.DataValidations);
            Assert.Equal("PROJECT", sheet.Cell("K2").GetString());
            Assert.Equal("Có", sheet.Cell("L2").GetString());
        }

        // Round-trip preserves TargetScope & IsIntegration
        var okPreview = RubricWorkbookService.Preview(rubric, bytes);
        Assert.Empty(okPreview.Issues);
        Assert.Equal(2, okPreview.StagedRows.Count);
        Assert.True(okPreview.Draft!.Criteria[0].IsIntegration);
        Assert.Equal("PROJECT", okPreview.Draft.Criteria[0].TargetScope);

        // Weight sum within ±0.001 tolerance passes (33.333 * 3 = 99.999)
        byte[] Mutate(Action<XLWorkbook> fn)
        {
            using var book = new XLWorkbook(new MemoryStream(bytes));
            fn(book);
            using var ms = new MemoryStream();
            book.SaveAs(ms);
            return ms.ToArray();
        }
        var withinTolerance = RubricWorkbookService.Preview(rubric, Mutate(b =>
        {
            var s = b.Worksheet("Tiêu chí");
            s.Cell("E2").Value = 33.333m;
            s.Cell("E3").Value = 33.333m;
            s.Cell("A4").Value = "C3"; s.Cell("B4").Value = ""; s.Cell("C4").Value = "Third";
            s.Cell("E4").Value = 33.333m; s.Cell("F4").Value = 10m; s.Cell("G4").Value = 2; s.Cell("H4").Value = "Có";
        }));
        Assert.Empty(withinTolerance.Issues);
        Assert.NotNull(withinTolerance.Draft);

        // Outside ±0.001 tolerance fails AND returns StagedRows flagged with HasError = true
        var outsideTolerance = RubricWorkbookService.Preview(rubric, Mutate(b =>
        {
            var s = b.Worksheet("Tiêu chí");
            s.Cell("E2").Value = 49.99m;
            s.Cell("E3").Value = 50.00m;
        }));
        Assert.Null(outsideTolerance.Draft);
        Assert.NotEmpty(outsideTolerance.Issues);
        Assert.Equal(2, outsideTolerance.StagedRows.Count);
        Assert.All(outsideTolerance.StagedRows, r => Assert.True(r.HasError));

        // Published rubric rejects import with 409 IMMUTABLE_VERSION
        var published = rubric with { Status = "PUBLISHED", CanEdit = false };
        var ex = Assert.Throws<ConflictException>(() => RubricWorkbookService.Preview(published, bytes));
        Assert.Equal(WorkflowErrorCodes.ImmutableVersion, ex.Code);
    }
}
