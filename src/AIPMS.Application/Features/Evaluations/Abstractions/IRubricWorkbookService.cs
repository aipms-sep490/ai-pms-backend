using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Evaluations.DTOs;

namespace AIPMS.Application.Features.Evaluations.Abstractions;

public sealed record RubricWorkbookIssue(string Cell, string Message);
public sealed record RubricWorkbookStagedRow(int RowNumber, string Code, string ParentCode, string Name,
    string? Description, decimal WeightPercent, decimal? MaxScore, int SortOrder, bool IsRequired,
    string? LearningOutcome, string? EvidenceGuidance, string? TargetScope, bool IsIntegration, bool HasError);
public sealed record RubricWorkbookPreview(UpdateRubricRequest? Draft, IReadOnlyList<RubricWorkbookIssue> Issues,
    IReadOnlyList<string> Changes)
{
    public IReadOnlyList<RubricWorkbookStagedRow> StagedRows { get; init; } = [];
}

public interface IRubricWorkbookService
{
    Task<byte[]> ExportAsync(long rubricId, CancellationToken ct);
    Task<RubricWorkbookPreview> PreviewAsync(long rubricId, byte[] bytes, CancellationToken ct);
}
