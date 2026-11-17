using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ReviewGates;
using ClosedXML.Excel;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ReviewGateService
{
    public async Task<byte[]> ExportTemplateWorkbook(long id, CancellationToken ct)
    {
        await RequireTemplateAuthority(id, ct);
        var template = await Template(id, ct) ?? throw new NotFoundException("ReviewGateTemplate", id);
        return ExportWorkbook(template);
    }

    public async Task<GateWorkbookPreview> PreviewTemplateWorkbook(long id, byte[] content, CancellationToken ct)
    {
        await RequireTemplateAuthority(id, ct);
        var template = await Template(id, ct) ?? throw new NotFoundException("ReviewGateTemplate", id);
        return PreviewWorkbook(template, content);
    }

    internal static byte[] ExportWorkbook(ReviewGateTemplateDto template)
    {
        using var book = new XLWorkbook();
        var help = book.Worksheets.Add("Hướng dẫn");
        help.Cell("A1").Value = "Mẫu tiến trình Gate AI-PMS";
        help.Cell("A2").Value = "Chỉ nhập vào bản nháp. Giữ nguyên ID và concurrency token ở sheet Thông tin.";
        help.Cell("A3").Value = "Mỗi Gate có ít nhất một yêu cầu; mã Gate và mã yêu cầu phải duy nhất trong phạm vi tương ứng.";
        help.Cell("A4").Value = "Thời gian UTC theo ISO 8601, ví dụ 2026-11-14T08:00:00Z; có thể để trống cả hai.";
        help.Cell("A5").Value = "Trọng số Gate: để trống tất cả, hoặc điền đủ và tổng đúng 100%. Không nhập công thức.";
        help.Cell("A6").Value = "Tải lên xem lỗi và thay đổi, sau đó xác nhận lưu; bản công bố không bị sửa trực tiếp.";
        help.Column(1).Width = 110;
        var info = book.Worksheets.Add("Thông tin");
        info.Cell("A1").Value = "Template ID"; info.Cell("B1").Value = template.Id;
        info.Cell("A2").Value = "Concurrency token"; info.Cell("B2").Value = template.ConcurrencyToken;
        info.Cell("A3").Value = "Mã mẫu"; info.Cell("B3").Value = SafeCell(template.Code);
        info.Cell("A4").Value = "Tên mẫu"; info.Cell("B4").Value = SafeCell(template.Name);
        info.Cell("A5").Value = "Trạng thái"; info.Cell("B5").Value = template.Status;
        info.Cell("A6").Value = "Phiên bản"; info.Cell("B6").Value = template.Version;
        info.Column(1).Width = 24; info.Column(2).Width = 70;
        var gates = book.Worksheets.Add("Gate");
        string[] gateHeaders = ["Mã Gate", "Thứ tự", "Tiêu đề", "Mục tiêu", "Bắt buộc", "Chặn Gate sau", "Chặn Defense", "Bắt đầu UTC", "Kết thúc UTC", "Chính sách reviewer", "Phạm vi review", "Trọng số %"];
        for (var c = 0; c < gateHeaders.Length; c++) gates.Cell(1, c + 1).Value = gateHeaders[c];
        var requirements = book.Worksheets.Add("Yêu cầu");
        string[] requirementHeaders = ["Mã Gate", "Mã yêu cầu", "Tiêu đề", "Mô tả/hướng dẫn", "Loại", "Phạm vi", "Major ID", "Student ID", "Xác minh", "Bắt buộc"];
        for (var c = 0; c < requirementHeaders.Length; c++) requirements.Cell(1, c + 1).Value = requirementHeaders[c];
        var gateRow = 2; var requirementRow = 2;
        foreach (var gate in template.Gates.OrderBy(g => g.Sequence))
        {
            gates.Cell(gateRow, 1).Value = SafeCell(gate.Code);
            gates.Cell(gateRow, 2).Value = gate.Sequence;
            gates.Cell(gateRow, 3).Value = SafeCell(gate.Title);
            gates.Cell(gateRow, 4).Value = SafeCell(gate.Objective);
            gates.Cell(gateRow, 5).Value = Flag(gate.Required);
            gates.Cell(gateRow, 6).Value = Flag(gate.BlockNextGate);
            gates.Cell(gateRow, 7).Value = Flag(gate.BlocksDefense);
            gates.Cell(gateRow, 8).Value = Date(gate.Start);
            gates.Cell(gateRow, 9).Value = Date(gate.End);
            gates.Cell(gateRow, 10).Value = SafeCell(gate.ReviewerPolicy);
            gates.Cell(gateRow, 11).Value = SafeCell(gate.ReviewScope);
            if (gate.Weight.HasValue) gates.Cell(gateRow, 12).Value = gate.Weight.Value;
            gateRow++;
            foreach (var item in gate.Requirements)
            {
                requirements.Cell(requirementRow, 1).Value = SafeCell(gate.Code);
                requirements.Cell(requirementRow, 2).Value = SafeCell(item.Code);
                requirements.Cell(requirementRow, 3).Value = SafeCell(item.Title);
                requirements.Cell(requirementRow, 4).Value = SafeCell(item.Description);
                requirements.Cell(requirementRow, 5).Value = SafeCell(item.Type);
                requirements.Cell(requirementRow, 6).Value = item.Scope;
                if (item.MajorId.HasValue) requirements.Cell(requirementRow, 7).Value = item.MajorId.Value;
                if (item.TargetStudentId.HasValue) requirements.Cell(requirementRow, 8).Value = item.TargetStudentId.Value;
                requirements.Cell(requirementRow, 9).Value = SafeCell(item.Verification);
                requirements.Cell(requirementRow, 10).Value = Flag(item.Required);
                requirementRow++;
            }
        }
        foreach (var sheet in new[] { gates, requirements })
        {
            sheet.Row(1).Style.Font.Bold = true;
            sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F2FF");
            sheet.SheetView.FreezeRows(1);
            sheet.Columns().AdjustToContents(12, 60);
        }
        using var output = new MemoryStream(); book.SaveAs(output); return output.ToArray();
    }

    internal static GateWorkbookPreview PreviewWorkbook(ReviewGateTemplateDto template, byte[] content)
    {
        if (template.Status != "DRAFT") throw new ConflictException("Only draft Gate templates can be imported.", "GATE_TEMPLATE_DRAFT_REQUIRED");
        if (content.Length is 0 or > 5 * 1024 * 1024 || !RubricWorkbookService.SafePackage(content))
            return new(null, [new("Workbook", "Tệp XLSX không hợp lệ hoặc vượt quá giới hạn an toàn.")], []);
        var issues = new List<GateWorkbookIssue>();
        try
        {
            using var book = new XLWorkbook(new MemoryStream(content, writable: false));
            var info = book.Worksheets.FirstOrDefault(s => s.Name == "Thông tin");
            var gateSheet = book.Worksheets.FirstOrDefault(s => s.Name == "Gate");
            var requirementSheet = book.Worksheets.FirstOrDefault(s => s.Name == "Yêu cầu");
            if (book.Worksheets.Count > 4 || info is null || gateSheet is null || requirementSheet is null)
                return new(null, [new("Workbook", "Thiếu sheet Thông tin, Gate hoặc Yêu cầu.")], []);
            if (book.Worksheets.SelectMany(s => s.CellsUsed()).Any(c => c.HasFormula))
                return new(null, [new("Workbook", "Không chấp nhận công thức trong workbook.")], []);
            if (info.Cell("B1").GetString() != template.Id || info.Cell("B2").GetString() != template.ConcurrencyToken)
                throw new ConflictException("Workbook does not match the current Gate template version.", "GATE_WORKBOOK_STALE");
            var gateLast = gateSheet.LastRowUsed()?.RowNumber() ?? 1;
            var requirementLast = requirementSheet.LastRowUsed()?.RowNumber() ?? 1;
            if (gateLast is < 2 or > 21 || requirementLast is < 2 or > 501)
                return new(null, [new("Workbook", "Giới hạn 20 Gate và 500 yêu cầu; mỗi loại cần ít nhất một dòng.")], []);
            var gates = new List<ReviewGateDefinitionInput>();
            for (var row = 2; row <= gateLast; row++)
            {
                var cell = gateSheet.Row(row);
                var code = cell.Cell(1).GetString().Trim();
                var title = cell.Cell(3).GetString().Trim();
                var objective = cell.Cell(4).GetString().Trim();
                if (code.Length is < 1 or > 80 || title.Length is < 1 or > 200)
                    issues.Add(new($"Gate!A{row}:C{row}", "Mã Gate và tiêu đề là bắt buộc, tối đa 80/200 ký tự."));
                var sequence = Whole(cell.Cell(2), $"Gate!B{row}", issues);
                var required = Boolean(cell.Cell(5), $"Gate!E{row}", issues);
                var blockNext = Boolean(cell.Cell(6), $"Gate!F{row}", issues);
                var blockDefense = Boolean(cell.Cell(7), $"Gate!G{row}", issues);
                var start = Timestamp(cell.Cell(8), $"Gate!H{row}", issues);
                var end = Timestamp(cell.Cell(9), $"Gate!I{row}", issues);
                if (start.HasValue != end.HasValue || start.HasValue && end <= start)
                    issues.Add(new($"Gate!H{row}:I{row}", "Nhập cả hai mốc UTC và kết thúc sau bắt đầu, hoặc để trống cả hai."));
                var weight = OptionalDecimal(cell.Cell(12), $"Gate!L{row}", issues);
                gates.Add(new(code, sequence, title, objective, required, blockNext, blockDefense,
                    start, end, cell.Cell(10).GetString().Trim(), cell.Cell(11).GetString().Trim(), [], weight));
            }
            if (gates.Select(g => g.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != gates.Count
                || gates.Select(g => g.Sequence).Distinct().Count() != gates.Count || gates.Any(g => g.Sequence <= 0))
                issues.Add(new("Gate!A:B", "Mã Gate và thứ tự phải duy nhất, thứ tự phải dương."));
            if (gates.Any(g => g.Weight.HasValue) && (gates.Any(g => !g.Weight.HasValue)
                || gates.Sum(g => g.Weight ?? 0) != 100m || gates.Any(g => g.Weight is < 0 or > 100)))
                issues.Add(new("Gate!L", "Điền đủ trọng số 0–100% có tổng đúng 100%, hoặc để trống tất cả."));
            if (issues.Count > 0) return new(null, issues, []);
            var grouped = gates.ToDictionary(g => g.Code, _ => new List<ReviewGateRequirementInput>(), StringComparer.OrdinalIgnoreCase);
            for (var row = 2; row <= requirementLast; row++)
            {
                var cell = requirementSheet.Row(row);
                var gateCode = cell.Cell(1).GetString().Trim();
                var code = cell.Cell(2).GetString().Trim();
                var title = cell.Cell(3).GetString().Trim();
                var scope = cell.Cell(6).GetString().Trim().ToUpperInvariant();
                var major = OptionalId(cell.Cell(7), $"Yêu cầu!G{row}", issues);
                var student = OptionalId(cell.Cell(8), $"Yêu cầu!H{row}", issues);
                if (!grouped.TryGetValue(gateCode, out var group))
                    issues.Add(new($"Yêu cầu!A{row}", "Mã Gate không tồn tại."));
                if (code.Length is < 1 or > 80 || title.Length is < 1 or > 200)
                    issues.Add(new($"Yêu cầu!B{row}:C{row}", "Mã yêu cầu và tiêu đề là bắt buộc, tối đa 80/200 ký tự."));
                if (scope != "COMMON" && scope != "MAJOR_SPECIFIC" && scope != "INDIVIDUAL"
                    || scope == "COMMON" && (major.HasValue || student.HasValue)
                    || scope == "MAJOR_SPECIFIC" && (!major.HasValue || student.HasValue)
                    || scope == "INDIVIDUAL" && (!major.HasValue || !student.HasValue))
                    issues.Add(new($"Yêu cầu!F{row}:H{row}", "Phạm vi và Major/Student ID không khớp."));
                var required = Boolean(cell.Cell(10), $"Yêu cầu!J{row}", issues);
                group?.Add(new(code, title, cell.Cell(4).GetString().Trim(), cell.Cell(5).GetString().Trim(),
                    scope, major, student, cell.Cell(9).GetString().Trim(), required));
            }
            foreach (var pair in grouped)
                if (pair.Value.Count == 0 || pair.Value.Select(r => r.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pair.Value.Count)
                    issues.Add(new("Yêu cầu!A:B", $"Gate {pair.Key} cần yêu cầu có mã duy nhất."));
            if (issues.Count > 0) return new(null, issues, []);
            var draft = new SaveReviewGateTemplateRequest(template.ProjectPeriodId, info.Cell("B3").GetString().Trim(),
                info.Cell("B4").GetString().Trim(), template.Mode, template.Major, template.Version,
                template.ConcurrencyToken, gates.Select(g => g with { Requirements = grouped[g.Code] }).ToArray());
            if (string.IsNullOrWhiteSpace(draft.Code) || string.IsNullOrWhiteSpace(draft.Name))
                return new(null, [new("Thông tin!B3:B4", "Mã mẫu và tên mẫu không được để trống.")], []);
            return new(draft, [], [$"Workbook có {gates.Count} Gate và {grouped.Values.Sum(g => g.Count)} yêu cầu; bản hiện tại có {template.Gates.Count} Gate."]);
        }
        catch (ConflictException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(null, [new("Workbook", "Không thể đọc an toàn tệp XLSX này.")], []);
        }
    }

    private static string Flag(bool value) => value ? "Có" : "Không";
    private static string Date(DateTime? value) => value.HasValue ? value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : "";
    private static string SafeCell(string? value) => value is null ? "" : value.Length > 0 && "=+-@".Contains(value[0]) ? "'" + value : value;
    private static bool Boolean(IXLCell cell, string address, List<GateWorkbookIssue> issues)
    {
        var value = cell.GetString().Trim();
        if (value == "Có") return true;
        if (value == "Không") return false;
        issues.Add(new(address, "Nhập Có hoặc Không.")); return false;
    }
    private static int Whole(IXLCell cell, string address, List<GateWorkbookIssue> issues)
    {
        if (int.TryParse(cell.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is > 0 and <= 100000) return number;
        issues.Add(new(address, "Cần số nguyên dương.")); return 0;
    }
    private static decimal? OptionalDecimal(IXLCell cell, string address, List<GateWorkbookIssue> issues)
    {
        var value = cell.GetString().Trim();
        if (value.Length == 0) return null;
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            && decimal.Round(number, 2) == number) return number;
        issues.Add(new(address, "Trọng số cần là số có tối đa hai chữ số thập phân.")); return null;
    }
    private static long? OptionalId(IXLCell cell, string address, List<GateWorkbookIssue> issues)
    {
        var value = cell.GetString().Trim();
        if (value.Length == 0) return null;
        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0) return number;
        issues.Add(new(address, "ID cần là số nguyên dương.")); return null;
    }
    private static DateTime? Timestamp(IXLCell cell, string address, List<GateWorkbookIssue> issues)
    {
        var value = cell.GetString().Trim();
        if (value.Length == 0) return null;
        if (DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            return timestamp.UtcDateTime;
        issues.Add(new(address, "Nhập thời gian ISO 8601 UTC, ví dụ 2026-11-14T08:00:00.0000000Z.")); return null;
    }
}
