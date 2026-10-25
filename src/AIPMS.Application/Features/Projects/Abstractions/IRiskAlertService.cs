using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.Projects.Abstractions;

public interface IRiskAlertService
{
    Task<IReadOnlyList<long>> GetProjectIdsAsync(long afterId, int limit, CancellationToken ct);
    Task ProcessProjectAsync(long projectId, DateTime nowUtc, CancellationToken ct);
}
