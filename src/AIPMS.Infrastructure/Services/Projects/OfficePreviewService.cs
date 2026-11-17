using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Files;
using AIPMS.Application.Features.Files.Abstractions;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class OfficePreviewService : IOfficePreviewService
{
    internal const int MaxBytes = 5 * 1024 * 1024;
    private const long MaxExpandedBytes = 25L * 1024 * 1024;

    private static ValidationException Invalid(string reason) => new(new Dictionary<string, string[]> { ["file"] = [reason] });

    public OfficePreviewDto Preview(string fileName, byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxBytes) throw Invalid("Office preview accepts files up to 5 MiB; download larger originals.");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".docx" or ".xlsx")) throw Invalid("Only DOCX and XLSX can be previewed here.");
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
            if (archive.Entries.Count is < 2 or > 1000 || archive.GetEntry("[Content_Types].xml") is null)
                throw Invalid("The Office package is invalid.");
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                expanded += entry.Length;
                if (expanded > MaxExpandedBytes || entry.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase))
                    throw Invalid("The Office package contains unsafe or excessive content.");
            }
            return extension == ".docx" ? Word(archive) : Workbook(archive);
        }
        catch (InvalidDataException) { throw Invalid("The Office package is damaged."); }
        catch (XmlException) { throw Invalid("The Office document XML is invalid."); }
        catch (ArgumentException) { throw Invalid("The Office document structure is invalid."); }
    }

    private static OfficePreviewDto Word(ZipArchive archive)
    {
        var entry = archive.GetEntry("word/document.xml") ?? throw Invalid("DOCX is missing document.xml.");
        if (entry.Length > 5L * 1024 * 1024) throw Invalid("DOCX text is too large to preview.");
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 5_000_000, IgnoreComments = true };
        using var reader = XmlReader.Create(entry.Open(), settings);
        var document = XDocument.Load(reader);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var text = document.Descendants(word + "p")
            .Select(p => string.Concat(p.Descendants(word + "t").Select(t => t.Value)))
            .Where(p => p.Length > 0).Take(201).ToArray();
        return new("DOCX_TEXT", text.Take(200).ToArray(), [], text.Length > 200);
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        if (entry.Length > 6L * 1024 * 1024) throw Invalid("Office XML is too large to preview.");
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 6_000_000, IgnoreComments = true };
        using var reader = XmlReader.Create(entry.Open(), settings);
        return XDocument.Load(reader);
    }

    private static OfficePreviewDto Workbook(ZipArchive archive)
    {
        var bookEntry = archive.GetEntry("xl/workbook.xml") ?? throw Invalid("XLSX is missing workbook.xml.");
        var relEntry = archive.GetEntry("xl/_rels/workbook.xml.rels") ?? throw Invalid("XLSX relationships are missing.");
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relation = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var book = ReadXml(bookEntry);
        var relations = ReadXml(relEntry).Descendants().Where(e => e.Name.LocalName == "Relationship")
            .Where(e => ((string?)e.Attribute("Type"))?.EndsWith("/worksheet", StringComparison.Ordinal) == true)
            .ToDictionary(e => (string?)e.Attribute("Id") ?? "", e => (string?)e.Attribute("Target") ?? "");
        var sheetDefinitions = book.Descendants(main + "sheet").ToArray();
        var truncated = sheetDefinitions.Length > 3;
        var shared = new List<string>();
        if (archive.GetEntry("xl/sharedStrings.xml") is { } strings)
        {
            foreach (var item in ReadXml(strings).Descendants(main + "si").Take(10_001))
                shared.Add(string.Concat(item.Descendants(main + "t").Select(t => t.Value)));
            truncated |= shared.Count > 10_000;
        }
        var sheets = new List<OfficePreviewSheet>();
        foreach (var definition in sheetDefinitions.Take(3))
        {
            var id = (string?)definition.Attribute(relation + "id") ?? "";
            if (!relations.TryGetValue(id, out var target) || target.Contains("..", StringComparison.Ordinal)
                || target.Contains(':', StringComparison.Ordinal) || target.Contains('\\'))
                throw Invalid("XLSX worksheet path is invalid.");
            var entryName = "xl/worksheets/" + target.Split('/').Last();
            var sheetEntry = archive.GetEntry(entryName) ?? throw Invalid("XLSX worksheet is missing.");
            var sheet = ReadXml(sheetEntry);
            var rowDefinitions = sheet.Descendants(main + "sheetData").Elements(main + "row").Take(101).ToArray();
            truncated |= rowDefinitions.Length > 100;
            var rows = new List<IReadOnlyList<string>>();
            foreach (var row in rowDefinitions.Take(100))
            {
                var cells = Enumerable.Repeat("", 20).ToArray();
                var nextColumn = 0;
                foreach (var cell in row.Elements(main + "c"))
                {
                    var address = (string?)cell.Attribute("r") ?? "";
                    var col = Column(address);
                    if (col < 0) col = nextColumn;
                    nextColumn = col + 1;
                    if (col >= 20) { truncated = true; continue; }
                    var raw = (string?)cell.Element(main + "v") ?? "";
                    var kind = (string?)cell.Attribute("t") ?? "";
                    var value = kind == "s" && int.TryParse(raw, out var index) && index >= 0 && index < shared.Count
                        ? shared[index] : kind == "inlineStr" ? string.Concat(cell.Descendants(main + "t").Select(x => x.Value)) : raw;
                    cells[col] = value.Length > 500 ? value[..500] : value;
                    truncated |= value.Length > 500;
                }
                rows.Add(cells.Take(Math.Max(1, Math.Min(nextColumn, 20))).ToArray());
            }
            sheets.Add(new((string?)definition.Attribute("name") ?? $"Sheet {sheets.Count + 1}", rows));
        }
        return new("XLSX_GRID", [], sheets, truncated);
    }

    private static int Column(string address)
    {
        var result = 0;
        foreach (var character in address)
        {
            if (character is not (>= 'A' and <= 'Z')) break;
            result = result * 26 + character - 'A' + 1;
            if (result > 20) return 20;
        }
        return result == 0 ? -1 : result - 1;
    }
}
