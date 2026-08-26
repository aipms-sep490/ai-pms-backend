using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Projects.Models;

namespace AIPMS.Application.Features.Projects.Abstractions;

public interface IProjectProgressDataReader
{
    Task<ProjectProgressFacts?> GetProjectProgressFactsAsync(
        long projectId,
        CancellationToken cancellationToken);
}
