using System.Data;
using System.IO;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.StudentRosters;
using AIPMS.Application.Features.StudentRosters.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.StudentRosters;

internal sealed class StudentRosterService(AipmsDbContext db, ICurrentUser currentUser, IAuditTrail audit, TimeProvider clock)
    : IStudentRosterService
{
    private async Task<long> RequireAdmin(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long actor) throw new UnauthorizedException();
        if (!await db.Users.AnyAsync(u => u.Id == actor && u.Status == "ACTIVE"
            && u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"), ct)) throw new ForbiddenException();
        return actor;
    }

    public async Task<CurriculumPreviewDto> PreviewAsync(Stream file, string fileName, CancellationToken ct)
    {
        await RequireAdmin(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await file.ReadAsync(buffer, ct)) > 0)
        {
            if (memory.Length + count > CurriculumFileReader.MaxBytes) throw CurriculumFileReader.Invalid("IMPORT_FILE_SIZE_INVALID");
            await memory.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        var inputs = CurriculumFileReader.Read(memory.ToArray(), fileName);
        var codes = inputs.Select(r => r.StudentCode).ToArray();
        var users = await db.Users.AsNoTracking().Where(u => u.StudentCode != null && codes.Contains(u.StudentCode))
            .Select(u => new { u.Id, u.StudentCode, u.FullName, u.CurriculumCode, u.RowVersion,
                IsStudent = u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT") }).ToListAsync(ct);
        var duplicates = inputs.GroupBy(r => r.StudentCode).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var rows = inputs.Select(input =>
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(input.StudentCode) || input.StudentCode.Length > 50 || input.StudentCode.Any(char.IsControl))
                errors.Add("STUDENT_CODE_INVALID");
            if (duplicates.Contains(input.StudentCode)) errors.Add("DUPLICATE_STUDENT_CODE");
            if (input.CurriculumCode.Length > 100 || input.CurriculumCode.Any(char.IsControl)) errors.Add("CURRICULUM_CODE_INVALID");
            var matches = users.Where(u => string.Equals(u.StudentCode, input.StudentCode, StringComparison.OrdinalIgnoreCase)).ToArray();
            var user = matches.Length == 1 ? matches[0] : null;
            if (user is null) errors.Add("STUDENT_NOT_FOUND");
            else if (!user.IsStudent) errors.Add("STUDENT_ROLE_REQUIRED");
            var status = errors.Count > 0 ? "ERROR" : input.CurriculumCode.Length == 0 ? "SKIPPED"
                : string.Equals(user!.CurriculumCode, input.CurriculumCode, StringComparison.Ordinal) ? "UNCHANGED" : "UPDATE";
            return new CurriculumPreviewRow(input.RowNumber, input.StudentCode, user?.Id, user?.FullName, user?.CurriculumCode,
                input.CurriculumCode, user is null ? null : Convert.ToBase64String(user.RowVersion), status, errors);
        }).ToArray();
        return new(rows, rows.All(r => r.Errors.Count == 0) && rows.Any(r => r.Status is "UPDATE" or "UNCHANGED"));
    }

    public async Task<CurriculumCommitDto> CommitAsync(CurriculumCommitRequest request, CancellationToken ct)
    {
        await RequireAdmin(ct);
        if (request.Rows is null || request.Rows.Count is < 1 or > 500
            || request.Rows.Any(r => r is null || r.UserId <= 0 || string.IsNullOrWhiteSpace(r.StudentCode)
                || r.StudentCode.Length > 50 || string.IsNullOrWhiteSpace(r.CurriculumCode) || r.CurriculumCode.Trim().Length > 100
                || r.CurriculumCode.Any(char.IsControl) || string.IsNullOrEmpty(r.ExpectedConcurrencyToken))
            || request.Rows.Select(r => r.UserId).Distinct().Count() != request.Rows.Count
            || request.Rows.Select(r => r.StudentCode.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Rows.Count)
            throw new ValidationException(new Dictionary<string, string[]> { ["rows"] = ["IMPORT_ROWS_INVALID"] });
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var actor = await RequireAdmin(ct);
            var changes = new List<object>();
            foreach (var row in request.Rows.OrderBy(r => r.UserId))
            {
                await db.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.users WITH (UPDLOCK,HOLDLOCK) WHERE id={row.UserId}").ToListAsync(ct);
                var user = await db.Users.Include(u => u.UserRoleUsers).ThenInclude(r => r.Role).SingleOrDefaultAsync(u => u.Id == row.UserId, ct)
                    ?? throw new ConflictException("IMPORT_STUDENT_CHANGED");
                if (!string.Equals(user.StudentCode, row.StudentCode.Trim(), StringComparison.OrdinalIgnoreCase)
                    || !user.UserRoleUsers.Any(r => r.Role.Code == "STUDENT")) throw new ConflictException("IMPORT_STUDENT_CHANGED");
                if (Convert.ToBase64String(user.RowVersion) != row.ExpectedConcurrencyToken) throw new ConflictException("IMPORT_STALE_VERSION");
                var code = row.CurriculumCode.Trim();
                if (user.CurriculumCode == code) continue;
                changes.Add(new { userId = user.Id, before = user.CurriculumCode, after = code });
                user.CurriculumCode = code;
                user.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            }
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new(actor, "STUDENT_CURRICULA_IMPORTED", "USER", null,
                new Dictionary<string, object?> { ["count"] = request.Rows.Count, ["changes"] = changes }), ct);
            await transaction.CommitAsync(ct);
            return new(changes.Count, request.Rows.Count - changes.Count);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            for (Exception? inner = ex; inner is not null; inner = inner.InnerException)
                if (inner is DbUpdateConcurrencyException or SqlException { Number: 1205 or 1222 }) throw new ConflictException("IMPORT_CONCURRENT_CHANGE");
            throw;
        }
    }
}
