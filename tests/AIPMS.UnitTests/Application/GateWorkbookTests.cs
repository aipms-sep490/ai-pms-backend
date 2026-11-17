using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Infrastructure.Services.Projects;
using ClosedXML.Excel;

namespace AIPMS.UnitTests.Application;

public sealed class GateWorkbookTests
{
    private static ReviewGateTemplateDto Draft => new("5", 11, "GATE_POLICY", "Mẫu tiến trình", "SINGLE_MAJOR", "COMMON",
        "DRAFT", "1", "token-1", 1,
        [new("20", "R1", 1, "Gate 1", "Review evidence", true, true, true, null, null,
            null, null, "TWO_REVIEWERS", "COMMON",
            [new("30", "REPORT", "Báo cáo", "Tải tệp PDF", "FILE", "COMMON", null, null, "HUMAN", true)], 100m)]);

    [Fact]
    public void Export_import_roundtrip_preserves_gate_requirements_and_weights()
    {
        var workbook = ReviewGateService.ExportWorkbook(Draft);
        var preview = ReviewGateService.PreviewWorkbook(Draft, workbook);
        Assert.Empty(preview.Issues);
        var gate = Assert.Single(preview.Draft!.Gates);
        Assert.Equal(100m, gate.Weight);
        Assert.Equal("REPORT", Assert.Single(gate.Requirements).Code);
        Assert.Equal("token-1", preview.Draft.ConcurrencyToken);
    }

    [Fact]
    public void Import_reports_bad_weight_duplicate_code_and_formula_without_committing()
    {
        using var book = new XLWorkbook(new MemoryStream(ReviewGateService.ExportWorkbook(Draft)));
        book.Worksheet("Gate").Cell("L2").Value = 90;
        book.Worksheet("Yêu cầu").Cell("B3").Value = "REPORT";
        book.Worksheet("Yêu cầu").Cell("A3").Value = "R1";
        book.Worksheet("Yêu cầu").Cell("C3").Value = "Lặp";
        book.Worksheet("Yêu cầu").Cell("F3").Value = "COMMON";
        book.Worksheet("Yêu cầu").Cell("J3").Value = "Có";
        using var stream = new MemoryStream(); book.SaveAs(stream);
        var invalid = ReviewGateService.PreviewWorkbook(Draft, stream.ToArray());
        Assert.Null(invalid.Draft);
        Assert.Contains(invalid.Issues, i => i.Cell == "Gate!L");
        book.Worksheet("Gate").Cell("L2").Value = 100;
        book.Worksheet("Gate").Cell("D2").FormulaA1 = "=1+1";
        using var formulaStream = new MemoryStream(); book.SaveAs(formulaStream);
        Assert.Contains(ReviewGateService.PreviewWorkbook(Draft, formulaStream.ToArray()).Issues,
            i => i.Message.Contains("công thức"));
    }

    [Fact]
    public void Import_rejects_an_old_template_token()
    {
        var bytes = ReviewGateService.ExportWorkbook(Draft);
        var current = Draft with { ConcurrencyToken = "token-2" };
        var conflict = Assert.Throws<ConflictException>(() => ReviewGateService.PreviewWorkbook(current, bytes));
        Assert.Equal("GATE_WORKBOOK_STALE", conflict.Code);
    }

    [Fact]
    public void Export_import_preserves_utc_gate_window()
    {
        var start = new DateTime(2026, 11, 14, 8, 0, 0, DateTimeKind.Utc);
        var gate = Draft.Gates.Single() with { Start = start, End = start.AddDays(7) };
        var source = Draft with { Gates = [gate] };
        var preview = ReviewGateService.PreviewWorkbook(source, ReviewGateService.ExportWorkbook(source));
        Assert.Empty(preview.Issues);
        Assert.Equal(start, Assert.Single(preview.Draft!.Gates).Start);
    }
}
