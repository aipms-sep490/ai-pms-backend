using System;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Abstractions.Security;

public sealed record ProjectActorScope(
    bool IsFullProjectAccess,
    bool IsPrimarySupervisor,
    bool IsMentor,
    IReadOnlyCollection<long> AllowedMajorIds,
    bool CanAccessCommonAndIntegration)
{
    public bool CanAccessMajor(long? majorId) =>
        IsFullProjectAccess
        || (!majorId.HasValue && CanAccessCommonAndIntegration)
        || (majorId.HasValue && AllowedMajorIds.Contains(majorId.Value));

    public bool CanAccessEvidenceScope(string? evidenceScope, long? targetMajorId)
    {
        if (IsFullProjectAccess) return true;
        var normalized = (evidenceScope ?? "COMMON").Trim().ToUpperInvariant();
        if (normalized is "COMMON" or "INTEGRATION")
            return CanAccessCommonAndIntegration;
        return targetMajorId.HasValue && AllowedMajorIds.Contains(targetMajorId.Value);
    }
}

public interface IProjectScopeResolver
{
    Task<ProjectActorScope?> ResolveAsync(long userId, long projectId, CancellationToken ct = default);
}
