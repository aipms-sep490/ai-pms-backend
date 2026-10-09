using AIPMS.Infrastructure.Services.StudentRosters;
using ClosedXML.Excel;

namespace AIPMS.UnitTests.StudentRosters;

public sealed class TeamRosterWorkbookTests
{
    [Fact]
    public void Workbook_has_eight_columns_merged_teams_and_safe_text_cells()
    {
        TeamRosterRow[] rows = [
            new(1, "SE_01", 1, true, "001234", "Nguyễn Văn An", "0123456789", "an@example.test", "BIT_SE_18D_Java"),
            new(1, "SE_01", 2, false, "001235", "=HYPERLINK(\"https://example.test\")", "+123", "@test", "=1+1"),
            new(2, "SE_02", 3, true, "001236", "Trần Bảo", null, "bao@example.test", null)
        ];
        using var book = new XLWorkbook(new MemoryStream(TeamRosterWorkbook.Create(rows)));
        var sheet = book.Worksheet(1);
        Assert.Equal(8, sheet.LastColumnUsed()!.ColumnNumber());
        Assert.Equal("Khung", sheet.Cell("H1").GetString());
        Assert.Equal("001234", sheet.Cell("C2").GetString());
        Assert.Equal("0123456789", sheet.Cell("F2").GetString());
        Assert.Equal("Nguyễn Văn An", sheet.Cell("D2").GetString());
        Assert.Equal("Trưởng nhóm", sheet.Cell("E2").GetString());
        Assert.Equal("Thành viên", sheet.Cell("E3").GetString());
        Assert.Equal("=1+1", sheet.Cell("H3").GetString());
        Assert.All(sheet.CellsUsed(), c => Assert.False(c.HasFormula));
        Assert.Equal(XLDataType.Text, sheet.Cell("C2").DataType);
        Assert.Equal("B2:B3", Assert.Single(sheet.MergedRanges).RangeAddress.ToString());
        Assert.Equal(1, sheet.SheetView.SplitRow);
        Assert.NotEqual(sheet.Cell("E2").Style.Fill.BackgroundColor, sheet.Cell("E3").Style.Fill.BackgroundColor);
        Assert.NotEqual(sheet.Cell("A2").Style.Fill.BackgroundColor, sheet.Cell("A4").Style.Fill.BackgroundColor);
        Assert.Empty(sheet.Cell("H4").GetString());
    }

    [Fact]
    public void Empty_result_has_header_without_fabricated_student()
    {
        using var book = new XLWorkbook(new MemoryStream(TeamRosterWorkbook.Create([])));
        Assert.Equal(1, book.Worksheet(1).LastRowUsed()!.RowNumber());
    }
}
