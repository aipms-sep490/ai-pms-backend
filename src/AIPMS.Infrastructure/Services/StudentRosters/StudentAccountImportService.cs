using System.Data;
using System.IO;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.StudentRosters;
using AIPMS.Application.Features.StudentRosters.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.StudentRosters;

internal sealed class StudentAccountImportService(AipmsDbContext db, ICurrentUser currentUser,
    IPasswordHashingService passwords, IAuditTrail audit, TimeProvider clock) : IStudentAccountImportService
{
    private async Task<long> RequireAdmin(CancellationToken ct)
    {
        if (currentUser.UserId is not long id || !currentUser.IsAuthenticated) throw new UnauthorizedException();
        if (!await db.Users.AnyAsync(x => x.Id == id && x.Status == "ACTIVE" && x.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"), ct)) throw new ForbiddenException();
        return id;
    }

    public async Task<StudentAccountImportPreview> PreviewAsync(Stream file, string fileName, long majorId, CancellationToken ct)
    {
        await RequireAdmin(ct);
        using var buffer = new MemoryStream();
        var block = new byte[81920];
        int read;
        while ((read = await file.ReadAsync(block, ct)) > 0)
        {
            if (buffer.Length + read > CurriculumFileReader.MaxBytes) throw CurriculumFileReader.Invalid("IMPORT_FILE_SIZE_INVALID");
            await buffer.WriteAsync(block.AsMemory(0, read), ct);
        }
        return await ValidateAsync(majorId, StudentAccountFileReader.Read(buffer.ToArray(), fileName), ct);
    }

    private async Task<StudentAccountImportPreview> ValidateAsync(long majorId, IReadOnlyList<StudentAccountImportRow> input, CancellationToken ct)
    {
        var major = await db.Majors.Include(x => x.Department).ThenInclude(x => x.Organization)
            .SingleOrDefaultAsync(x => x.Id == majorId, ct);
        if (major is null || !major.IsActive || !major.Department.IsActive || !major.Department.Organization.IsActive)
            throw new ValidationException(new Dictionary<string, string[]> { ["majorId"] = ["IMPORT_ACTIVE_MAJOR_REQUIRED"] });
        if (input is null || input.Count is < 1 or > 500 || input.Any(x => x is null)) throw CurriculumFileReader.Invalid("IMPORT_REQUIRES_1_TO_500_ROWS");
        var rows = input.Select(x => x with { StudentCode = x.StudentCode?.Trim().ToUpperInvariant() ?? "", Email = x.Email?.Trim().ToLowerInvariant() ?? "",
            FullName = x.FullName?.Trim() ?? "", Phone = Optional(x.Phone), CurriculumCode = Optional(x.CurriculumCode) }).ToArray();
        var emails = rows.Select(x => x.Email).ToArray(); var codes = rows.Select(x => x.StudentCode).ToArray();
        var existing = await db.Users.AsNoTracking().Where(x => emails.Contains(x.Email) || codes.Contains(x.StudentCode!))
            .Select(x => new { x.Email, x.StudentCode }).ToArrayAsync(ct);
        var duplicateEmails = rows.GroupBy(x => x.Email).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet();
        var duplicateCodes = rows.GroupBy(x => x.StudentCode).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet();
        var preview = rows.Select(row =>
        {
            var errors = new List<string>();
            bool Invalid(string value, int max, bool required = false) => required && value.Length == 0 || value.Length > max || value.Any(char.IsControl);
            if (Invalid(row.StudentCode, 50, true)) errors.Add("STUDENT_CODE_INVALID");
            if (Invalid(row.FullName, 255, true)) errors.Add("FULL_NAME_INVALID");
            if (Invalid(row.Email, 255, true) || !MailAddress.TryCreate(row.Email, out var parsed) || parsed.Address != row.Email || !row.Email.Contains('.')) errors.Add("EMAIL_INVALID");
            if (Invalid(row.Phone ?? "", 30)) errors.Add("PHONE_INVALID");
            if (Invalid(row.CurriculumCode ?? "", 100)) errors.Add("CURRICULUM_CODE_INVALID");
            if (duplicateEmails.Contains(row.Email)) errors.Add("DUPLICATE_EMAIL");
            if (duplicateCodes.Contains(row.StudentCode)) errors.Add("DUPLICATE_STUDENT_CODE");
            if (existing.Any(x => string.Equals(x.Email, row.Email, StringComparison.OrdinalIgnoreCase))) errors.Add("EMAIL_ALREADY_EXISTS");
            if (existing.Any(x => string.Equals(x.StudentCode, row.StudentCode, StringComparison.OrdinalIgnoreCase))) errors.Add("STUDENT_CODE_ALREADY_EXISTS");
            return new StudentAccountImportPreviewRow(row, errors);
        }).ToArray();
        return new(major.Id, major.Name, major.Department.Name, preview, preview.All(x => x.Errors.Count == 0));
    }

    public async Task<StudentAccountImportResult> CommitAsync(StudentAccountImportCommit request, CancellationToken ct)
    {
        await RequireAdmin(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var actor = await RequireAdmin(ct);
            var preview = await ValidateAsync(request.MajorId, request.Rows, ct);
            if (!preview.CanCommit)
            {
                if (preview.Rows.Any(x => x.Errors.Any(e => e.EndsWith("_ALREADY_EXISTS", StringComparison.Ordinal))))
                    throw new ConflictException("Student identities already exist. Preview the file again.");
                throw new ValidationException(new Dictionary<string, string[]> { ["rows"] = preview.Rows.SelectMany(x => x.Errors).Distinct().ToArray() });
            }
            var role = await db.Roles.SingleOrDefaultAsync(x => x.Code == "STUDENT", ct) ?? throw new ConflictException("Student role is not configured.");
            var departmentId = await db.Majors.Where(x => x.Id == request.MajorId).Select(x => x.DepartmentId).SingleAsync(ct);
            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var item in preview.Rows.OrderBy(x => x.Account.Email, StringComparer.Ordinal))
            {
                var row = item.Account;
                var user = new User { Email = row.Email, StudentCode = row.StudentCode, FullName = row.FullName, Phone = row.Phone,
                    CurriculumCode = row.CurriculumCode, MajorId = request.MajorId, DepartmentId = departmentId,
                    Status = "ACTIVE", AcademicProfileStatus = "PENDING", GoogleEnrollmentPending = true,
                    PasswordHash = passwords.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), CreatedAt = now, UpdatedAt = now };
                user.UserRoleUsers.Add(new UserRole { RoleId = role.Id, AssignedBy = actor, AssignedAt = now });
                db.Users.Add(user);
            }
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEntry(actor, "STUDENT_ACCOUNTS_IMPORTED", "USER", null,
                new Dictionary<string, object?> { ["count"] = preview.Rows.Count, ["majorId"] = request.MajorId, ["loginMethod"] = "GOOGLE" }), ct);
            await transaction.CommitAsync(ct);
            return new(preview.Rows.Count);
        }
        catch (Exception ex)
        {
            for (Exception? inner = ex; inner is not null; inner = inner.InnerException)
                if (inner is SqlException { Number: 1205 or 1222 or 2601 or 2627 })
                    throw new ConflictException("The import conflicts with another update. Preview the file again.");
            throw;
        }
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
