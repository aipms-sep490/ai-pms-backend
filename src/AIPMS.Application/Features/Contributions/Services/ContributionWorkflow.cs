using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Contributions.Abstractions;
using AIPMS.Application.Features.Contributions.DTOs;

namespace AIPMS.Application.Features.Contributions.Services;

public sealed class ContributionWorkflow(IContributionRepository repository, IProjectAccessService access,
    ICurrentUser currentUser, IAuditTrail audit, TimeProvider clock)
{
    private long Actor() => currentUser.IsAuthenticated && currentUser.UserId.HasValue
        ? currentUser.UserId.Value : throw new UnauthorizedException();

    private async Task Authorize(long projectId, long actor, CancellationToken ct)
    {
        if (!await repository.IsActiveUserAsync(actor, ct)
            || !await access.CanAccessAsync(actor, projectId, ct))
            throw new ForbiddenException("You do not have access to this project.");
    }

    public Task<ContributionSummaryDto> Summary(long projectId, int page, int pageSize, bool snapshot, CancellationToken ct)
    {
        var actor = Actor();
        return repository.InProjectTransactionAsync(projectId, async token =>
        {
            await Authorize(projectId, actor, token);
            var summary = await repository.GetSummaryAsync(projectId, snapshot, token);
            return ContributionScoring.Page(summary, page, pageSize);
        }, ct);
    }

    public Task<PagedResult<ContributionEvidenceDto>> Evidence(long projectId, long userId, int page, int pageSize,
        string? sourceType, CancellationToken ct)
    {
        var actor = Actor();
        return repository.InProjectTransactionAsync(projectId, async token =>
        {
            await Authorize(projectId, actor, token);
            var items = await repository.GetEvidenceAsync(projectId, userId, token);
            var filtered = items.Where(e => sourceType is null || e.SourceType == sourceType)
                .OrderByDescending(e => e.OccurredAt).ThenBy(e => e.SourceType, StringComparer.Ordinal).ThenBy(e => e.SourceId).ToArray();
            return new PagedResult<ContributionEvidenceDto>(filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
                page, pageSize, filtered.Length);
        }, ct);
    }

    public Task<ContributionSummaryDto> Rebuild(long projectId, CancellationToken ct)
    {
        var actor = Actor();
        return repository.InProjectTransactionAsync(projectId, async token =>
        {
            await Authorize(projectId, actor, token);
            // Claims do not grant mutation rights after the persisted role/scope has changed.
            if (!await repository.CanRebuildAsync(projectId, actor, token))
                throw new ForbiddenException("Only academic staff in this project's scope can rebuild contribution snapshots.");
            var result = await repository.RebuildSnapshotAsync(projectId, clock.GetUtcNow().UtcDateTime, token);
            if (result.Created)
                await audit.RecordAsync(new AuditEntry(actor, "CONTRIBUTION_SNAPSHOT_REBUILT", "PROJECT", projectId,
                    new Dictionary<string, object?>
                    {
                        ["snapshotHash"] = result.Summary.SnapshotHash,
                        ["snapshotAt"] = result.Summary.SnapshotAt,
                        ["ruleVersion"] = result.Summary.RuleVersion,
                        ["memberCount"] = result.Summary.Members.Count
                    }), token);
            return result.Summary;
        }, ct);
    }
}
