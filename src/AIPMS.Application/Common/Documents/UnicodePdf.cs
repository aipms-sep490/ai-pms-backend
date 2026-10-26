using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace AIPMS.Application.Common.Documents;

public static class UnicodePdf
{
    static UnicodePdf() => GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
    public static void InitializeFonts() { }

    public static byte[] Write(string title, IEnumerable<string> paragraphs)
    {
        using var document = new PdfDocument();
        document.Info.Title = title;
        var font = new XFont("DejaVuSans", 11, XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));
        XGraphics? canvas = null;
        double y = 0;
        void Page()
        {
            canvas?.Dispose();
            var page = document.AddPage(); page.Size = PdfSharp.PageSize.A4;
            canvas = XGraphics.FromPdfPage(page); y = 48;
            canvas.DrawString($"AI-PMS | {document.PageCount}", font, XBrushes.Gray, 42, 810);
        }
        void Line(string value)
        {
            if (y > 770) Page();
            canvas!.DrawString(value, font, XBrushes.Black, 42, y); y += 17;
        }
        try
        {
            Page();
            foreach (var paragraph in new[] { title, "" }.Concat(paragraphs))
            {
                var line = "";
                var elements = StringInfo.GetTextElementEnumerator(paragraph.Replace('\t', ' '));
                while (elements.MoveNext())
                {
                    var next = elements.GetTextElement();
                    if (line.Length > 0 && canvas!.MeasureString(line + next, font).Width > 510)
                    {
                        var space = line.LastIndexOf(' ');
                        if (space > 0) { Line(line[..space]); line = line[(space + 1)..]; }
                        else { Line(line); line = ""; }
                    }
                    line += next;
                }
                Line(line);
            }
        }
        finally { canvas?.Dispose(); }
        using var output = new MemoryStream(); document.Save(output, false); return output.ToArray();
    }

    private sealed class EmbeddedFontResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new("DejaVuSans");
        public byte[] GetFont(string faceName)
        {
            using var stream = typeof(UnicodePdf).Assembly.GetManifestResourceStream("AIPMS.Application.Features.Dashboards.Fonts.DejaVuSans.ttf")
                ?? throw new InvalidOperationException("The export font is missing.");
            using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
        }
    }
}
