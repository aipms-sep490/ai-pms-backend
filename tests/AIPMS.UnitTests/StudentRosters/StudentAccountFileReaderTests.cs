using System.Text;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Services.StudentRosters;
using ClosedXML.Excel;

namespace AIPMS.UnitTests.StudentRosters;

public sealed class StudentAccountFileReaderTests
{
    [Fact]
    public void Csv_reads_aliases_optional_fields_and_quoted_names_without_losing_zeroes()
    {
        var row = Assert.Single(StudentAccountFileReader.Read(Encoding.UTF8.GetBytes("MSSV,Họ và tên,Email,SĐT,Khung\n0001,\"Nguyen, An\",an@example.test,0123456789,BIT_SE"), "students.csv"));
        Assert.Equal("0001", row.StudentCode); Assert.Equal("Nguyen, An", row.FullName);
        Assert.Equal("0123456789", row.Phone); Assert.Equal("BIT_SE", row.CurriculumCode);
        Assert.Equal(2, row.RowNumber);
    }

    [Fact]
    public void Xlsx_reads_text_and_rejects_formulas()
    {
        using var book = new XLWorkbook(); var sheet = book.AddWorksheet("Students");
        sheet.Cell(1, 1).Value = "studentCode"; sheet.Cell(1, 2).Value = "fullName"; sheet.Cell(1, 3).Value = "email";
        sheet.Cell(2, 1).Value = "0001"; sheet.Cell(2, 2).Value = "Student"; sheet.Cell(2, 3).Value = "student@example.test";
        using var bytes = new MemoryStream(); book.SaveAs(bytes);
        Assert.Equal("0001", Assert.Single(StudentAccountFileReader.Read(bytes.ToArray(), "students.xlsx")).StudentCode);
        sheet.Cell(2, 2).FormulaA1 = "1+1";
        using var formula = new MemoryStream(); book.SaveAs(formula);
        Assert.Throws<ValidationException>(() => StudentAccountFileReader.Read(formula.ToArray(), "students.xlsx"));
    }

    [Theory]
    [InlineData("MSSV,Khung\nDE1,SE")]
    [InlineData("MSSV,studentCode,fullName,email\na,b,c,d")]
    [InlineData("MSSV,fullName,email")]
    public void Invalid_headers_and_empty_data_are_rejected(string csv) =>
        Assert.Throws<ValidationException>(() => StudentAccountFileReader.Read(Encoding.UTF8.GetBytes(csv), "students.csv"));

    [Fact]
    public void More_than_500_rows_are_rejected()
    {
        var csv = "MSSV,fullName,email\n" + string.Join('\n', Enumerable.Range(1, 501).Select(i => $"DE{i},Student,s{i}@example.test"));
        Assert.Throws<ValidationException>(() => StudentAccountFileReader.Read(Encoding.UTF8.GetBytes(csv), "students.csv"));
    }
}
