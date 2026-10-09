using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using AIPMS.Application.Common.Exceptions;
using ClosedXML.Excel;
using Microsoft.VisualBasic.FileIO;

namespace AIPMS.Infrastructure.Services.StudentRosters;

internal sealed record CurriculumInput(int RowNumber, string StudentCode, string CurriculumCode);

internal static class CurriculumFileReader
{
    internal static ValidationException Invalid(string code) => new(new Dictionary<string, string[]> { ["file"] = [code] });
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxRows = 500;
    public static IReadOnlyList<CurriculumInput> Read(byte[] bytes, string fileName)
    {
        if (bytes.Length == 0 || bytes.Length > MaxBytes) throw Invalid("IMPORT_FILE_SIZE_INVALID");
        try
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var rows = extension switch
            {
                ".xlsx" => ReadExcel(bytes),
                ".csv" => ReadCsv(bytes),
                _ => throw Invalid("IMPORT_FORMAT_REQUIRED_XLSX_OR_CSV")
            };
            if (rows.Count < 2) throw Invalid("IMPORT_NO_DATA");
            var headers = rows[0].Select(x => x.Trim().TrimStart('\uFEFF')).ToArray();
            int Column(string english, string vietnamese)
            {
                var columns = headers.Select((h, i) => (h, i)).Where(x =>
                    x.h.Equals(english, StringComparison.OrdinalIgnoreCase) || x.h.Equals(vietnamese, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (columns.Length != 1) throw Invalid("IMPORT_HEADERS_REQUIRE_UNIQUE_MSSV_AND_CURRICULUM");
                return columns[0].i;
            }
            var student = Column("studentCode", "MSSV");
            var curriculum = Column("curriculumCode", "Khung");
            var result = new List<CurriculumInput>();
            for (var i = 1; i < rows.Count; i++)
            {
                if (rows[i].All(string.IsNullOrWhiteSpace)) continue;
                string Value(int index) => index < rows[i].Length ? rows[i][index].Trim() : "";
                result.Add(new(i + 1, Value(student).ToUpperInvariant(), Value(curriculum)));
            }
            if (result.Count is < 1 or > MaxRows) throw Invalid("IMPORT_REQUIRES_1_TO_500_ROWS");
            return result;
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or FormatException or ArgumentException or MalformedLineException)
        {
            throw Invalid("IMPORT_FILE_INVALID");
        }
    }

    private static List<string[]> ReadCsv(byte[] bytes)
    {
        using var reader = new StringReader(new UTF8Encoding(false, true).GetString(bytes));
        using var parser = new TextFieldParser(reader) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields()!;
            if (row.Length > 64 || rows.Count > MaxRows) throw Invalid("IMPORT_LIMIT_EXCEEDED");
            rows.Add(row);
        }
        return rows;
    }

    private static List<string[]> ReadExcel(byte[] bytes)
    {
        // Bound decompression before handing the workbook to the spreadsheet library.
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
        {
            if (zip.Entries.Count > 256 || zip.Entries.Sum(x => x.Length) > 20 * 1024 * 1024
                || zip.Entries.Any(x => x.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)
                    || x.FullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase)))
                throw Invalid("IMPORT_WORKBOOK_UNSUPPORTED");
        }
        using var book = new XLWorkbook(new MemoryStream(bytes));
        if (book.Worksheets.Count != 1) throw Invalid("IMPORT_REQUIRES_ONE_WORKSHEET");
        var sheet = book.Worksheet(1);
        var lastRow = sheet.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 0;
        var lastColumn = sheet.LastColumnUsed(XLCellsUsedOptions.Contents)?.ColumnNumber() ?? 0;
        if (lastRow > MaxRows + 1 || lastColumn > 64) throw Invalid("IMPORT_LIMIT_EXCEEDED");
        var rows = new List<string[]>();
        for (var r = 1; r <= lastRow; r++)
        {
            var row = new string[lastColumn];
            for (var c = 1; c <= lastColumn; c++)
            {
                var cell = sheet.Cell(r, c);
                if (cell.HasFormula) throw Invalid("IMPORT_FORMULAS_NOT_ALLOWED");
                row[c - 1] = cell.GetString();
            }
            rows.Add(row);
        }
        return rows;
    }
}
