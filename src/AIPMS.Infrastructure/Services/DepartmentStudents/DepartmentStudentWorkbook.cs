using System.IO;
using AIPMS.Application.Features.DepartmentStudents;
using ClosedXML.Excel;

namespace AIPMS.Infrastructure.Services.DepartmentStudents;

internal sealed record AdminUserAccountExportRow(
    string? Code,
    string FullName,
    string Email,
    string? Phone,
    string Roles,
    string? DepartmentName,
    string? MajorName,
    string? ClassCode,
    string? SpecializationName,
    string? CurriculumCode,
    string AccountStatus,
    string AcademicProfileStatus,
    DateTime? LastLoginAt);

internal static class DepartmentStudentWorkbook
{
    public static byte[] CreateDepartmentStudentsWorkbook(IReadOnlyList<DepartmentStudentSummaryDto> rows)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Danh sach sinh vien");
        string[] headers =
        [
            "STT", "MSSV", "Họ và tên", "Email", "SĐT",
            "Chuyên ngành", "Lớp", "Chuyên ngành hẹp", "Khung CTĐT",
            "Trạng thái TK", "Hồ sơ học vụ", "Chứng chỉ ĐK", "Mã nhóm", "Vai trò nhóm"
        ];
        double[] widths = [7, 16, 28, 34, 16, 24, 14, 24, 22, 16, 16, 18, 16, 16];
        SetupHeader(sheet, headers, widths);

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var rowNum = i + 2;
            string[] values =
            [
                r.StudentCode ?? "",
                r.FullName,
                r.Email,
                r.Phone ?? "",
                r.MajorName ?? r.MajorCode ?? "",
                r.ClassCode ?? "",
                r.SpecializationName ?? r.SpecializationCode ?? "",
                r.CurriculumCode ?? "",
                r.Status,
                r.AcademicProfileStatus,
                r.QualificationStatus,
                r.TeamCode ?? "Chưa có nhóm",
                r.TeamId.HasValue ? (r.IsTeamLeader ? "Trưởng nhóm" : "Thành viên") : ""
            ];
            WriteSafeRow(sheet, rowNum, i + 1, values, widths);
        }

        FinalizeSheet(sheet, headers.Length, rows.Count);
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] CreateAdminAccountsWorkbook(IReadOnlyList<AdminUserAccountExportRow> rows)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Danh sach tai khoan");
        string[] headers =
        [
            "STT", "MSSV / Mã NV", "Họ và tên", "Email", "SĐT", "Vai trò",
            "Khoa / Bộ môn", "Chuyên ngành", "Lớp", "Chuyên ngành hẹp", "Khung CTĐT",
            "Trạng thái TK", "Hồ sơ học vụ", "Đăng nhập gần nhất (UTC)"
        ];
        double[] widths = [7, 16, 28, 34, 16, 22, 24, 24, 14, 24, 22, 16, 16, 22];
        SetupHeader(sheet, headers, widths);

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var rowNum = i + 2;
            string[] values =
            [
                r.Code ?? "",
                r.FullName,
                r.Email,
                r.Phone ?? "",
                r.Roles,
                r.DepartmentName ?? "",
                r.MajorName ?? "",
                r.ClassCode ?? "",
                r.SpecializationName ?? "",
                r.CurriculumCode ?? "",
                r.AccountStatus,
                r.AcademicProfileStatus,
                r.LastLoginAt?.ToString("yyyy-MM-dd HH:mm") ?? ""
            ];
            WriteSafeRow(sheet, rowNum, i + 1, values, widths);
        }

        FinalizeSheet(sheet, headers.Length, rows.Count);
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private static void SetupHeader(IXLWorksheet sheet, string[] headers, double[] widths)
    {
        for (var c = 1; c <= headers.Length; c++)
        {
            sheet.Cell(1, c).Value = headers[c - 1];
            sheet.Column(c).Width = widths[c - 1];
        }
        var headerRange = sheet.Range(1, 1, 1, headers.Length);
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1D4ED8");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Row(1).Height = 36;
        sheet.SheetView.FreezeRows(1);
    }

    private static void WriteSafeRow(IXLWorksheet sheet, int rowNum, int index, string[] values, double[] widths)
    {
        sheet.Cell(rowNum, 1).Value = index;
        if (index % 2 == 0)
            sheet.Range(rowNum, 1, rowNum, values.Length + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
        for (var c = 0; c < values.Length; c++)
        {
            var cell = sheet.Cell(rowNum, c + 2);
            cell.Style.NumberFormat.Format = "@";
            cell.Value = values[c];
        }
        var lines = values.Select((v, i) => Math.Max(1, (int)Math.Ceiling(v.Length / (widths[i + 1] * 0.85)))).Max();
        sheet.Row(rowNum).Height = Math.Min(200, Math.Max(26, lines * 15 + 6));
    }

    private static void FinalizeSheet(IXLWorksheet sheet, int colCount, int rowCount)
    {
        var range = sheet.Range(1, 1, rowCount + 1, colCount);
        range.Style.Font.FontName = "Calibri";
        range.Style.Font.FontSize = 11;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Hair;
        range.Style.Border.BottomBorderColor = XLColor.FromHtml("#CBD5E1");
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(1, 1);
    }
}
