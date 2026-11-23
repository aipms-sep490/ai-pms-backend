using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Queries;
using AIPMS.Application.Features.Evaluations.Services;
using ClosedXML.Excel;
using MediatR;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class RubricWorkbookService(ISender sender) : IRubricWorkbookService
{
    private const int MaxBytes = 5 * 1024 * 1024;
    private sealed record FlatCriterion(int RowNumber, string Code, string Parent, string Name, string? Description,
        decimal Weight, decimal? MaxScore, int Order, bool Required, string? Outcome, string? Guidance,
        string? TargetScope, bool IsIntegration);

    public async Task<byte[]> ExportAsync(long rubricId, CancellationToken ct) =>
        Export(await sender.Send(new GetRubricQuery(rubricId), ct));

    internal static byte[] Export(RubricDto rubric)
    {
        using var book = new XLWorkbook();
        var help = book.Worksheets.Add("Hướng dẫn");
        help.Cell("A1").Value = "Bộ tiêu chí AI-PMS (V4.0 — Hỗ trợ Round-Trip & Data Validation)";
        help.Cell("A2").Value = "Chỉ sửa bản nháp. Giữ nguyên ID và mã phiên bản ở sheet Thông tin.";
        help.Cell("A3").Value = "Mỗi nhóm tiêu chí cùng cấp phải có tổng trọng số 100% (sai số tối đa ±0.001).";
        help.Cell("A4").Value = "Mã dòng và mã cha định nghĩa cây tiêu chí; không nhập công thức hoặc macro.";
        help.Cell("A5").Value = "Cột Phạm vi mục tiêu (PROJECT/MAJOR/STUDENT) và Tích hợp INT (Có/Không) có sẵn danh sách chọn.";
        help.Cell("A6").Value = "Tải lên để xem Staging Preview Grid, sửa lỗi trực tiếp rồi mới xác nhận lưu bản nháp.";
        help.Column(1).Width = 105;

        var info = book.Worksheets.Add("Thông tin");
        info.Cell("A1").Value = "Rubric ID"; info.Cell("B1").Value = rubric.Id;
        info.Cell("A2").Value = "Concurrency token"; info.Cell("B2").Value = rubric.ConcurrencyToken;
        info.Cell("A3").Value = "Tên bộ tiêu chí"; info.Cell("B3").Value = Safe(rubric.Name);
        info.Cell("A4").Value = "Mô tả"; info.Cell("B4").Value = Safe(rubric.Description);
        info.Cell("A5").Value = "Trạng thái"; info.Cell("B5").Value = rubric.Status;
        info.Cell("A6").Value = "Phiên bản"; info.Cell("B6").Value = rubric.Version;
        info.Cell("A7").Value = "Phạm vi (Scope)"; info.Cell("B7").Value = Safe(rubric.EvaluationScope);
        info.Cell("A8").Value = "Giai đoạn (Stage)"; info.Cell("B8").Value = Safe(rubric.EvaluationStage);
        info.Cell("A9").Value = "Loại đồ án (ProjectMode)"; info.Cell("B9").Value = Safe(rubric.ProjectMode);
        info.Cell("B7").CreateDataValidation().List("\"COMMON,MAJOR_SPECIFIC,INDIVIDUAL\"", true);
        info.Cell("B8").CreateDataValidation().List("\"ON_GOING,COLD,DEFENSE\"", true);
        info.Cell("B9").CreateDataValidation().List("\"SINGLE_MAJOR,INTERDISCIPLINARY\"", true);
        info.Column(1).Width = 28; info.Column(2).Width = 70;

        var criteria = book.Worksheets.Add("Tiêu chí");
        string[] headings = [
            "Mã dòng", "Mã cha", "Tên tiêu chí", "Mô tả", "Trọng số %", "Điểm tối đa",
            "Thứ tự", "Bắt buộc", "Chuẩn đầu ra", "Hướng dẫn minh chứng",
            "Phạm vi mục tiêu", "Tích hợp INT"
        ];
        for (var col = 0; col < headings.Length; col++) criteria.Cell(1, col + 1).Value = headings[col];

        var levels = book.Worksheets.Add("Mức chất lượng");
        string[] levelHeadings = ["Mã tiêu chí", "Thứ tự mức", "Tên mức", "Mô tả mức", "Từ điểm", "Đến điểm"];
        for (var col = 0; col < levelHeadings.Length; col++) levels.Cell(1, col + 1).Value = levelHeadings[col];

        var criterionRow = 2; var levelRow = 2; var generated = 0;
        void Write(RubricCriterionDto item, string parent)
        {
            var code = "C" + (++generated).ToString(CultureInfo.InvariantCulture);
            criteria.Cell(criterionRow, 1).Value = code;
            criteria.Cell(criterionRow, 2).Value = parent;
            criteria.Cell(criterionRow, 3).Value = Safe(item.Name);
            criteria.Cell(criterionRow, 4).Value = Safe(item.Description);
            criteria.Cell(criterionRow, 5).Value = item.WeightPercent;
            if (item.MaxScore.HasValue) criteria.Cell(criterionRow, 6).Value = item.MaxScore.Value;
            criteria.Cell(criterionRow, 7).Value = item.SortOrder;
            criteria.Cell(criterionRow, 8).Value = item.IsRequired ? "Có" : "Không";
            criteria.Cell(criterionRow, 9).Value = Safe(item.LearningOutcome);
            criteria.Cell(criterionRow, 10).Value = Safe(item.EvidenceGuidance);
            criteria.Cell(criterionRow, 11).Value = Safe(item.TargetScope);
            criteria.Cell(criterionRow, 12).Value = item.IsIntegration ? "Có" : "Không";
            criterionRow++;
            foreach (var level in item.Levels)
            {
                levels.Cell(levelRow, 1).Value = code;
                levels.Cell(levelRow, 2).Value = level.LevelOrder;
                levels.Cell(levelRow, 3).Value = Safe(level.Label);
                levels.Cell(levelRow, 4).Value = Safe(level.Descriptor);
                if (level.ScoreFrom.HasValue) levels.Cell(levelRow, 5).Value = level.ScoreFrom.Value;
                if (level.ScoreTo.HasValue) levels.Cell(levelRow, 6).Value = level.ScoreTo.Value;
                levelRow++;
            }
            foreach (var child in item.Children) Write(child, code);
        }
        foreach (var item in rubric.Criteria) Write(item, "");

        var validationEndRow = Math.Max(criterionRow + 25, 201);
        criteria.Range($"H2:H{validationEndRow}").CreateDataValidation().List("\"Có,Không\"", true);
        criteria.Range($"K2:K{validationEndRow}").CreateDataValidation().List("\"PROJECT,MAJOR,STUDENT\"", true);
        criteria.Range($"L2:L{validationEndRow}").CreateDataValidation().List("\"Có,Không\"", true);

        foreach (var sheet in new[] { criteria, levels })
        {
            sheet.Row(1).Style.Font.Bold = true;
            sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F2FF");
            sheet.SheetView.FreezeRows(1);
            sheet.Columns().AdjustToContents(12, 55);
        }
        using var output = new MemoryStream(); book.SaveAs(output); return output.ToArray();
    }

    public async Task<RubricWorkbookPreview> PreviewAsync(long rubricId, byte[] bytes, CancellationToken ct) =>
        Preview(await sender.Send(new GetRubricQuery(rubricId), ct), bytes);

    internal static RubricWorkbookPreview Preview(RubricDto rubric, byte[] bytes)
    {
        if (!rubric.CanEdit || rubric.Status != "DRAFT")
            throw new ConflictException("Create an editable draft version before importing criteria.", WorkflowErrorCodes.ImmutableVersion);
        if (bytes.Length is 0 or > MaxBytes)
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["Workbook must be 1 byte to 5 MiB."] });
        if (!SafePackage(bytes))
            return new(null, [new("Workbook", "Tệp XLSX không hợp lệ hoặc chứa nội dung không được hỗ trợ.")], []);

        var issues = new List<RubricWorkbookIssue>();
        try
        {
            using var book = new XLWorkbook(new MemoryStream(bytes, writable: false));
            var info = book.Worksheets.FirstOrDefault(s => s.Name == "Thông tin");
            var sheet = book.Worksheets.FirstOrDefault(s => s.Name == "Tiêu chí");
            var levelSheet = book.Worksheets.FirstOrDefault(s => s.Name == "Mức chất lượng");
            if (book.Worksheets.Count > 4 || info is null || sheet is null || levelSheet is null)
                return new(null, [new("Workbook", "Cần đúng các sheet Thông tin, Tiêu chí và Mức chất lượng.")], []);
            if (book.Worksheets.SelectMany(s => s.CellsUsed()).Any(c => c.HasFormula))
                return new(null, [new("Workbook", "Không chấp nhận công thức trong workbook nhập liệu.")], []);
            if (info.Cell("B1").GetString() != rubric.Id.ToString(CultureInfo.InvariantCulture)
                || info.Cell("B2").GetString() != rubric.ConcurrencyToken)
                throw new ConflictException("Workbook was exported from another or an older rubric version.", WorkflowErrorCodes.StaleConcurrencyToken);

            var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
            var levelLast = levelSheet.LastRowUsed()?.RowNumber() ?? 1;
            if (last is < 2 or > 201 || levelLast > 1201)
                return new(null, [new("Workbook", "Tối đa 200 tiêu chí và 1.200 mức chất lượng.")], []);

            var rows = new List<FlatCriterion>();
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var errorRows = new HashSet<int>();
            for (var r = 2; r <= last; r++)
            {
                var beforeCount = issues.Count;
                var code = sheet.Cell(r, 1).GetString().Trim().ToUpperInvariant();
                var parent = sheet.Cell(r, 2).GetString().Trim().ToUpperInvariant();
                var name = sheet.Cell(r, 3).GetString().Trim();
                if (code.Length is < 1 or > 40 || !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z0-9_]+$") || !codes.Add(code))
                    issues.Add(new($"Tiêu chí!A{r}", "Mã dòng phải duy nhất, dài tối đa 40 ký tự A-Z, 0-9, _."));
                if (name.Length is < 1 or > 200) issues.Add(new($"Tiêu chí!C{r}", "Tên tiêu chí cần 1–200 ký tự."));
                var weight = Number(sheet.Cell(r, 5), $"Tiêu chí!E{r}", issues);
                var max = OptionalNumber(sheet.Cell(r, 6), $"Tiêu chí!F{r}", issues);
                var order = Integer(sheet.Cell(r, 7), $"Tiêu chí!G{r}", issues);
                var requiredText = sheet.Cell(r, 8).GetString().Trim();
                if (requiredText is not ("Có" or "Không")) issues.Add(new($"Tiêu chí!H{r}", "Nhập Có hoặc Không."));
                var targetScopeRaw = sheet.Cell(r, 11).GetString().Trim().ToUpperInvariant();
                string? targetScope = string.IsNullOrEmpty(targetScopeRaw) ? null : targetScopeRaw;
                if (targetScope is not null && targetScope is not ("PROJECT" or "MAJOR" or "STUDENT"))
                    issues.Add(new($"Tiêu chí!K{r}", "Phạm vi mục tiêu phải là PROJECT, MAJOR hoặc STUDENT."));
                var integrationRaw = sheet.Cell(r, 12).GetString().Trim();
                if (!string.IsNullOrEmpty(integrationRaw) && integrationRaw is not ("Có" or "Không"))
                    issues.Add(new($"Tiêu chí!L{r}", "Tích hợp INT phải là Có hoặc Không."));

                if (issues.Count > beforeCount) errorRows.Add(r);
                rows.Add(new(r, code, parent, name, sheet.Cell(r, 4).GetString().Trim(), weight, max, order,
                    requiredText == "Có", sheet.Cell(r, 9).GetString().Trim(), sheet.Cell(r, 10).GetString().Trim(),
                    targetScope, integrationRaw == "Có"));
            }

            var byCode = rows.GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
                if (row.Parent.Length > 0 && (!byCode.ContainsKey(row.Parent) || row.Parent == row.Code))
                {
                    errorRows.Add(row.RowNumber);
                    issues.Add(new("Tiêu chí!B", $"Mã cha của {row.Code} không hợp lệ."));
                }

            foreach (var group in rows.GroupBy(r => r.Parent, StringComparer.OrdinalIgnoreCase))
            {
                if (Math.Abs(group.Sum(r => r.Weight) - 100m) > RubricRules.WeightSumTolerance
                    || group.Any(r => r.Weight <= 0 || r.Weight > 100 || decimal.Round(r.Weight, 3) != r.Weight)
                    || group.Select(r => r.Order).Distinct().Count() != group.Count())
                {
                    foreach (var r in group) errorRows.Add(r.RowNumber);
                    issues.Add(new("Tiêu chí!E:G", $"Nhóm {group.Key} cần tổng trọng số 100% (±0.001), mỗi thứ tự duy nhất và trọng số dương."));
                }
            }

            var levelMap = new Dictionary<string, List<RubricCriterionLevelInput>>(StringComparer.OrdinalIgnoreCase);
            for (var r = 2; r <= levelLast; r++)
            {
                var code = levelSheet.Cell(r, 1).GetString().Trim().ToUpperInvariant();
                if (!byCode.ContainsKey(code)) { issues.Add(new($"Mức chất lượng!A{r}", "Mã tiêu chí không tồn tại.")); continue; }
                var order = Integer(levelSheet.Cell(r, 2), $"Mức chất lượng!B{r}", issues);
                var label = levelSheet.Cell(r, 3).GetString().Trim();
                var descriptor = levelSheet.Cell(r, 4).GetString().Trim();
                if (label.Length is < 1 or > 100 || descriptor.Length is < 1 or > 2000)
                    issues.Add(new($"Mức chất lượng!C{r}:D{r}", "Tên mức và mô tả mức là bắt buộc."));
                var from = OptionalNumber(levelSheet.Cell(r, 5), $"Mức chất lượng!E{r}", issues);
                var to = OptionalNumber(levelSheet.Cell(r, 6), $"Mức chất lượng!F{r}", issues);
                if (from.HasValue && to.HasValue && from > to) issues.Add(new($"Mức chất lượng!E{r}:F{r}", "Từ điểm phải nhỏ hơn hoặc bằng đến điểm."));
                if (!levelMap.TryGetValue(code, out var levels)) levelMap[code] = levels = [];
                levels.Add(new(order, label, descriptor, from, to));
            }
            foreach (var pair in levelMap)
                if (pair.Value.Select(x => x.LevelOrder).Distinct().Count() != pair.Value.Count || pair.Value.Any(x => x.LevelOrder <= 0))
                    issues.Add(new("Mức chất lượng!B", $"Thứ tự mức của {pair.Key} phải dương và không trùng."));

            var stagedRows = rows.Select(r => new RubricWorkbookStagedRow(
                r.RowNumber, r.Code, r.Parent, r.Name, r.Description, r.Weight, r.MaxScore, r.Order,
                r.Required, r.Outcome, r.Guidance, r.TargetScope, r.IsIntegration, errorRows.Contains(r.RowNumber))).ToArray();

            if (issues.Count > 0) return new(null, issues, []) { StagedRows = stagedRows };

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            RubricCriterionInput Build(FlatCriterion row, HashSet<string> path)
            {
                if (!path.Add(row.Code)) throw new InvalidDataException("Cycle in criterion parents.");
                var children = rows.Where(r => r.Parent == row.Code).OrderBy(r => r.Order)
                    .Select(r => Build(r, path)).ToArray();
                path.Remove(row.Code); visited.Add(row.Code);
                return new(row.Name, row.Description, row.Weight, row.MaxScore, row.Order, row.Required)
                {
                    LearningOutcome = row.Outcome,
                    EvidenceGuidance = row.Guidance,
                    TargetScope = row.TargetScope,
                    IsIntegration = row.IsIntegration,
                    Levels = levelMap.GetValueOrDefault(row.Code) ?? [],
                    Children = children
                };
            }
            var criteria = rows.Where(r => r.Parent.Length == 0).OrderBy(r => r.Order)
                .Select(r => Build(r, new HashSet<string>(StringComparer.OrdinalIgnoreCase))).ToArray();
            if (visited.Count != rows.Count)
                return new(null, [new("Tiêu chí!B", "Cây tiêu chí có vòng lặp hoặc nhánh không nối tới gốc.")], []) { StagedRows = stagedRows };

            var draft = new UpdateRubricRequest(info.Cell("B3").GetString().Trim(), info.Cell("B4").GetString().Trim(), criteria, rubric.ConcurrencyToken)
            {
                MajorId = rubric.MajorId,
                ProjectMode = rubric.ProjectMode,
                EvaluationStage = rubric.EvaluationStage,
                EvaluationScope = rubric.EvaluationScope,
                EffectiveFrom = rubric.EffectiveFrom,
                EffectiveTo = rubric.EffectiveTo
            };
            if (draft.Name.Length is < 1 or > 200) issues.Add(new("Thông tin!B3", "Tên bộ tiêu chí cần 1–200 ký tự."));
            if (draft.Description?.Length > 2000) issues.Add(new("Thông tin!B4", "Mô tả tối đa 2.000 ký tự."));
            foreach (var row in rows)
            {
                var parent = rows.Any(x => x.Parent == row.Code);
                if (parent && (row.MaxScore.HasValue || row.Required || levelMap.ContainsKey(row.Code)))
                {
                    errorRows.Add(row.RowNumber);
                    issues.Add(new("Tiêu chí", $"Tiêu chí cha {row.Code} không có điểm tối đa, bắt buộc hay mức chất lượng."));
                }
                if (!parent && (row.MaxScore is null or <= 0 or > 999999.99m))
                {
                    errorRows.Add(row.RowNumber);
                    issues.Add(new("Tiêu chí", $"Tiêu chí lá {row.Code} cần điểm tối đa hợp lệ."));
                }
            }
            if (issues.Count > 0)
            {
                var updatedStaged = rows.Select(r => new RubricWorkbookStagedRow(
                    r.RowNumber, r.Code, r.Parent, r.Name, r.Description, r.Weight, r.MaxScore, r.Order,
                    r.Required, r.Outcome, r.Guidance, r.TargetScope, r.IsIntegration, errorRows.Contains(r.RowNumber))).ToArray();
                return new(null, issues, []) { StagedRows = updatedStaged };
            }

            var changes = new List<string>();
            if (draft.Name != rubric.Name) changes.Add("Tên bộ tiêu chí thay đổi.");
            if (draft.Description != rubric.Description) changes.Add("Mô tả thay đổi.");
            changes.Add($"Workbook có {rows.Count} tiêu chí và {levelMap.Values.Sum(v => v.Count)} mức chất lượng; bản hiện tại có {Count(rubric.Criteria)} tiêu chí.");
            return new(draft, [], changes) { StagedRows = stagedRows };
        }
        catch (ConflictException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException and not ConflictException)
        {
            return new(null, [new("Workbook", "Tệp Excel không hợp lệ hoặc không thể đọc an toàn.")], []);
        }
    }

    private static int Count(IReadOnlyList<RubricCriterionDto> rows) => rows.Sum(r => 1 + Count(r.Children));
    private static string Safe(string? text) => text is null ? "" : text.Length > 0 && "=+-@".Contains(text[0]) ? "'" + text : text;
    private static decimal Number(IXLCell cell, string address, List<RubricWorkbookIssue> issues)
    {
        if (cell.DataType == XLDataType.Number)
            return decimal.Round((decimal)cell.GetDouble(), 6);
        if (decimal.TryParse(cell.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return value;
        issues.Add(new(address, "Cần nhập số hợp lệ.")); return 0;
    }
    private static decimal? OptionalNumber(IXLCell cell, string address, List<RubricWorkbookIssue> issues) =>
        string.IsNullOrWhiteSpace(cell.GetString()) ? null : Number(cell, address, issues);
    private static int Integer(IXLCell cell, string address, List<RubricWorkbookIssue> issues)
    {
        var value = Number(cell, address, issues);
        if (value is >= 0 and <= 100000 && value == decimal.Truncate(value)) return (int)value;
        issues.Add(new(address, "Cần nhập số nguyên từ 0 đến 100.000.")); return 0;
    }
    internal static bool SafePackage(byte[] bytes)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
            if (archive.Entries.Count is < 2 or > 100 || archive.GetEntry("xl/workbook.xml") is null) return false;
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                expanded += entry.Length;
                if (expanded > 25L * 1024 * 1024 || entry.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
        catch (InvalidDataException) { return false; }
    }
}
