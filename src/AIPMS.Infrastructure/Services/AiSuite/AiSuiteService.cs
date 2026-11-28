using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiSuite;
using AIPMS.Application.Features.AiSuite.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Services.AiSuite;

internal sealed partial class AiSuiteService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IProjectAccessService projectAccess,
    IFileStorage fileStorage,
    TimeProvider clock,
    IConfiguration configuration,
    IAiTextGenerationProvider? aiProvider = null,
    IProjectScopeResolver? scopeResolver = null,
    ILogger<AiSuiteService>? logger = null) : IAiSuiteService
{
    private const string AiSuiteIdentity =
        "Bạn là Trợ lý AI chuyên sâu của hệ thống AI-PMS (Hệ thống Quản lý Đồ án Tốt nghiệp Đại học). " +
        "Tuyệt đối không tự xưng là Kiro, Claude, ChatGPT hay tên trợ lý bên thứ ba. " +
        "Trả lời bằng tiếng Việt học thuật, rõ ràng, súc tích và CHỈ dựa trên dữ liệu thực tế được cung cấp; tuyệt đối không bịa đặt tên sinh viên, điểm số hay sự kiện không tồn tại.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    private IProjectScopeResolver ScopeResolver => scopeResolver ?? new ProjectScopeResolver(db);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private string VietnamNowIso => clock.GetUtcNow().ToOffset(VietnamOffset).ToString("O");

    private long RequireActor() => currentUser.UserId ?? throw new UnauthorizedException();

    private bool IsAdmin => currentUser.Roles.Contains(AppRoles.Admin);
    private bool IsDepartmentStaff => currentUser.Roles.Contains(AppRoles.DepartmentStaff);
    private bool IsPureAdmin => IsAdmin && !IsDepartmentStaff;

    private void EnsureNotPureAdmin(string? message = null)
    {
        if (IsPureAdmin)
        {
            throw new ForbiddenException(
                message ?? "Quản trị viên hệ thống (ADMIN) không có thẩm quyền thực thi nghiệp vụ học thuật.",
                WorkflowErrorCodes.AdminNotAcademicAuthority);
        }
    }

    private void RequireDepartmentStaffOnly(string? message = null)
    {
        EnsureNotPureAdmin(message);
        if (!IsDepartmentStaff)
            throw new ForbiddenException(message ?? "Chỉ Cán bộ Khoa / Bộ môn mới có quyền thực hiện thao tác học thuật này.");
    }

    private async Task<(M.Project Project, long Actor, string ActorName, ProjectActorScope Scope)> RequireProjectAccessAsync(long projectId, CancellationToken ct)
    {
        var actor = RequireActor();
        EnsureNotPureAdmin();
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == actor && u.Status == "ACTIVE")
            .Select(u => new { u.Id, u.FullName })
            .SingleOrDefaultAsync(ct)
            ?? throw new ForbiddenException("Tài khoản không hoạt động.");

        var project = await db.Projects.AsNoTracking()
            .Include(p => p.Team)
            .SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        var scope = await ScopeResolver.ResolveAsync(actor, projectId, ct)
            ?? throw new ForbiddenException("Bạn không có quyền truy cập đồ án này.");

        return (project, actor, user.FullName, scope);
    }

    private async Task<bool> IsSupervisorOrStaffForProjectAsync(long projectId, long actor, CancellationToken ct)
    {
        if (IsPureAdmin) return false;
        if (IsDepartmentStaff)
        {
            var deptId = await db.Users.AsNoTracking()
                .Where(u => u.Id == actor && u.Status == "ACTIVE")
                .Select(u => u.DepartmentId)
                .FirstOrDefaultAsync(ct);
            if (!deptId.HasValue) return false;
            var projectScope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, requireFrozenScope: false);
            return deptId == projectScope.LeadDepartmentId || projectScope.DepartmentIds.Contains(deptId.Value);
        }
        return await db.SupervisorAssignments.AsNoTracking()
            .AnyAsync(a => a.ProjectId == projectId
                && a.EndedAt == null
                && a.AssignmentType == "PRIMARY"
                && a.IsPrimary
                && a.SupervisorProfile.UserId == actor, ct);
    }

    private IQueryable<M.Task> ScopedTasksQuery(long projectId, ProjectActorScope scope)
    {
        var q = db.Tasks.AsNoTracking()
            .Where(t => t.DeletedAt == null && t.Milestone.ProjectId == projectId && t.Status != "CANCELLED");
        if (!scope.IsFullProjectAccess)
        {
            var allowed = scope.AllowedMajorIds.ToArray();
            q = q.Where(t => db.TaskDisciplines.Any(td => td.TaskId == t.Id && allowed.Contains(td.MajorId)));
        }
        return q;
    }

    private IQueryable<M.Deliverable> ScopedDeliverablesQuery(long projectId, ProjectActorScope scope)
    {
        var q = db.Deliverables.AsNoTracking()
            .Where(d => d.ProjectId == projectId || (d.Milestone != null && d.Milestone.ProjectId == projectId));
        if (!scope.IsFullProjectAccess)
        {
            var allowed = scope.AllowedMajorIds.ToArray();
            q = q.Where(d => d.EvidenceScope == null
                || d.EvidenceScope == "COMMON"
                || d.EvidenceScope == "INTEGRATION"
                || (d.TargetMajorId.HasValue && allowed.Contains(d.TargetMajorId.Value)));
        }
        return q;
    }

    private static bool IsGateDecided(string? status) =>
        status is "PASSED" or "REVISION_REQUIRED" or "FAILED" or "OVERRIDDEN_PASSED" or "OVERRIDDEN_FAILED";

    private int ResolveTimeoutSeconds() =>
        int.TryParse(configuration["AiAssistant:TimeoutSeconds"], out var sec)
            ? Math.Clamp(sec, 10, 120)
            : 60;

    private async Task<string?> CallAiAsync(string systemPrompt, object payload, CancellationToken ct)
    {
        if (aiProvider is null) return null;
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(ResolveTimeoutSeconds()));
        try
        {
            return await aiProvider.GenerateTextAsync(systemPrompt, json, cts.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger?.LogWarning(ex, "AI Suite provider call timed out after {TimeoutSeconds}s.", ResolveTimeoutSeconds());
            return null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger?.LogWarning(ex, "AI Suite provider call failed; falling back to deterministic baseline.");
            return null;
        }
    }

    private static bool IsSimulatedModelVersion(string? modelVersion) =>
        string.IsNullOrWhiteSpace(modelVersion)
        || modelVersion.StartsWith("GROUNDED_LOCAL", StringComparison.OrdinalIgnoreCase);

    private bool IsSimulatedProvider => IsSimulatedModelVersion(aiProvider?.ModelVersion);

    private string ResolveGenerationStatus(bool parsed) =>
        (!parsed || IsSimulatedProvider) ? "FALLBACK" : "COMPLETED";

    private static AiSnapshotMetaDto BuildSnapshotMeta(
        long? snapshotId,
        string featureKey,
        string status,
        string? modelVersion,
        DateTime generatedAt,
        string? createdByName)
    {
        var simulated = IsSimulatedModelVersion(modelVersion);
        var effectiveStatus = simulated ? "FALLBACK" : status;
        return new AiSnapshotMetaDto(snapshotId, featureKey, effectiveStatus, modelVersion, generatedAt, createdByName, simulated);
    }

    private async Task<AiSnapshotMetaDto> SaveSnapshotAsync<TPayload>(
        string featureKey,
        string scopeType,
        long scopeId,
        long? projectId,
        long? departmentId,
        string status,
        string headline,
        TPayload payload,
        long actorId,
        string actorName,
        CancellationToken ct)
    {
        var now = Now;
        var modelVersion = aiProvider?.ModelVersion;
        var simulated = IsSimulatedModelVersion(modelVersion);
        var effectiveStatus = simulated ? "FALLBACK" : status;
        try
        {
            var row = new AiCopilotSnapshot
            {
                FeatureKey = featureKey,
                ScopeType = scopeType,
                ScopeId = scopeId,
                ProjectId = projectId,
                DepartmentId = departmentId,
                Status = effectiveStatus,
                ModelVersion = modelVersion,
                Headline = Clip(headline, 400),
                PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
                CreatedBy = actorId,
                CreatedAt = now
            };
            db.AiCopilotSnapshots.Add(row);
            await db.SaveChangesAsync(ct);
            await RecordAuditAsync(actorId, $"AI_SUITE_{featureKey}", scopeType, scopeId, ct);
            return new AiSnapshotMetaDto(row.Id, featureKey, effectiveStatus, modelVersion, now, actorName, simulated);
        }
        catch (Exception ex) when (IsMissingSnapshotTable(ex))
        {
            // Non-fatal if snapshot table has not been migrated yet: still return the AI result cleanly.
            return new AiSnapshotMetaDto(null, featureKey, effectiveStatus, modelVersion, now, actorName, simulated);
        }
    }

    private async Task<(AiCopilotSnapshot Row, string CreatorName)?> LoadLatestSnapshotAsync(
        string featureKey,
        string scopeType,
        long scopeId,
        CancellationToken ct)
    {
        try
        {
            var row = await db.AiCopilotSnapshots.AsNoTracking()
                .Where(x => x.FeatureKey == featureKey && x.ScopeType == scopeType && x.ScopeId == scopeId)
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);
            if (row is null) return null;
            var creatorName = await db.Users.AsNoTracking()
                .Where(u => u.Id == row.CreatedBy)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct) ?? $"Người dùng #{row.CreatedBy}";
            return (row, creatorName);
        }
        catch (Exception ex) when (IsMissingSnapshotTable(ex))
        {
            return null;
        }
    }

    private async Task RecordAuditAsync(long actorId, string action, string entityType, long entityId, CancellationToken ct)
    {
        try
        {
            db.AuditLogs.Add(new M.AuditLog
            {
                ActorUserId = actorId,
                Action = Clip(action, 80),
                EntityType = Clip(entityType, 60),
                EntityId = entityId.ToString(CultureInfo.InvariantCulture),
                Outcome = "SUCCESS",
                OccurredAt = Now
            });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Non-blocking audit record.
        }
    }

    private async Task<string?> TryExtractPdfExcerptAsync(string? storageKey, string? fileName, int maxChars, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || string.IsNullOrWhiteSpace(fileName)) return null;
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            await using var stream = await fileStorage.OpenReadAsync(storageKey, ct);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            if (ms.Length == 0 || ms.Length > 25 * 1024 * 1024) return null;
            ms.Position = 0;

            using var pdf = PdfDocument.Open(ms);
            var sb = new StringBuilder();
            foreach (var page in pdf.GetPages().Take(6))
            {
                var text = page.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                    if (sb.Length >= maxChars) break;
                }
            }
            var extracted = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(extracted) ? null : Clip(extracted, maxChars);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsMissingSnapshotTable(Exception ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur.Message.Contains("ai_copilot_snapshots", StringComparison.OrdinalIgnoreCase)
                && cur.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static JsonDocument? TryParseJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var cleaned = AiJsonSanitizer.ExtractJsonObject(raw);
            return JsonDocument.Parse(cleaned);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadString(JsonElement root, string name, string fallback = "", int max = 4000) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? Sanitize(Clip(el.GetString(), max))
            : fallback;

    private static string? ReadNullableString(JsonElement root, string name, int max = 1000)
    {
        var v = ReadString(root, name, "", max);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static int ReadInt(JsonElement root, string name, int fallback, int min = int.MinValue, int max = int.MaxValue)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var el))
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var iv))
                return Math.Clamp(iv, min, max);
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var dv))
                return Math.Clamp((int)Math.Round(dv), min, max);
        }
        return fallback;
    }

    private static decimal? ReadDecimal(JsonElement root, string name, decimal min = 0m, decimal max = 100m)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d))
            return Math.Clamp(Math.Round(d, 2), min, max);
        return null;
    }

    private static string[] ReadStringArray(JsonElement root, string name, int limit = 12, int itemMax = 500) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array
            ? el.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => Sanitize(Clip(x.GetString(), itemMax)))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(limit)
                .ToArray()
            : [];

    private static string Sanitize(string text) => AcademicKnowledgeService.SanitizePersona(text).Trim();

    private static string Clip(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var t = text.Trim();
        return t.Length <= max ? t : t[..max];
    }

    private static ValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]> { ["input"] = [message] });
}
