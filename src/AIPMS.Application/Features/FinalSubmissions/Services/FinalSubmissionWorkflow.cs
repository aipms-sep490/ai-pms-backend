using System.Security.Cryptography;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Abstractions;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Application.Features.Supervisors.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.FinalSubmissions.Services;

public sealed class FinalSubmissionWorkflow(IFinalSubmissionRepository repository, IFinalSubmissionDraftRepository drafts,
    ISupervisorProfileRepository accounts, ICurrentUser currentUser, IFileStorage storage, IAuditTrail audit,
    IPublisher publisher, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private long ActorId => currentUser.IsAuthenticated && currentUser.UserId is long id ? id : throw new UnauthorizedException();

    private async Task<FinalDraftProject> Project(long projectId, long actorId, bool teamOnly, CancellationToken ct)
    {
        var actor = await accounts.GetAccountAsync(actorId, ct);
        if (actor is null || !actor.IsActive || (!actor.HasActiveAcademicScope && !actor.Roles.Contains(AppRoles.Admin)))
            throw new ForbiddenException("An active academic account is required.");
        var project = await drafts.GetProjectAsync(projectId, actorId, ct) ?? throw new NotFoundException("Project", projectId);
        if (teamOnly ? !actor.HasActiveAcademicScope || !actor.Roles.Contains(AppRoles.Student) || !project.IsMember
            : !await repository.CanReadAsync(projectId, actorId, ct)) throw new ForbiddenException();
        return project;
    }

    private static void Current(string expected, string? supplied)
    {
        if (!Guid.TryParse(supplied, out var token) || token != Guid.Parse(expected))
            throw new ConflictException("The draft or required checklist changed. Reload before retrying.");
    }

    public Task<FinalRequirementsDto> Requirements(long projectId, CancellationToken ct) => drafts.InTransactionAsync(async () =>
    {
        await Project(projectId, ActorId, false, ct);
        var requirements = await repository.RequirementsAsync(projectId, ct);
        var items = await repository.DeliverablesAsync(projectId, requirements?.DeliverableIds ?? [], ct);
        return new FinalRequirementsDto(projectId, requirements?.ConcurrencyToken,
            items.Select(i => new FinalRequirementDefinitionDto(i.Id, i.Title)).ToArray());
    }, ct);

    public Task<FinalRequirementsDto> Configure(long projectId, ConfigureFinalRequirementsRequest input, CancellationToken ct) => drafts.InTransactionAsync(async () =>
    {
        var actor = ActorId;
        await drafts.LockProjectAsync(projectId, ct);
        var project = await Project(projectId, actor, false, ct);
        if (!await repository.CanManageAsync(projectId, actor, ct)) throw new ForbiddenException("Only the managed department staff or administrator can configure final requirements.");
        if (project.Status != "ACTIVE" || !project.AcademicScopeActive || await repository.GetAsync(projectId, ct) is not null)
            throw new ConflictException("Requirements can only change before submission on an ACTIVE project.");
        var before = await repository.RequirementsAsync(projectId, ct);
        if (before is not null) Current(before.ConcurrencyToken, input.ConcurrencyToken);
        else if (input.ConcurrencyToken is not null) throw new ConflictException("The required checklist does not exist. Reload before creating it.");
        var items = await repository.DeliverablesAsync(projectId, input.DeliverableIds, ct);
        if (items.Count != input.DeliverableIds.Count) throw new ConflictException("Required deliverables must belong to this project.");
        var saved = await repository.ConfigureAsync(projectId, input.DeliverableIds, actor, Now, ct);
        await audit.RecordAsync(new(actor, "FINAL_REQUIREMENTS_CONFIGURED", "PROJECT", projectId,
            new Dictionary<string, object?> { ["before"] = before, ["after"] = saved }), ct);
        return new FinalRequirementsDto(projectId, saved.ConcurrencyToken,
            items.Select(i => new FinalRequirementDefinitionDto(i.Id, i.Title)).ToArray());
    }, ct);

    public Task<FinalSubmissionChecklistDto> Checklist(long projectId, CancellationToken ct) => drafts.InTransactionAsync(async () =>
    {
        var project = await Project(projectId, ActorId, true, ct);
        return (await Check(project, ct)).Checklist;
    }, ct);

    private sealed record Checked(FinalSubmissionChecklistDto Checklist, FinalDraftRecord? Draft,
        IReadOnlyList<FinalDraftVersion> Versions, IReadOnlyList<FinalSnapshotFile> Files);

    private async Task<Checked> Check(FinalDraftProject project, CancellationToken ct)
    {
        var draft = await drafts.GetAsync(project.Id, ct);
        var requirements = await repository.RequirementsAsync(project.Id, ct);
        var period = draft is null ? null : await drafts.GetPeriodAsync(draft.ProjectPeriodId, Now, ct);
        var blockers = (await FinalSubmissionRules.Blockers(drafts, project, period, Now, ct)).ToList();
        if (await repository.GetAsync(project.Id, ct) is not null) blockers.Add("ALREADY_SUBMITTED");
        if (draft is null) blockers.Add("DRAFT_REQUIRED");
        if (requirements is null || requirements.DeliverableIds.Count == 0) blockers.Add("REQUIREMENTS_NOT_CONFIGURED");
        var versions = await drafts.GetVersionsAsync(project.Id, draft?.VersionIds ?? [], ct);
        if (versions.Count != (draft?.VersionIds.Count ?? 0)) blockers.Add("VERSION_NOT_IN_PROJECT");
        if (versions.Count == 0) blockers.Add("EMPTY_PACKAGE");
        if (versions.Select(v => v.DeliverableId).Distinct().Count() != versions.Count) blockers.Add("DUPLICATE_DELIVERABLE_VERSION");
        var files = await repository.FilesAsync(versions.Select(v => v.Id).ToArray(), ct);
        var invalid = new HashSet<long>();
        foreach (var version in versions)
        {
            if (!version.FilesValid || version.Status is not ("SUBMITTED" or "ACCEPTED"))
            {
                invalid.Add(version.Id);
                blockers.Add($"VERSION_INELIGIBLE:{version.Id}");
                continue;
            }
            foreach (var file in files.Where(f => f.Metadata.ParentId == version.Id))
                if (!await ValidContent(file, ct))
                {
                    invalid.Add(version.Id);
                    blockers.Add($"FILE_CONTENT_INVALID:{file.Metadata.Id}");
                }
        }
        var required = await repository.DeliverablesAsync(project.Id, requirements?.DeliverableIds ?? [], ct);
        var items = new List<FinalRequirementDto>();
        foreach (var id in requirements?.DeliverableIds ?? [])
        {
            var deliverable = required.SingleOrDefault(d => d.Id == id);
            var version = versions.FirstOrDefault(v => v.DeliverableId == id);
            var complete = deliverable is not null && version is not null && !invalid.Contains(version.Id);
            items.Add(new(id, deliverable?.Title ?? "Unavailable deliverable", version?.Id, complete));
            if (!complete) blockers.Add($"REQUIRED_DELIVERABLE_MISSING:{id}");
        }
        // Content verification can take time; readiness uses the time after storage reads.
        blockers.AddRange(await FinalSubmissionRules.Blockers(drafts, project, period, Now, ct));
        return new(new(project.Id, draft?.ProjectPeriodId, period?.EndAt, draft?.ConcurrencyToken,
            requirements?.ConcurrencyToken, blockers.Count == 0, blockers.Distinct().ToArray(), items), draft, versions, files);
    }

    private async Task<bool> ValidContent(FinalSnapshotFile file, CancellationToken ct)
    {
        try
        {
            await using var stream = await storage.OpenReadAsync(file.StorageKey, ct);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536];
            long size = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                size += read;
                if (size > file.Metadata.SizeBytes) return false;
                hash.AppendData(buffer, 0, read);
            }
            return size == file.Metadata.SizeBytes && string.Equals(Convert.ToHexString(hash.GetHashAndReset()),
                file.Metadata.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (ArgumentException) { return false; }
    }

    public Task<FinalSubmissionDto> Submit(long projectId, SubmitFinalSubmissionRequest input, CancellationToken ct) => drafts.InTransactionAsync(async () =>
    {
        var actor = ActorId;
        await drafts.LockProjectAsync(projectId, ct);
        var project = await Project(projectId, actor, true, ct);
        if (!project.IsLeader) throw new ForbiddenException("Only the current student leader can submit the final package.");
        var result = await Check(project, ct);
        if (!result.Checklist.CanSubmit) throw new ConflictException(string.Join(", ", result.Checklist.Blockers));
        Current(result.Checklist.DraftConcurrencyToken!, input.DraftConcurrencyToken);
        Current(result.Checklist.RequirementsConcurrencyToken!, input.RequirementsConcurrencyToken);
        var required = result.Checklist.Items.Select(i => i.DeliverableId).ToHashSet();
        var now = Now;
        var submission = new FinalSubmissionRecord(0, projectId, result.Draft!.ProjectPeriodId, actor, now,
            result.Checklist.Deadline!.Value, result.Draft.Notes, result.Draft.ConcurrencyToken,
            result.Checklist.RequirementsConcurrencyToken!, result.Versions.Select(v => new FinalSnapshotItem(v.Id,
                v.DeliverableId, v.Title, v.VersionNumber, v.Status, required.Contains(v.DeliverableId),
                result.Files.Where(f => f.Metadata.ParentId == v.Id).ToArray())).ToArray());
        var saved = await repository.SubmitAsync(submission, ct);
        await audit.RecordAsync(new(actor, "FINAL_SUBMISSION_LOCKED", "FINAL_SUBMISSION", saved.Id,
            new Dictionary<string, object?> { ["projectId"] = projectId, ["beforeStatus"] = "ACTIVE",
                ["afterStatus"] = "FINAL_SUBMISSION", ["snapshot"] = saved.ToDto() }), ct);
        await publisher.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.FinalSubmissionLocked, saved.Id, actor, now), ct);
        var period = await drafts.GetPeriodAsync(saved.ProjectPeriodId, Now, ct);
        var blockers = await FinalSubmissionRules.Blockers(drafts, project, period, Now, ct);
        if (blockers.Count != 0) throw new ConflictException(string.Join(", ", blockers));
        return saved.ToDto();
    }, ct);

    private async Task<FinalSubmissionRecord> Read(long projectId, CancellationToken ct)
    {
        await Project(projectId, ActorId, false, ct);
        return await repository.GetAsync(projectId, ct) ?? throw new NotFoundException("FinalSubmission", projectId);
    }

    public Task<FinalSubmissionDto> Get(long projectId, CancellationToken ct) => drafts.InTransactionAsync(async () => (await Read(projectId, ct)).ToDto(), ct);

    public async Task<FileDownload> Download(long projectId, long fileId, CancellationToken ct)
    {
        var file = await drafts.InTransactionAsync(async () =>
        {
            var snapshot = await Read(projectId, ct);
            return snapshot.Items.SelectMany(i => i.Files).SingleOrDefault(f => f.Metadata.Id == fileId)
                ?? throw new NotFoundException("Final submission file", fileId);
        }, ct);
        try { return new(await storage.OpenReadAsync(file.StorageKey, ct), file.Metadata.ContentType, file.Metadata.FileName); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { throw new NotFoundException("File content", fileId); }
    }
}
