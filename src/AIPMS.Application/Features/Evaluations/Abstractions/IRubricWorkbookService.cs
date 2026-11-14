using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Evaluations.DTOs;

namespace AIPMS.Application.Features.Evaluations.Abstractions;

public sealed record RubricWorkbookIssue(string Cell, string Message);
public sealed record RubricWorkbookPreview(UpdateRubricRequest? Draft, IReadOnlyList<RubricWorkbookIssue> Issues,
    IReadOnlyList<string> Changes);

public interface IRubricWorkbookService
{
    Task<byte[]> ExportAsync(long rubricId, CancellationToken ct);
    Task<RubricWorkbookPreview> PreviewAsync(long rubricId, byte[] bytes, CancellationToken ct);
}
