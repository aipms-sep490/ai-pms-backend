using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed record ColdEvidenceBinding(long PackageId, IReadOnlyList<long> SubmissionIds, int FileCount, string Hash);
internal sealed record ColdEvidenceSet(ColdPackage Package, IReadOnlyList<ColdChecklistItem> Items, IReadOnlyList<ColdSubmission> Submissions);

internal static class ColdEvaluationBinding
{
    internal static bool Includes(string scope, long? major, long? student, ColdChecklistItem item) => scope switch
    {
        "COMMON" => true,
        "MAJOR_SPECIFIC" => major.HasValue && item.MajorId == major,
        "INDIVIDUAL" => major.HasValue && student.HasValue && item.MajorId == major && item.StudentId == student,
        _ => false
    };

    internal static async Task<ColdEvidenceBinding> Capture(AipmsDbContext db, long projectId, string scope, long? major, long? student, CancellationToken ct)
        => Capture(await Load(db, projectId, ct), scope, major, student);

    internal static async Task<ColdEvidenceSet> Load(AipmsDbContext db, long projectId, CancellationToken ct)
    {
        var package = await db.Set<ColdPackage>().AsNoTracking().SingleOrDefaultAsync(p => p.ProjectId == projectId, ct);
        if (package is null || package.Status != "LOCKED") throw new ConflictException("A locked COLD package is required.");
        var items = await db.Set<ColdChecklistItem>().AsNoTracking().Where(i => i.PackageId == package.Id).OrderBy(i => i.Id).ToArrayAsync(ct);
        var ids = items.Select(i => i.Id).ToArray();
        var rows = await db.Set<ColdSubmission>().AsNoTracking().Include(s => s.Files)
            .Where(s => ids.Contains(s.ChecklistItemId) && s.Status == "LOCKED").OrderBy(s => s.Id).ToArrayAsync(ct);
        return new(package, items, rows);
    }

    internal static ColdEvidenceBinding Capture(ColdEvidenceSet data, string scope, long? major, long? student)
    {
        var package = data.Package;
        var items = data.Items.Where(i => Includes(scope, major, student, i)).ToArray();
        var ids = items.Select(i => i.Id).ToHashSet();
        var rows = data.Submissions.Where(s => ids.Contains(s.ChecklistItemId)).ToArray();
        if (rows.Length == 0 || rows.Any(r => r.Files.Count == 0)
            || rows.Select(r => r.ChecklistItemId).Distinct().Count() != rows.Length
            || items.Any(i => i.Required && rows.All(r => r.ChecklistItemId != i.Id)))
            throw new ConflictException("The locked COLD package has no complete evidence for this target.");
        var manifest = JsonSerializer.Serialize(new { package.Id, package.LockedAt, package.RegistrationSnapshotJson,
            Items = items.Select(i => new { i.Id, i.TemplateId, i.TemplateVersion, i.ReportType, i.Scope, i.MajorId, i.StudentId }),
            Versions = rows.Select(r => new { r.Id, r.ChecklistItemId, r.Version, r.LockedAt,
                Files = r.Files.OrderBy(f => f.FileId).Select(f => new { f.FileId, f.StorageKey, f.Sha256, f.SizeBytes, f.ContentType, f.FileName }) }) });
        return new(package.Id, rows.Select(r => r.Id).ToArray(), rows.Sum(r => r.Files.Count),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))));
    }

    internal static async Task<ColdEvidenceBinding> Validate(AipmsDbContext db, EvaluationAssignment assignment, CancellationToken ct)
        => Validate(await Load(db, assignment.ProjectId, ct), assignment);

    internal static ColdEvidenceBinding Validate(ColdEvidenceSet data, EvaluationAssignment assignment)
    {
        if (assignment.Status != "ACTIVE" || assignment.Stage != "COLD" || assignment.ColdPackageId is null)
            throw new ForbiddenException("An active COLD assignment is required.");
        ColdEvidenceBinding? pinned;
        try
        {
            using var json = JsonDocument.Parse(assignment.ScopeSnapshotJson ?? "{}");
            pinned = json.RootElement.TryGetProperty("Cold", out var node) ? node.Deserialize<ColdEvidenceBinding>() : null;
        }
        catch (JsonException) { pinned = null; }
        var current = Capture(data, assignment.Scope, assignment.MajorId, assignment.StudentId);
        if (pinned is null || pinned.SubmissionIds is null || pinned.PackageId != assignment.ColdPackageId || current.PackageId != pinned.PackageId
            || current.Hash != pinned.Hash || !current.SubmissionIds.SequenceEqual(pinned.SubmissionIds))
            throw new ConflictException("COLD evidence differs from the assignment's locked version.");
        return current;
    }
}
