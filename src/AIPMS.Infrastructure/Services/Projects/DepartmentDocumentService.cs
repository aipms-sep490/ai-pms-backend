using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Application.Features.DepartmentDocuments;
using AIPMS.Application.Features.DepartmentDocuments.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class DepartmentDocumentService(AipmsDbContext db, ICurrentUser current, IFileStorage storage,
    IAuditTrail audit, TimeProvider clock, ILogger<DepartmentDocumentService> logger) : IDepartmentDocumentService
{
    private long Actor => current.IsAuthenticated && current.UserId is long id ? id : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static ValidationException Invalid(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
    private static DepartmentDocumentDto Dto(DepartmentDocumentTemplate row) => new(row.Id, row.DepartmentId, row.MajorId,
        row.ProjectPeriodId, row.Code, row.Title, row.Version, row.Status, row.FileName, row.ContentType,
        row.SizeBytes, row.Sha256, row.ConcurrencyToken.ToString("N"), DateTime.SpecifyKind(row.UploadedAt, DateTimeKind.Utc),
        row.PublishedAt.HasValue ? DateTime.SpecifyKind(row.PublishedAt.Value, DateTimeKind.Utc) : null);

    private async Task<(long? DepartmentId, long OrganizationId, long? MajorId, bool Staff)> Identity(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == Actor && u.Status == "ACTIVE")
            .Select(u => new { u.DepartmentId, u.MajorId, OrganizationId = u.Department != null ? (long?)u.Department.OrganizationId
                : u.Major != null ? (long?)u.Major.Department.OrganizationId : null,
                Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF") }).SingleOrDefaultAsync(ct);
        if (user is null || user.OrganizationId is null) throw new ForbiddenException("An active academic account is required.");
        return (user.DepartmentId, user.OrganizationId.Value, user.MajorId, user.Staff);
    }

    private async Task RequireManagement(long majorId, long? periodId, CancellationToken ct)
    {
        var user = await Identity(ct);
        if (!user.Staff || !await db.Majors.AnyAsync(m => m.Id == majorId && m.IsActive && m.DepartmentId == user.DepartmentId
            && m.Department.IsActive && m.Department.Organization.IsActive, ct))
            throw new ForbiddenException("Only staff of this major's department may manage its templates.");
        if (periodId.HasValue && !await db.ProjectPeriods.AnyAsync(p => p.Id == periodId.Value
            && p.AcademicSemester.OrganizationId == user.OrganizationId, ct))
            throw new ForbiddenException("Project period is outside the department organization.");
    }

    private async Task<bool> CanRead(DepartmentDocumentTemplate row, CancellationToken ct)
    {
        var user = await Identity(ct);
        if (!await db.Majors.AnyAsync(m => m.Id == row.MajorId && m.Department.OrganizationId == user.OrganizationId, ct)) return false;
        if (user.Staff && user.DepartmentId == row.DepartmentId) return true;
        if (row.Status != "PUBLISHED") return false;
        if (row.ProjectPeriodId is null && user.MajorId == row.MajorId) return true;
        return await db.TeamMembers.AsNoTracking().AnyAsync(m => m.UserId == Actor && m.LeftAt == null
            && m.Team.Project != null && m.Team.Project.ProjectMajors.Any(p => p.MajorId == row.MajorId)
            && (!row.ProjectPeriodId.HasValue || db.Set<ProjectRegistrationSnapshot>().Any(s => s.ProjectId == m.Team.Project.Id
                && s.ProjectPeriodId == row.ProjectPeriodId.Value)), ct);
    }

    public async Task<IReadOnlyList<DepartmentDocumentDto>> List(long majorId, long? projectPeriodId, CancellationToken ct)
    {
        var user = await Identity(ct);
        var major = await db.Majors.AsNoTracking().Where(m => m.Id == majorId && m.IsActive)
            .Select(m => new { m.DepartmentId, m.Department.OrganizationId }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Major", majorId);
        if (major.OrganizationId != user.OrganizationId) throw new ForbiddenException();
        var manage = user.Staff && user.DepartmentId == major.DepartmentId;
        var participates = user.MajorId == majorId || await db.TeamMembers.AnyAsync(m => m.UserId == Actor && m.LeftAt == null
            && m.Team.Project != null && m.Team.Project.ProjectMajors.Any(p => p.MajorId == majorId), ct);
        if (!manage && !participates) throw new ForbiddenException();
        var query = db.Set<DepartmentDocumentTemplate>().AsNoTracking().Where(x => x.MajorId == majorId);
        if (projectPeriodId.HasValue) query = query.Where(x => x.ProjectPeriodId == null || x.ProjectPeriodId == projectPeriodId.Value);
        if (!manage) query = query.Where(x => x.Status == "PUBLISHED"
            && (x.ProjectPeriodId == null || db.TeamMembers.Any(m => m.UserId == Actor && m.LeftAt == null
                && m.Team.Project != null && m.Team.Project.ProjectMajors.Any(p => p.MajorId == majorId)
                && db.Set<ProjectRegistrationSnapshot>().Any(s => s.ProjectId == m.Team.Project.Id
                    && s.ProjectPeriodId == x.ProjectPeriodId))));
        return (await query.OrderBy(x => x.Code).ThenByDescending(x => x.Version).ToArrayAsync(ct)).Select(Dto).ToArray();
    }

    public async Task<DepartmentDocumentDto> Upload(long majorId, long? projectPeriodId, string code, string title, UploadContent file, CancellationToken ct)
    {
        await RequireManagement(majorId, projectPeriodId, ct);
        code = code.Trim().ToUpperInvariant(); title = title.Trim();
        if (code.Length is < 2 or > 60 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            throw Invalid("code", "Code must contain 2–60 ASCII letters, digits, underscores or hyphens.");
        if (title.Length is < 2 or > 200) throw Invalid("title", "Title must contain 2–200 characters.");
        if (Path.GetExtension(file.FileName).ToLowerInvariant() is not (".pdf" or ".docx" or ".xlsx"))
            throw Invalid("file", "Department templates accept PDF, DOCX or XLSX.");
        var validated = await UploadValidator.ReadAsync(file, ct);
        var key = Guid.NewGuid().ToString("N");
        await using (var stream = new MemoryStream(validated.Bytes, writable: false))
            await storage.WriteAsync(key, stream, ct);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await RequireManagement(majorId, projectPeriodId, ct);
            var deptId = (await Identity(ct)).DepartmentId!.Value;
            var version = (await db.Set<DepartmentDocumentTemplate>().Where(x => x.DepartmentId == deptId
                && x.MajorId == majorId && x.ProjectPeriodId == projectPeriodId && x.Code == code)
                .MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;
            var row = new DepartmentDocumentTemplate { DepartmentId = deptId, MajorId = majorId,
                ProjectPeriodId = projectPeriodId, Code = code, Title = title, Version = version,
                FileName = validated.FileName, ContentType = validated.ContentType, SizeBytes = validated.Bytes.Length,
                Sha256 = validated.Sha256, StorageKey = key, UploadedBy = Actor, UploadedAt = Now };
            db.Set<DepartmentDocumentTemplate>().Add(row);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new(Actor, "DEPARTMENT_DOCUMENT_UPLOADED", "DEPARTMENT_DOCUMENT_TEMPLATE", row.Id,
                new Dictionary<string, object?> { ["majorId"] = majorId, ["version"] = version, ["sha256"] = row.Sha256 }), ct);
            await tx.CommitAsync(ct);
            return Dto(row);
        }
        catch
        {
            try { await storage.DeleteAsync(key, CancellationToken.None); }
            catch (Exception ex) { logger.LogError(ex, "Department document cleanup failed; storage reconciliation required"); }
            throw;
        }
    }

    private async Task<DepartmentDocumentDto> ChangeStatus(long id, string token, string target, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await db.Set<DepartmentDocumentTemplate>().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("DepartmentDocumentTemplate", id);
        await RequireManagement(row.MajorId, row.ProjectPeriodId, ct);
        if (!Guid.TryParse(token, out var parsed) || parsed != row.ConcurrencyToken)
            throw new ConflictException("Template changed; reload before retrying.");
        if (target == "PUBLISHED")
        {
            if (row.Status != "DRAFT") throw new ConflictException("Only a draft template can be published.");
            var newer = await db.Set<DepartmentDocumentTemplate>().AnyAsync(x => x.DepartmentId == row.DepartmentId
                && x.MajorId == row.MajorId && x.ProjectPeriodId == row.ProjectPeriodId && x.Code == row.Code && x.Version > row.Version, ct);
            if (newer) throw new ConflictException("Publish the latest version of this template.");
            var old = await db.Set<DepartmentDocumentTemplate>().Where(x => x.DepartmentId == row.DepartmentId
                && x.MajorId == row.MajorId && x.ProjectPeriodId == row.ProjectPeriodId && x.Code == row.Code && x.Status == "PUBLISHED").ToArrayAsync(ct);
            foreach (var prior in old) { prior.Status = "RETIRED"; prior.ConcurrencyToken = Guid.NewGuid(); }
            row.PublishedAt = Now;
        }
        else if (row.Status != "PUBLISHED") throw new ConflictException("Only a published template can be retired.");
        row.Status = target; row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, $"DEPARTMENT_DOCUMENT_{target}", "DEPARTMENT_DOCUMENT_TEMPLATE", id,
            new Dictionary<string, object?> { ["majorId"] = row.MajorId, ["version"] = row.Version }), ct);
        await tx.CommitAsync(ct);
        return Dto(row);
    }
    public Task<DepartmentDocumentDto> Publish(long id, string token, CancellationToken ct) => ChangeStatus(id, token, "PUBLISHED", ct);
    public Task<DepartmentDocumentDto> Retire(long id, string token, CancellationToken ct) => ChangeStatus(id, token, "RETIRED", ct);

    public async Task<FileDownload> Download(long id, CancellationToken ct)
    {
        var row = await db.Set<DepartmentDocumentTemplate>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("DepartmentDocumentTemplate", id);
        if (!await CanRead(row, ct)) throw new ForbiddenException();
        return new(await storage.OpenReadAsync(row.StorageKey, ct), row.ContentType, row.FileName);
    }
}
