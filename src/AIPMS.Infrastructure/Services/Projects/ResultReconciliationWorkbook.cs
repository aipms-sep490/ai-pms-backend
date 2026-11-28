using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed record ResultReconciliationExportRow(
    string ProjectCode,
    string ProjectTitle,
    string TrackCode,
    string ProjectStatus,
    string StudentCode,
    string StudentName,
    string StudentEmail,
    string MajorCode,
    decimal? Gate1Score,
    decimal? Gate2Score,
    decimal? Gate3Score,
    decimal? ProcessWeighted20,
    string EvaluationsSummary80,
    int? ResultVersion,
    decimal? TotalScore,
    string Outcome,
    string? OutcomeDetail,
    bool HasActiveDefenseAbsence,
    int AppealCount,
    int AcceptedAppealCount,
    DateTime? PublishedAtUtc);

internal static class ResultReconciliationWorkbook
{
    private static readonly string[] Headers =
    [
        "STT",
        "Mã đồ án",
        "Tên đồ án",
        "Track",
        "Trạng thái ĐA",
        "MSSV",
        "Họ và tên",
        "Email",
        "Ngành",
        "Gate 1 (QT)",
        "Gate 2 (QT)",
        "Gate 3 (QT)",
        "Quá trình 20%",
        "Chi tiết thành phần 80%",
        "Phiên bản KQ",
        "Điểm tổng kết",
        "Kết quả",
        "Vắng bảo vệ",
        "Đơn phúc khảo",
        "Đơn chấp thuận",
        "Công bố lúc (UTC)"
    ];

    public static byte[] CreateXlsx(IReadOnlyList<ResultReconciliationExportRow> rows)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Doi soat ket qua");

        double[] widths = [7, 16, 30, 12, 16, 15, 26, 30, 12, 12, 12, 12, 14, 34, 13, 14, 20, 14, 14, 15, 22];
        for (var c = 1; c <= Headers.Length; c++)
        {
            sheet.Cell(1, c).Value = Headers[c - 1];
            sheet.Column(c).Width = widths[c - 1];
        }

        var headerRange = sheet.Range(1, 1, 1, Headers.Length);
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1D4ED8");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Row(1).Height = 34;
        sheet.SheetView.FreezeRows(1);

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var rowNum = i + 2;
            if ((i + 1) % 2 == 0)
                sheet.Range(rowNum, 1, rowNum, Headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");

            sheet.Cell(rowNum, 1).Value = i + 1;
            SetText(sheet.Cell(rowNum, 2), r.ProjectCode);
            SetText(sheet.Cell(rowNum, 3), r.ProjectTitle);
            SetText(sheet.Cell(rowNum, 4), r.TrackCode);
            SetText(sheet.Cell(rowNum, 5), r.ProjectStatus);
            SetText(sheet.Cell(rowNum, 6), r.StudentCode);
            SetText(sheet.Cell(rowNum, 7), r.StudentName);
            SetText(sheet.Cell(rowNum, 8), r.StudentEmail);
            SetText(sheet.Cell(rowNum, 9), r.MajorCode);
            SetNumber(sheet.Cell(rowNum, 10), r.Gate1Score);
            SetNumber(sheet.Cell(rowNum, 11), r.Gate2Score);
            SetNumber(sheet.Cell(rowNum, 12), r.Gate3Score);
            SetNumber(sheet.Cell(rowNum, 13), r.ProcessWeighted20);
            SetText(sheet.Cell(rowNum, 14), r.EvaluationsSummary80);
            SetText(sheet.Cell(rowNum, 15), r.ResultVersion.HasValue ? $"v{r.ResultVersion.Value}" : "-");
            SetNumber(sheet.Cell(rowNum, 16), r.TotalScore);
            SetText(sheet.Cell(rowNum, 17), string.IsNullOrWhiteSpace(r.OutcomeDetail) ? r.Outcome : $"{r.Outcome} ({r.OutcomeDetail})");
            SetText(sheet.Cell(rowNum, 18), r.HasActiveDefenseAbsence ? "Có" : "Không");
            sheet.Cell(rowNum, 19).Value = r.AppealCount;
            sheet.Cell(rowNum, 20).Value = r.AcceptedAppealCount;
            SetText(sheet.Cell(rowNum, 21), r.PublishedAtUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "");
            sheet.Row(rowNum).Height = 26;
        }

        var range = sheet.Range(1, 1, rows.Count + 1, Headers.Length);
        range.Style.Font.FontName = "Calibri";
        range.Style.Font.FontSize = 11;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
        range.Style.Border.BottomBorderColor = XLColor.FromHtml("#CBD5E1");
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(1, 1);

        using var ms = new MemoryStream();
        book.SaveAs(ms);
        return ms.ToArray();
    }

    public static byte[] CreateCsv(IReadOnlyList<ResultReconciliationExportRow> rows, DateTime utcNow)
    {
        var sb = new StringBuilder();
        AppendCsvRow(sb,
            "projectCode", "projectTitle", "trackCode", "projectStatus",
            "studentCode", "studentName", "studentEmail", "majorCode",
            "gate1Score", "gate2Score", "gate3Score", "processWeighted20",
            "evaluationsSummary80", "resultVersion", "totalScore", "outcome", "outcomeDetail",
            "hasActiveDefenseAbsence", "appealCount", "acceptedAppealCount", "publishedAtUtc", "generatedAtUtc");

        var generatedAt = utcNow.ToString("O", CultureInfo.InvariantCulture);
        foreach (var r in rows)
        {
            AppendCsvRow(sb,
                SafeCsvText(r.ProjectCode),
                SafeCsvText(r.ProjectTitle),
                SafeCsvText(r.TrackCode),
                SafeCsvText(r.ProjectStatus),
                SafeCsvText(r.StudentCode),
                SafeCsvText(r.StudentName),
                SafeCsvText(r.StudentEmail),
                SafeCsvText(r.MajorCode),
                FormatDecimal(r.Gate1Score),
                FormatDecimal(r.Gate2Score),
                FormatDecimal(r.Gate3Score),
                FormatDecimal(r.ProcessWeighted20),
                SafeCsvText(r.EvaluationsSummary80),
                r.ResultVersion?.ToString(CultureInfo.InvariantCulture) ?? "",
                FormatDecimal(r.TotalScore),
                SafeCsvText(r.Outcome),
                SafeCsvText(r.OutcomeDetail ?? ""),
                r.HasActiveDefenseAbsence ? "true" : "false",
                r.AppealCount.ToString(CultureInfo.InvariantCulture),
                r.AcceptedAppealCount.ToString(CultureInfo.InvariantCulture),
                r.PublishedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "",
                generatedAt);
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static void SetText(IXLCell cell, string value)
    {
        cell.Style.NumberFormat.Format = "@";
        cell.Value = value;
    }

    private static void SetNumber(IXLCell cell, decimal? value)
    {
        if (value.HasValue)
        {
            cell.Style.NumberFormat.Format = "0.00";
            cell.Value = value.Value;
        }
        else
        {
            SetText(cell, "-");
        }
    }

    private static string FormatDecimal(decimal? value) =>
        value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";

    private static string SafeCsvText(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
    }

    private static void AppendCsvRow(StringBuilder sb, params string[] fields) =>
        sb.Append(string.Join(",", fields.Select(f => $"\"{f.Replace("\"", "\"\"")}\""))).Append("\r\n");
}
