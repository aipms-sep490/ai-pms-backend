using System.IO;
using ClosedXML.Excel;

namespace AIPMS.Infrastructure.Services.StudentRosters;

internal sealed record TeamRosterRow(long TeamId, string TeamCode, long UserId, bool IsLeader,
    string? StudentCode, string FullName, string? Phone, string Email, string? CurriculumCode);

internal static class TeamRosterWorkbook
{
    public static byte[] Create(IReadOnlyList<TeamRosterRow> rows)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Danh sach nhom");
        string[] headers = ["STT", "Mã nhóm", "MSSV", "Họ và tên", "Trưởng nhóm/Thành viên", "SĐT", "Email", "Khung"];
        double[] widths = [7, 16, 18, 32, 25, 19, 42, 30];
        for (var c = 1; c <= headers.Length; c++)
        {
            sheet.Cell(1, c).Value = headers[c - 1];
            sheet.Column(c).Width = widths[c - 1];
        }
        var range = sheet.Range(1, 1, rows.Count + 1, 8);
        range.Style.Font.FontName = "Calibri";
        range.Style.Font.FontSize = 11;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
        range.Style.Border.BottomBorderColor = XLColor.FromHtml("#CBD5E1");
        sheet.Range(1, 1, 1, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#12568C");
        sheet.Range(1, 1, 1, 8).Style.Font.FontColor = XLColor.White;
        sheet.Range(1, 1, 1, 8).Style.Font.Bold = true;
        sheet.Range(1, 1, 1, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Row(1).Height = 42;
        sheet.SheetView.FreezeRows(1);
        var rowNumber = 2;
        var groupNumber = 0;
        foreach (var team in rows.GroupBy(r => r.TeamId))
        {
            var first = rowNumber;
            foreach (var row in team)
            {
                sheet.Row(rowNumber).Height = 32;
                sheet.Range(rowNumber, 1, rowNumber, 8).Style.Fill.BackgroundColor =
                    XLColor.FromHtml(groupNumber % 2 == 0 ? "#E2EBFA" : "#FFFFFF");
                sheet.Cell(rowNumber, 1).Value = rowNumber - 1;
                string[] values = [row.TeamCode, row.StudentCode ?? "", row.FullName,
                    row.IsLeader ? "Trưởng nhóm" : "Thành viên", row.Phone ?? "", row.Email, row.CurriculumCode ?? ""];
                for (var c = 2; c <= 8; c++)
                {
                    // Assign a string value, never FormulaA1: source data is untrusted text.
                    sheet.Cell(rowNumber, c).Style.NumberFormat.Format = "@";
                    sheet.Cell(rowNumber, c).Value = values[c - 2];
                }
                sheet.Cell(rowNumber, 5).Style.Fill.BackgroundColor = XLColor.FromHtml(row.IsLeader ? "#FFD1CA" : "#D5EDB7");
                sheet.Cell(rowNumber, 5).Style.Font.FontColor = XLColor.FromHtml(row.IsLeader ? "#9A291E" : "#225E32");
                rowNumber++;
            }
            if (rowNumber - first > 1) sheet.Range(first, 2, rowNumber - 1, 2).Merge();
            sheet.Cell(first, 2).Style.Font.Bold = true;
            sheet.Cell(first, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            groupNumber++;
        }
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(1, 1);
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }
}
