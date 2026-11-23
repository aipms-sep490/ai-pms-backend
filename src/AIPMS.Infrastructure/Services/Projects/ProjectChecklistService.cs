using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ProjectChecklists;
using AIPMS.Application.Features.ProjectChecklists.Abstractions;
using AIPMS.Domain.Exceptions;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ProjectChecklistService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    TimeProvider clock) : IProjectChecklistService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private long ActorId => currentUser.IsAuthenticated && currentUser.UserId.HasValue
        ? currentUser.UserId.Value
        : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<(bool IsAdmin, bool IsStaff, bool IsLecturer, bool IsStudent, long? DepartmentId)> GetActorAsync(CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().Where(x => x.Id == ActorId && x.Status == "ACTIVE")
            .Select(x => new
            {
                x.DepartmentId,
                IsAdmin = x.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"),
                IsStaff = x.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"),
                IsLecturer = x.UserRoleUsers.Any(r => r.Role.Code == "LECTURER"),
                IsStudent = x.UserRoleUsers.Any(r => r.Role.Code == "STUDENT")
            })
            .SingleOrDefaultAsync(ct) ?? throw new ForbiddenException();
        return (u.IsAdmin, u.IsStaff, u.IsLecturer, u.IsStudent, u.DepartmentId);
    }

    private static IReadOnlyList<MasterChecklistItemDefinition> ParseItems(string json)
    {
        try { return JsonSerializer.Deserialize<List<MasterChecklistItemDefinition>>(json, JsonOpts) ?? []; }
        catch { return []; }
    }

    private static MasterChecklistTemplateDto ToTemplateDto(ReviewGateChecklistTemplate t) => new(
        t.Id, t.AcademicSemesterId, t.TrackCode, t.Code, t.Name, t.Version, t.Status, t.LeadDepartmentId,
        ParseItems(t.ItemsJson), t.ConcurrencyToken.ToString("N"), t.CreatedAt, t.PublishedAt);

    public static ChecklistNormalizedScoreDto ComputeNormalizedScore(IEnumerable<ProjectGateChecklistItem> items)
    {
        var list = items.ToList();
        var applicable = list.Where(i => i.IsApplicable).ToList();
        var scored = applicable.Where(i => i.ReviewerScore.HasValue).ToList();
        var totalWeight = applicable.Sum(i => i.Weight);
        var earned = scored.Sum(i => i.ReviewerScore!.Value * i.Weight);
        decimal? normalized = null;
        var band = "Unscored";
        if (totalWeight > 0m && scored.Count > 0)
        {
            normalized = Math.Round((earned / totalWeight) * 10m, 2, MidpointRounding.AwayFromZero);
            if (normalized > 10.00m) normalized = 10.00m;
            band = normalized.Value switch
            {
                >= 8.50m => "Excellent",
                >= 7.00m => "Good",
                >= 5.00m => "Acceptable",
                _ => "Fail"
            };
        }
        return new ChecklistNormalizedScoreDto(totalWeight, earned, normalized, band, applicable.Count, scored.Count, list.Count - applicable.Count);
    }

    private static string ComputeConsensus(string advisorRec, string reviewerRec)
    {
        if (advisorRec == "PENDING" || reviewerRec == "PENDING")
            return "PENDING_RECOMMENDATIONS";
        return string.Equals(advisorRec, reviewerRec, StringComparison.Ordinal) ? "AGREE" : "DISAGREE";
    }

    private static ProjectGateChecklistDto ToChecklistDto(
        ProjectGateChecklist c,
        string gateCode,
        IEnumerable<ProjectGateChecklistItem> visibleItems,
        bool maskEvaluations)
    {
        var itemsList = visibleItems.OrderBy(i => i.Id).ToList();
        var score = maskEvaluations ? null : ComputeNormalizedScore(itemsList);
        return new ProjectGateChecklistDto(
            c.Id, c.ProjectId, c.GateInstanceId, gateCode, c.TemplateId, c.Version, c.Status,
            maskEvaluations ? "PENDING" : c.AdvisorRecommendation,
            maskEvaluations ? "PENDING" : c.ReviewerRecommendation,
            maskEvaluations ? "PENDING_RECOMMENDATIONS" : c.Review3ConsensusStatus,
            c.PublishedBy, c.PublishedAt, c.ConcurrencyToken.ToString("N"),
            score,
            itemsList.Select(i => new ProjectChecklistItemDto(
                i.Id, i.ItemCode, i.Section, i.TrackScope, i.MajorId, i.ConditionTag, i.CoreQuestion,
                i.Priority, i.Weight, i.IsApplicable, i.NaReason,
                i.TailoredCheckpoints, i.TailoredEvidence, i.ReviewerFocusPrompt, i.SourceRef,
                maskEvaluations ? null : i.AdvisoryStatus,
                maskEvaluations ? null : i.AdvisoryNotes,
                maskEvaluations ? null : i.AdvisoryBy,
                maskEvaluations ? null : i.ReviewerScore,
                maskEvaluations ? null : i.ReviewerNotes,
                maskEvaluations ? null : i.ReviewerBy)).ToArray());
    }

    public async Task<IReadOnlyList<MasterChecklistTemplateDto>> ListTemplatesAsync(long? semesterId, string? trackCode, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        if (!actor.IsAdmin && !actor.IsStaff) throw new ForbiddenException();

        var q = db.ReviewGateChecklistTemplates.AsNoTracking().AsQueryable();
        if (semesterId.HasValue) q = q.Where(t => t.AcademicSemesterId == semesterId.Value);
        if (!string.IsNullOrWhiteSpace(trackCode)) q = q.Where(t => t.TrackCode == trackCode.Trim().ToUpperInvariant());
        if (!actor.IsAdmin && actor.DepartmentId.HasValue) q = q.Where(t => t.LeadDepartmentId == actor.DepartmentId.Value);
        return (await q.OrderByDescending(t => t.CreatedAt).ToListAsync(ct)).Select(ToTemplateDto).ToArray();
    }

    public async Task<MasterChecklistTemplateDto> CreateTemplateAsync(CreateMasterChecklistTemplateRequest request, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        if (!actor.IsAdmin && !(actor.IsStaff && actor.DepartmentId == request.LeadDepartmentId))
            throw new ForbiddenException("Only lead department staff or admin can author master checklist templates.");

        var track = AcademicTrackCodes.Normalize(request.TrackCode)
            ?? throw new DomainException("TrackCode must be IT_ONLY, MKT_ONLY, or IT_MKT.", "INVALID_TRACK_CODE");
        if (request.Items is null || request.Items.Count == 0)
            throw new DomainException("Master checklist template must contain at least one item.", "CHECKLIST_ITEMS_REQUIRED");
        if (request.Items.Select(i => i.ItemCode.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Items.Count)
            throw new DomainException("Checklist item codes must be unique within a template.", "DUPLICATE_ITEM_CODE");
        if (request.Items.Any(i => i.Weight <= 0m || i.Priority is not ("Mandatory" or "Recommended" or "Conditional")))
            throw new DomainException("Every checklist item must have Weight > 0 and valid Priority (Mandatory/Recommended/Conditional).", "INVALID_CHECKLIST_ITEM");

        var now = Now;
        var entity = new ReviewGateChecklistTemplate
        {
            AcademicSemesterId = request.AcademicSemesterId,
            TrackCode = track,
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            Version = 1,
            Status = "DRAFT",
            LeadDepartmentId = request.LeadDepartmentId,
            ItemsJson = JsonSerializer.Serialize(request.Items, JsonOpts),
            ConcurrencyToken = Guid.NewGuid(),
            CreatedBy = ActorId,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ReviewGateChecklistTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToTemplateDto(entity);
    }

    public async Task<MasterChecklistTemplateDto> PublishTemplateAsync(long templateId, CancellationToken ct = default)
    {
        var actor = await GetActorAsync(ct);
        var tpl = await db.ReviewGateChecklistTemplates.SingleOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new NotFoundException("ReviewGateChecklistTemplate", templateId);
        if (!actor.IsAdmin && !(actor.IsStaff && actor.DepartmentId == tpl.LeadDepartmentId))
            throw new ForbiddenException();
        if (tpl.Status == "PUBLISHED") return ToTemplateDto(tpl);
        if (tpl.Status == "RETIRED")
            throw new ConflictException("Retired checklist template is immutable.", WorkflowErrorCodes.ImmutableVersion);

        var now = Now;
        tpl.Status = "PUBLISHED";
        tpl.PublishedAt = now;
        tpl.UpdatedAt = now;
        tpl.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return ToTemplateDto(tpl);
    }
}
