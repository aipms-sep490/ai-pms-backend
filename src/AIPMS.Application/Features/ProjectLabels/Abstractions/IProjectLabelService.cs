using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.ProjectLabels.Abstractions;

public interface IProjectLabelService
{
    Task<IReadOnlyList<ProjectLabelDto>> ListAsync(long projectId, CancellationToken ct = default);
    Task<ProjectLabelDto> CreateAsync(long projectId, SaveProjectLabelRequest request, CancellationToken ct = default);
    Task<ProjectLabelDto> UpdateAsync(long projectId, long labelId, SaveProjectLabelRequest request, CancellationToken ct = default);
    Task DeleteAsync(long projectId, long labelId, CancellationToken ct = default);
    Task<TaskTaxonomyStateDto> UpdateTaskTaxonomyAsync(long taskId, UpdateTaskTaxonomyRequest request, CancellationToken ct = default);
}
