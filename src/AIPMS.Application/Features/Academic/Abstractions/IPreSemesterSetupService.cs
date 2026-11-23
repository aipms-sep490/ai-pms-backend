using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.AcademicKnowledge;

namespace AIPMS.Application.Features.Academic.Abstractions;

public interface IPreSemesterSetupService
{
    Task<PreSemesterReadinessDto> GetReadinessAsync(long? semesterId, long? departmentId, CancellationToken ct = default);
    Task<RolloverPreviewDto> PreviewRolloverAsync(RolloverPreviewRequest request, CancellationToken ct = default);
    Task<RolloverApplyResultDto> ApplyRolloverAsync(RolloverApplyRequest request, CancellationToken ct = default);
    Task<PreSemesterAiDiffResponseDto> AiDiffCheckAsync(long targetSemesterId, long? departmentId, AssistantUpload file, CancellationToken ct = default);
}
