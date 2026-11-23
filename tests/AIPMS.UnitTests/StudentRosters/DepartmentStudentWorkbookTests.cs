using AIPMS.Application.Features.DepartmentStudents;
using AIPMS.Infrastructure.Services.DepartmentStudents;
using AIPMS.Infrastructure.Services.StudentRosters;
using ClosedXML.Excel;

namespace AIPMS.UnitTests.StudentRosters;

public sealed class DepartmentStudentWorkbookTests
{
    [Fact]
    public void Department_students_workbook_renders_class_and_specialization_safely_without_formulas()
    {
        DepartmentStudentSummaryDto[] rows =
        [
            new(1, "SE180001", "Nguyễn An", "an@fpt.edu.vn", "0901234567", "ACTIVE",
                1, "Khoa CNTT", 10, "SE", "Kỹ thuật phần mềm",
                "SE1801", 5, "DOTNET", ".NET Enterprise", "BIT_SE_18",
                "VERIFIED", "VERIFIED", 100, "SE_G01", true, 200, "PRJ-01", "ACTIVE", DateTime.UtcNow),
            new(2, "SE180002", "=1+1", "binh@fpt.edu.vn", null, "ACTIVE",
                1, "Khoa CNTT", 10, "SE", "Kỹ thuật phần mềm",
                "SE1802", null, null, null, null,
                "PENDING", "NOT_SUBMITTED", null, null, false, null, null, null, null)
        ];

        using var book = new XLWorkbook(new MemoryStream(DepartmentStudentWorkbook.CreateDepartmentStudentsWorkbook(rows)));
        var sheet = book.Worksheet(1);
        Assert.Equal(14, sheet.LastColumnUsed()!.ColumnNumber());
        Assert.Equal("Lớp", sheet.Cell("G1").GetString());
        Assert.Equal("Chuyên ngành hẹp", sheet.Cell("H1").GetString());
        Assert.Equal("SE1801", sheet.Cell("G2").GetString());
        Assert.Equal(".NET Enterprise", sheet.Cell("H2").GetString());
        Assert.Equal("Chưa có nhóm", sheet.Cell("M3").GetString());
        Assert.Equal("=1+1", sheet.Cell("C3").GetString());
        Assert.All(sheet.CellsUsed(), c => Assert.False(c.HasFormula));
    }

    [Fact]
    public void Admin_accounts_workbook_excludes_secrets_and_formats_text_safely()
    {
        AdminUserAccountExportRow[] rows =
        [
            new("SE180001", "Nguyễn An", "an@fpt.edu.vn", "0901234567", "STUDENT",
                "Khoa CNTT", "SE", "SE1801", ".NET Enterprise", "BIT_SE_18",
                "ACTIVE", "VERIFIED", new DateTime(2026, 11, 23, 8, 30, 0, DateTimeKind.Utc))
        ];

        using var book = new XLWorkbook(new MemoryStream(DepartmentStudentWorkbook.CreateAdminAccountsWorkbook(rows)));
        var sheet = book.Worksheet(1);
        Assert.Equal(14, sheet.LastColumnUsed()!.ColumnNumber());
        Assert.Equal("Vai trò", sheet.Cell("F1").GetString());
        Assert.Equal("SE1801", sheet.Cell("I2").GetString());
        Assert.Equal(".NET Enterprise", sheet.Cell("J2").GetString());
        Assert.All(sheet.CellsUsed(), c => Assert.False(c.HasFormula));
    }

    [Fact]
    public void Team_roster_workbook_appends_class_and_specialization_columns_when_present()
    {
        TeamRosterRow[] rows =
        [
            new(1, "SE_01", 1, true, "SE180001", "Nguyễn An", "0901234567", "an@fpt.edu.vn", "BIT_SE_18", "SE1801", ".NET Enterprise")
        ];

        using var book = new XLWorkbook(new MemoryStream(TeamRosterWorkbook.Create(rows)));
        var sheet = book.Worksheet(1);
        Assert.Equal(10, sheet.LastColumnUsed()!.ColumnNumber());
        Assert.Equal("Lớp", sheet.Cell("I1").GetString());
        Assert.Equal("Chuyên ngành hẹp", sheet.Cell("J1").GetString());
        Assert.Equal("SE1801", sheet.Cell("I2").GetString());
        Assert.Equal(".NET Enterprise", sheet.Cell("J2").GetString());
    }
}
