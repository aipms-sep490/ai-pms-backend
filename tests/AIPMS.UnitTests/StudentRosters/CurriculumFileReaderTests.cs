using System.Text;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Services.StudentRosters;
using ClosedXML.Excel;

namespace AIPMS.UnitTests.StudentRosters;

public sealed class CurriculumFileReaderTests
{
    [Fact]
    public void Csv_reads_aliases_quotes_and_preserves_leading_zero()
    {
        var rows = CurriculumFileReader.Read(Encoding.UTF8.GetBytes("MSSV,Khung,Ignored\n00123,\"BIT,SE\",x\nde123,,y"), "students.csv");
        Assert.Equal("00123", rows[0].StudentCode);
        Assert.Equal("BIT,SE", rows[0].CurriculumCode);
        Assert.Equal("DE123", rows[1].StudentCode);
        Assert.Empty(rows[1].CurriculumCode);
    }

    [Theory]
    [InlineData("MSSV,MSSV,Khung\nA,A,B")]
    [InlineData("Email,Khung\nA,B")]
    [InlineData("MSSV,Khung")]
    [InlineData("MSSV,Khung\n\"unterminated,B")]
    public void Invalid_csv_is_validation_error(string value) =>
        Assert.Throws<ValidationException>(() => CurriculumFileReader.Read(Encoding.UTF8.GetBytes(value), "data.csv"));

    [Fact]
    public void Rejects_501_rows_and_invalid_utf8()
    {
        var csv = "MSSV,Khung\n" + string.Join('\n', Enumerable.Range(0, 501).Select(i => $"S{i},SE"));
        Assert.Throws<ValidationException>(() => CurriculumFileReader.Read(Encoding.UTF8.GetBytes(csv), "data.csv"));
        Assert.Throws<ValidationException>(() => CurriculumFileReader.Read([0xff, 0xfe, 0x80], "data.csv"));
    }

    [Fact]
    public void Xlsx_reads_text_but_rejects_formulas_and_multiple_sheets()
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Students");
        sheet.Cell(1, 1).Value = "studentCode";
        sheet.Cell(1, 2).Value = "curriculumCode";
        sheet.Cell(2, 1).Value = "001234";
        sheet.Cell(2, 2).Value = "BIT_SE_18D_.NET";
        using var stream = new MemoryStream();
        byte[] Bytes() { book.SaveAs(stream); return stream.ToArray(); }
        Assert.Equal("001234", Assert.Single(CurriculumFileReader.Read(Bytes(), "data.xlsx")).StudentCode);
        sheet.Cell(2, 2).FormulaA1 = "1+1";
        Assert.Throws<ValidationException>(() => CurriculumFileReader.Read(Bytes(), "data.xlsx"));
        sheet.Cell(2, 2).Value = "SE";
        book.AddWorksheet("Extra");
        Assert.Throws<ValidationException>(() => CurriculumFileReader.Read(Bytes(), "data.xlsx"));
        Assert.Throws<ValidationException>(() => CurriculumFileReader.Read([1, 2, 3], "data.xlsx"));
    }
}
