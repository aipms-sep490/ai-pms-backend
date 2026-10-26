using System.Globalization;
using AIPMS.Application.Features.Dashboards.DTOs;
using AIPMS.Application.Features.Dashboards.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace AIPMS.Application.Features.Dashboards.Queries;

internal static class DashboardPdfWriter
{
    static DashboardPdfWriter() => AIPMS.Application.Common.Documents.UnicodePdf.InitializeFonts();

    public static byte[] Write(IEnumerable<(DashboardProjectFacts Project, ProjectProgressAnalysisDto Analysis)> source)
    {
        var rows = source.OrderByDescending(x => x.Project.CreatedAt).ThenByDescending(x => x.Project.Id).ToArray();
        using var document = new PdfDocument();
        document.Info.Title = "AI-PMS portfolio export";
        var font = new XFont("Portfolio", 10, XFontStyleEx.Regular,
            new XPdfFontOptions(PdfFontEncoding.Unicode));
        XGraphics? graphics = null;
        double y = 0;
        const double margin = 36, width = 520, lineHeight = 15;
        void Page()
        {
            graphics?.Dispose();
            var page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            graphics = XGraphics.FromPdfPage(page);
            y = margin;
            graphics.DrawString($"AI-PMS portfolio | Page {document.PageCount}", font, XBrushes.Black, margin, y);
            y += lineHeight * 2;
        }
        void Line(string text)
        {
            if (y > 790) Page();
            graphics!.DrawString(text, font, XBrushes.Black, margin, y);
            y += lineHeight;
        }
        void Wrapped(string text)
        {
            // Split by grapheme so long identifiers and Vietnamese combining marks survive wrapping.
            var line = "";
            var characters = StringInfo.GetTextElementEnumerator(text.Replace('\r', ' ').Replace('\n', ' '));
            while (characters.MoveNext())
            {
                var next = characters.GetTextElement();
                if (line.Length > 0 && graphics!.MeasureString(line + next, font).Width > width)
                {
                    Line(line);
                    line = "";
                }
                line += next;
            }
            Line(line);
        }
        try
        {
            Page();
            Line($"Projects: {rows.Length} | Tasks: {rows.Sum(x => x.Analysis.ProgressSummary.TotalTasks)} | Done: {rows.Sum(x => x.Analysis.ProgressSummary.DoneTasks)}");
            foreach (var (project, analysis) in rows)
            {
                Wrapped($"{project.Id} | {project.Code} | {project.Title}");
                Wrapped(FormattableString.Invariant($"{project.Status} | Semester {project.SemesterId} | {analysis.RiskLevel} | Progress {analysis.ProgressSummary.ProgressPercentage:0.##}%"));
                Wrapped($"Majors: {string.Join(", ", (project.Majors ?? []).Select(m => m.Code))} | Supervisor: {project.Supervisor?.Name ?? "-"}");
                Line($"Tasks: {analysis.ProgressSummary.TotalTasks} | Done: {analysis.ProgressSummary.DoneTasks} | Blocked: {analysis.ProgressSummary.BlockedTasks} | Overdue: {analysis.ProgressSummary.OverdueTasks}");
                y += lineHeight;
            }
        }
        finally { graphics?.Dispose(); }
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

}
