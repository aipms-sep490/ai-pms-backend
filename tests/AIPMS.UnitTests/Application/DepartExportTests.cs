using System.IO.Compression;
using System.Xml.Linq;
using AIPMS.AI.Services;
using AIPMS.Application.Features.Dashboards.Abstractions;
using AIPMS.Application.Features.Dashboards.Queries;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Models;
using PdfSharp.Pdf.IO;

namespace AIPMS.UnitTests.Application;

public sealed class DepartExportTests
{
    private static (DashboardProjectFacts Project, ProjectProgressAnalysisDto Analysis)[] Rows(int count)
    {
        var now = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        var analysis = new RuleBasedProgressAnalysisService();
        return Enumerable.Range(1, count).Select(i =>
        {
            var facts = new ProjectProgressFacts(i, "ACTIVE", i, 2, [], [], [], []);
            var project = new DashboardProjectFacts(i, $"PROJECT-{i}", "D\u1ef1 \u00e1n li\u00ean ng\u00e0nh <test> & research",
                "ACTIVE", i, 1, 0, facts);
            return (project, analysis.Analyze(facts, now, default));
        }).ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(120)]
    public void Pdf_export_opens_and_paginates_without_truncating_rows(int count)
    {
        var bytes = DashboardPdfWriter.Write(Rows(count));
        using var input = new MemoryStream(bytes);
        using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount >= (count == 0 ? 1 : 10));
        Assert.Equal("AI-PMS portfolio export", pdf.Info.Title);
        // A ToUnicode map and embedded font preserve Vietnamese text on Linux as well as Windows.
        var raw = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.Contains("/ToUnicode", raw);
        Assert.Contains("/FontFile2", raw);
    }

    [Fact]
    public void Xlsx_has_all_rows_and_keeps_text_as_text_without_formula_execution()
    {
        var rows = Rows(120);
        rows[0] = (rows[0].Project with { Title = "=HYPERLINK(\"https://example.test\")" }, rows[0].Analysis);
        using var zip = new ZipArchive(new MemoryStream(DashboardSpreadsheetWriter.Write(rows)));
        using var sheetStream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var sheet = XDocument.Load(sheetStream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Assert.Equal(121, sheet.Descendants(ns + "row").Count());
        Assert.Empty(sheet.Descendants(ns + "f"));
        Assert.Contains(sheet.Descendants(ns + "t"), t => t.Value.Contains("<test> & research"));
        Assert.Contains(sheet.Descendants(ns + "t"), t => t.Value.Contains("HYPERLINK"));
    }
}
