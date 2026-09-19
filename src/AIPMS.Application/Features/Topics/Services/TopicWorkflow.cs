using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Topics.Abstractions;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Application.Features.Topics.Models;
using AIPMS.Domain.Teams;
using AIPMS.Domain.Topics;

namespace AIPMS.Application.Features.Topics.Services;

public sealed class TopicWorkflow(ITopicRepository repository, ITeamFormationPolicyProvider policies,
    ICurrentUser currentUser, IAuditTrail audit, TimeProvider clock)
{
    private async Task<TopicActor> Actor(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long id) throw new UnauthorizedException();
        return await repository.GetActorAsync(id, currentUser.Roles, ct) ?? throw new UnauthorizedException();
    }

    private static TeamAcademicScope Scope(TopicContentRequest content, long lead) => new(content.ProjectMode,
        content.PrimaryMajorId, lead, content.Requirements.Select(r => new MajorRequirement(r.MajorId,
            r.MinMembers, r.MaxMembers, r.Responsibility)).ToArray(), Guid.Empty);

    private static TopicContentRequest Content(TopicDto topic) => new(topic.Title, topic.Description,
        topic.ProblemStatement, topic.Objectives, topic.ExpectedOutput, topic.Domain, topic.Technologies,
        topic.Keywords, topic.ProjectMode, topic.PrimaryMajorId,
        topic.Requirements.Select(r => new TopicMajorRequirementRequest(r.MajorId, r.MinMembers, r.MaxMembers, r.Responsibility)).ToArray());

    private static void RequireEditor(TopicActor actor, long lead, long? creator = null)
    {
        if (!actor.HasActiveScope || actor.DepartmentId != lead
            || !(actor.IsStaff || actor.IsLecturer && (creator is null || creator == actor.Id)))
            throw new ForbiddenException("Only lead-department staff or the topic's lecturer author can edit its draft.");
    }

    private static void RequirePublisher(TopicActor actor, long lead)
    {
        if (!actor.IsStaff || !actor.HasActiveScope || actor.DepartmentId != lead)
            throw new ForbiddenException("Only active staff of the lead department can publish or close this topic.");
    }

    private static void RequireToken(TopicDto topic, Guid token)
    {
        if (topic.ConcurrencyToken != token) throw new ConflictException("Topic changed. Reload it before retrying.");
    }

    private async Task<TopicPeriod> ValidateScope(long periodId, long lead, TopicContentRequest content, bool publish, CancellationToken ct)
    {
        var period = await repository.GetPeriodAsync(periodId, ct) ?? throw new NotFoundException("Project period", periodId);
        if (period.PeriodType != "REGISTRATION" || !period.ActiveOrganization
            || period.Status is "CLOSED" or "ARCHIVED" || period.SemesterStatus is "CLOSED" or "ARCHIVED"
            || period.EndAt <= clock.GetUtcNow().UtcDateTime)
            throw new ConflictException("Topic requires a registration period in an active organization that has not closed or ended.");
        if (!await repository.IsActiveDepartmentAsync(lead, period.OrganizationId, ct))
            throw new ConflictException("Lead department must be active in the period's organization.");
        var scope = Scope(content, lead);
        var shapeIssues = TopicRules.ScopeIssues(scope);
        if (shapeIssues.Count > 0) throw new ConflictException(string.Join(", ", shapeIssues));
        var majors = await repository.GetMajorsAsync(content.Requirements.Select(r => r.MajorId).ToArray(), ct);
        if (majors.Count != content.Requirements.Count || majors.Any(m => !m.IsActive || m.OrganizationId != period.OrganizationId)
            || !majors.Any(m => m.DepartmentId == lead))
            throw new ConflictException("Required majors must be active in the period's organization and include the lead department.");
        if (publish)
        {
            // Catalogue publication may precede registration opening, but must use its authoritative policy.
            if (period.Status != "ACTIVE" || period.SemesterStatus != "ACTIVE"
                || period.SemesterEnd < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
                throw new ConflictException("Publication requires an active registration period and semester.");
            var policy = await policies.GetAsync(periodId, ct);
            if (policy is not { IsValid: true }) throw new ConflictException("TEAM_POLICY_UNCONFIGURED");
            var issues = TopicRules.ScopeIssues(scope, policy).Concat(TopicRules.PublicationIssues(content.ProblemStatement,
                content.Objectives, content.ExpectedOutput, content.Domain, content.Technologies, content.Keywords)).ToArray();
            if (issues.Length > 0) throw new ConflictException(string.Join(", ", issues));
        }
        return period;
    }

    private Task Audit(string action, TopicDto topic, TopicActor actor, CancellationToken ct) =>
        audit.RecordAsync(new AuditEntry(actor.Id, action, "PROJECT_TOPIC", topic.Id,
            new Dictionary<string, object?> { ["projectPeriodId"] = topic.ProjectPeriodId,
                ["leadDepartmentId"] = topic.LeadDepartmentId, ["status"] = topic.Status,
                ["concurrencyToken"] = topic.ConcurrencyToken, ["reason"] = topic.CloseReason }), ct);

    public async Task<PagedResult<TopicDto>> List(TopicFilter filter, CancellationToken ct) =>
        await repository.ListAsync(await Actor(ct), filter, ct);

    public async Task<TopicDto> Get(long id, CancellationToken ct) =>
        await repository.GetAsync(id, await Actor(ct), false, ct) ?? throw new NotFoundException("Topic", id);

    public async Task<TopicDto> GetForSelection(long id, CancellationToken ct) =>
        await repository.GetByIdForSelectionAsync(id, await Actor(ct), ct) ?? throw new NotFoundException("Topic", id);

    public Task<TopicDto> Create(CreateTopicRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        RequireEditor(actor, input.LeadDepartmentId);
        await ValidateScope(input.ProjectPeriodId, input.LeadDepartmentId, input.Content, false, ct);
        var result = await repository.CreateAsync(input, actor, clock.GetUtcNow().UtcDateTime, ct);
        await Audit("TOPIC_CREATED", result, actor, ct);
        return result;
    }, ct);

    public Task<TopicDto> Update(long id, UpdateTopicRequest input, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var topic = await repository.GetAsync(id, actor, true, ct) ?? throw new NotFoundException("Topic", id);
        RequireEditor(actor, topic.LeadDepartmentId, topic.CreatedBy);
        RequireToken(topic, input.ConcurrencyToken);
        if (topic.Status != "DRAFT") throw new ConflictException("Only draft topics can be edited. Published content is immutable.");
        await ValidateScope(topic.ProjectPeriodId, topic.LeadDepartmentId, input.Content, false, ct);
        var result = await repository.UpdateAsync(id, input.Content, actor, clock.GetUtcNow().UtcDateTime, ct);
        await Audit("TOPIC_UPDATED", result, actor, ct);
        return result;
    }, ct);

    public Task<TopicDto> ChangeStatus(long id, Guid token, bool publish, string? reason, CancellationToken ct) => repository.InTransactionAsync(async () =>
    {
        var actor = await Actor(ct);
        var topic = await repository.GetAsync(id, actor, true, ct) ?? throw new NotFoundException("Topic", id);
        RequirePublisher(actor, topic.LeadDepartmentId);
        RequireToken(topic, token);
        if (publish ? topic.Status != "DRAFT" : topic.Status == "CLOSED")
            throw new ConflictException("Invalid topic state transition.");
        if (publish) await ValidateScope(topic.ProjectPeriodId, topic.LeadDepartmentId, Content(topic), true, ct);
        var result = await repository.SetStatusAsync(id, publish, reason, actor, clock.GetUtcNow().UtcDateTime, ct);
        await Audit(publish ? "TOPIC_PUBLISHED" : "TOPIC_CLOSED", result, actor, ct);
        return result;
    }, ct);
}
