using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Infrastructure.Services;
using ClosedXML.Excel;
using UglyToad.PdfPig;

namespace AIPMS.UnitTests.AI;

public sealed class AssistantStudyToolsTests
{
    private static AcademicAssistantResponseDto Answer(string text) => new(text, "GROUNDED", [],
        [new("RUBRIC", "private-id", "Rubric kỹ thuật · v2", null, null, "Kiểm thử và bảo mật")], false,
        "Bản nháp, không phải điểm chính thức.", "COMPLETED", DateTime.UtcNow);

    [Fact]
    public void ExportsPreserveVietnameseSourcesAndNeverTreatTextAsFormulaOrHtml()
    {
        var answer = Answer("## Báo cáo tuần\nHoàn thành kiểm thử đăng nhập.\n=HYPERLINK(\"https://example.test\")\n<script>alert('x')</script>");
        using var doc = new ZipArchive(new MemoryStream(AssistantDocumentExporter.Create(answer, "docx").Content));
        using var reader = new StreamReader(doc.GetEntry("word/document.xml")!.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains("Báo cáo tuần", xml); Assert.Contains("Rubric kỹ thuật", xml); Assert.Contains("&lt;script&gt;", xml);
        using var word = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(new MemoryStream(AssistantDocumentExporter.Create(answer, "docx").Content), false);
        Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(word));
        using var book = new XLWorkbook(new MemoryStream(AssistantDocumentExporter.Create(answer, "xlsx").Content));
        Assert.All(book.Worksheet(1).CellsUsed(), cell => Assert.False(cell.HasFormula));
        Assert.Contains(book.Worksheet(1).CellsUsed(), cell => cell.GetString().Contains("=HYPERLINK"));
        using var pdf = PdfDocument.Open(AssistantDocumentExporter.Create(answer, "pdf").Content);
        Assert.Contains("Báo cáo tuần", string.Join(" ", pdf.GetPages().Select(p => p.Text)));
        foreach (var format in new[] { "txt", "md" }) Assert.Contains("Rubric kỹ thuật", Encoding.UTF8.GetString(AssistantDocumentExporter.Create(answer, format).Content));
    }

    [Fact]
    public void PdfPaginatesLongVietnameseContentWithoutDroppingItsEnding()
    {
        var text = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"Dòng {i}: Kiểm thử báo cáo và dữ liệu đồ án."));
        using var pdf = PdfDocument.Open(AssistantDocumentExporter.Create(Answer(text), "pdf").Content);
        Assert.True(pdf.NumberOfPages > 1);
        Assert.Contains("Dòng 200", string.Join(" ", pdf.GetPages().Select(p => p.Text)));
    }

    [Fact]
    public void ProgressLabelsPartialDataAndExcludesDoneAndCancelledTasks()
    {
        TaskEvidenceItem Item(string name, string status, bool overdue) => new(name, 1, name, status, null, "2026-10-20 12:00", overdue, false, null);
        var payload = new ProjectEvidencePayload(1, "ACTIVE", 30, 3, true, [],
            [Item("Cần sửa API", "TODO", true), Item("Đã xong", "DONE", true), Item("Đã hủy", "CANCELLED", true)], [], [], null);
        var context = new ProjectBoundedContext(1, "ACTIVE", [], JsonSerializer.Serialize(payload), true, 3, 30, 3, true);
        var summary = AcademicKnowledgeService.ProgressSummary(context);
        Assert.Contains("3/30", summary); Assert.Contains("một phần dữ liệu", summary); Assert.Contains("Cần sửa API: quá hạn", summary);
        Assert.DoesNotContain("Đã xong", summary); Assert.DoesNotContain("Đã hủy", summary);
    }
}
