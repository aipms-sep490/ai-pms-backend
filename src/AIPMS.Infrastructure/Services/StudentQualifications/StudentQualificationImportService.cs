using System.Data;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.StudentQualifications.Abstractions;
using AIPMS.Application.Features.StudentQualifications.DTOs;
using AIPMS.Application.Features.StudentQualifications.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Projects;
using AIPMS.Infrastructure.Services.StudentRosters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.StudentQualifications;

internal sealed class StudentQualificationImportService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IStudentQualificationImportService
{
    private async Task<(long ActorId, long DepartmentId, string DepartmentName, long OrganizationId)> RequireDepartmentStaffAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId)
            throw new UnauthorizedException();

        var actor = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId && u.Status == "ACTIVE")
            .Select(u => new
            {
                u.DepartmentId,
                DepartmentName = u.Department == null ? null : u.Department.Name,
                OrganizationId = u.Department == null ? (long?)null : u.Department.OrganizationId,
                IsStaff = u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.DepartmentStaff)
                    && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive,
                IsAdmin = u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Admin)
            })
            .SingleOrDefaultAsync(ct) ?? throw new ForbiddenException();

        if (!actor.IsStaff || !actor.DepartmentId.HasValue || !actor.OrganizationId.HasValue)
        {
            throw new ForbiddenException(
                actor.IsAdmin
                    ? "Platform administrators without DEPARTMENT_STAFF role cannot import student qualifications."
                    : "An active department staff account is required to import student qualifications.",
                actor.IsAdmin ? WorkflowErrorCodes.AdminNotAcademicAuthority : null);
        }

        return (actorId, actor.DepartmentId.Value, actor.DepartmentName ?? string.Empty, actor.OrganizationId.Value);
    }

    public async Task<StudentQualificationImportPreview> PreviewAsync(Stream file, string fileName, CancellationToken ct = default)
    {
        var staff = await RequireDepartmentStaffAsync(ct);
        using var buffer = new MemoryStream();
        var block = new byte[81920];
        int read;
        while ((read = await file.ReadAsync(block, ct)) > 0)
        {
            if (buffer.Length + read > CurriculumFileReader.MaxBytes)
                throw CurriculumFileReader.Invalid("IMPORT_FILE_SIZE_INVALID");
            await buffer.WriteAsync(block.AsMemory(0, read), ct);
        }
        var parsedRows = ReadRows(buffer.ToArray(), fileName);
        return await ValidateAsync(staff.DepartmentId, staff.DepartmentName, parsedRows, ct);
    }

    public async Task<StudentQualificationImportResult> CommitAsync(StudentQualificationImportCommit request, CancellationToken ct = default)
    {
        var staff = await RequireDepartmentStaffAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var preview = await ValidateAsync(staff.DepartmentId, staff.DepartmentName, request.Rows, ct);
            if (!preview.CanCommit)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["rows"] = preview.Rows.SelectMany(x => x.Errors).Distinct().ToArray()
                });
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var matchedUserIds = preview.Rows.Select(r => r.MatchedUserId!.Value).Distinct().OrderBy(id => id).ToArray();
            var existingList = await db.Set<StudentQualification>()
                .Where(q => matchedUserIds.Contains(q.UserId))
                .ToListAsync(ct);
            var existingMap = existingList.ToDictionary(q => (q.UserId, q.QualificationType));

            var created = 0;
            var updated = 0;
            foreach (var item in preview.Rows.OrderBy(r => r.MatchedUserId!.Value).ThenBy(r => r.Row.QualificationType, StringComparer.Ordinal))
            {
                var userId = item.MatchedUserId!.Value;
                var row = item.Row;
                if (existingMap.TryGetValue((userId, row.QualificationType), out var entity))
                {
                    entity.OrganizationId = staff.OrganizationId;
                    entity.TrainingStatus = StudentTrainingStatuses.Completed;
                    entity.VerificationStatus = StudentQualificationStatuses.Verified;
                    entity.CertificateNumber = string.IsNullOrWhiteSpace(row.CertificateNumber) ? entity.CertificateNumber : row.CertificateNumber;
                    entity.IssuedAt = row.IssuedAt ?? entity.IssuedAt ?? now;
                    entity.ExpiresAt = row.ExpiresAt;
                    entity.VerifiedBy = staff.ActorId;
                    entity.VerifiedAt = now;
                    entity.RejectionReason = null;
                    entity.ConcurrencyToken = Guid.NewGuid();
                    entity.UpdatedAt = now;
                    updated++;
                }
                else
                {
                    entity = new StudentQualification
                    {
                        UserId = userId,
                        OrganizationId = staff.OrganizationId,
                        QualificationType = row.QualificationType,
                        TrainingStatus = StudentTrainingStatuses.Completed,
                        VerificationStatus = StudentQualificationStatuses.Verified,
                        CertificateNumber = row.CertificateNumber,
                        IssuedAt = row.IssuedAt ?? now,
                        ExpiresAt = row.ExpiresAt,
                        VerifiedBy = staff.ActorId,
                        VerifiedAt = now,
                        RejectionReason = null,
                        ConcurrencyToken = Guid.NewGuid(),
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    db.Set<StudentQualification>().Add(entity);
                    existingMap[(userId, row.QualificationType)] = entity;
                    created++;
                }
            }

            await db.SaveChangesAsync(ct);

            await audit.RecordAsync(new AuditEntry(
                staff.ActorId,
                "STUDENT_QUALIFICATIONS_BULK_IMPORTED",
                "DEPARTMENT",
                staff.DepartmentId,
                new Dictionary<string, object?>
                {
                    ["departmentId"] = staff.DepartmentId,
                    ["totalRows"] = preview.Rows.Count,
                    ["createdCount"] = created,
                    ["updatedCount"] = updated
                }), ct);

            await V4WorkflowNotificationHelper.EmitAsync(
                db,
                staff.ActorId,
                "STUDENT_QUALIFICATION_VERIFIED",
                "Điều kiện tham gia đồ án của bạn đã được xác nhận",
                "Giáo vụ Khoa đã nhập và xác nhận danh sách sinh viên đủ điều kiện tham gia đồ án.",
                "DEPARTMENT",
                staff.DepartmentId,
                matchedUserIds,
                now,
                enqueueEmail: false,
                ct);

            await tx.CommitAsync(ct);
            return new StudentQualificationImportResult(created + updated, created, updated);
        }
        catch (Exception ex)
        {
            for (Exception? inner = ex; inner is not null; inner = inner.InnerException)
                if (inner is SqlException { Number: 1205 or 1222 or 2601 or 2627 })
                    throw new ConflictException("The qualification import conflicts with another update. Preview and retry.");
            throw;
        }
    }

    private async Task<StudentQualificationImportPreview> ValidateAsync(
        long departmentId,
        string departmentName,
        IReadOnlyList<StudentQualificationImportRow> input,
        CancellationToken ct)
    {
        if (input is null || input.Count is < 1 or > 500 || input.Any(x => x is null))
            throw CurriculumFileReader.Invalid("IMPORT_REQUIRES_1_TO_500_ROWS");

        var normalized = input.Select(r => r with
        {
            StudentCode = r.StudentCode?.Trim().ToUpperInvariant() ?? "",
            QualificationType = string.IsNullOrWhiteSpace(r.QualificationType)
                ? StudentQualificationTypes.CapstoneReadiness
                : r.QualificationType.Trim().ToUpperInvariant(),
            CertificateNumber = string.IsNullOrWhiteSpace(r.CertificateNumber) ? null : r.CertificateNumber.Trim()
        }).ToArray();

        var studentCodes = normalized.Select(r => r.StudentCode).Where(c => c.Length > 0).Distinct().ToArray();
        var students = await db.Users.AsNoTracking()
            .Where(u => u.StudentCode != null && studentCodes.Contains(u.StudentCode)
                && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Student))
            .Select(u => new
            {
                u.Id,
                u.StudentCode,
                u.FullName,
                u.Status,
                DepartmentId = u.Major != null ? u.Major.DepartmentId : u.DepartmentId
            })
            .ToArrayAsync(ct);

        var studentMap = students
            .Where(s => s.StudentCode is not null)
            .GroupBy(s => s.StudentCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var duplicatePairs = normalized
            .GroupBy(r => (r.StudentCode, r.QualificationType))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        var previewRows = normalized.Select(row =>
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(row.StudentCode) || row.StudentCode.Length > 50)
                errors.Add("STUDENT_CODE_INVALID");
            if (!StudentQualificationTypes.IsValid(row.QualificationType))
                errors.Add(WorkflowErrorCodes.InvalidQualificationType);
            if (row.CertificateNumber is not null && row.CertificateNumber.Length > 100)
                errors.Add("CERTIFICATE_NUMBER_INVALID");
            if (row.IssuedAt.HasValue && row.ExpiresAt.HasValue && row.ExpiresAt.Value <= row.IssuedAt.Value)
                errors.Add("EXPIRATION_BEFORE_ISSUED");
            if (duplicatePairs.Contains((row.StudentCode, row.QualificationType)))
                errors.Add("DUPLICATE_ROW");

            long? matchedUserId = null;
            string? fullName = null;
            if (!string.IsNullOrWhiteSpace(row.StudentCode))
            {
                if (!studentMap.TryGetValue(row.StudentCode, out var matched))
                {
                    errors.Add("STUDENT_NOT_FOUND");
                }
                else
                {
                    matchedUserId = matched.Id;
                    fullName = matched.FullName;
                    if (matched.DepartmentId != departmentId)
                        errors.Add(WorkflowErrorCodes.StudentOutsideDepartment);
                    if (matched.Status != "ACTIVE")
                        errors.Add("STUDENT_NOT_ACTIVE");
                }
            }

            return new StudentQualificationImportPreviewRow(row, matchedUserId, fullName, errors);
        }).ToArray();

        return new StudentQualificationImportPreview(
            departmentId,
            departmentName,
            previewRows,
            previewRows.All(r => r.Errors.Count == 0));
    }

    private static IReadOnlyList<StudentQualificationImportRow> ReadRows(byte[] bytes, string fileName)
    {
        if (bytes.Length == 0 || bytes.Length > CurriculumFileReader.MaxBytes)
            throw CurriculumFileReader.Invalid("IMPORT_FILE_SIZE_INVALID");
        try
        {
            var rows = Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".csv" => CurriculumFileReader.ReadCsv(bytes),
                ".xlsx" => CurriculumFileReader.ReadExcel(bytes),
                _ => throw CurriculumFileReader.Invalid("IMPORT_FORMAT_REQUIRED_XLSX_OR_CSV")
            };
            if (rows.Count < 2) throw CurriculumFileReader.Invalid("IMPORT_NO_DATA");
            var headers = rows[0].Select(x => x.Trim().TrimStart('\uFEFF')).ToArray();
            int Column(bool required, params string[] names)
            {
                var found = headers.Select((name, index) => (name, index))
                    .Where(x => names.Contains(x.name, StringComparer.OrdinalIgnoreCase)).ToArray();
                if (found.Length > 1 || (required && found.Length != 1))
                    throw CurriculumFileReader.Invalid("IMPORT_HEADERS_REQUIRE_MSSV_AND_TYPE");
                return found.Length == 0 ? -1 : found[0].index;
            }

            var studentCol = Column(true, "MSSV", "studentCode", "MaSV", "Mã sinh viên");
            var typeCol = Column(false, "Ma loai chung chi", "Mã loại chứng chỉ", "qualificationType", "type", "LoaiChungChi");
            var certCol = Column(false, "So chung nhan", "Số chứng nhận", "certificateNumber", "SoChungNhan");
            var issuedCol = Column(false, "Ngay cap", "Ngày cấp", "issuedAt", "NgayCap");
            var expiresCol = Column(false, "Ngay het han", "Ngày hết hạn", "expiresAt", "NgayHetHan");

            var output = new List<StudentQualificationImportRow>();
            for (var i = 1; i < rows.Count; i++)
            {
                if (rows[i].All(string.IsNullOrWhiteSpace)) continue;
                string Value(int col) => col >= 0 && col < rows[i].Length ? rows[i][col].Trim() : "";
                DateTime? ParseDate(string raw)
                {
                    if (string.IsNullOrWhiteSpace(raw)) return null;
                    if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                    if (double.TryParse(raw, CultureInfo.InvariantCulture, out var oaDate) && oaDate > 1)
                        return DateTime.SpecifyKind(DateTime.FromOADate(oaDate), DateTimeKind.Utc);
                    throw CurriculumFileReader.Invalid("IMPORT_DATE_INVALID");
                }

                output.Add(new StudentQualificationImportRow(
                    i + 1,
                    Value(studentCol),
                    string.IsNullOrWhiteSpace(Value(typeCol)) ? StudentQualificationTypes.CapstoneReadiness : Value(typeCol),
                    string.IsNullOrWhiteSpace(Value(certCol)) ? null : Value(certCol),
                    ParseDate(Value(issuedCol)),
                    ParseDate(Value(expiresCol))));
            }
            if (output.Count is < 1 or > 500)
                throw CurriculumFileReader.Invalid("IMPORT_REQUIRES_1_TO_500_ROWS");
            return output;
        }
        catch (Exception ex) when (ex is not ValidationException && ex is not OutOfMemoryException && ex is not OperationCanceledException)
        {
            throw CurriculumFileReader.Invalid("IMPORT_FILE_INVALID");
        }
    }
}
