using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

/// <summary>
/// TK-W2-03: Automatically appends deduplicated read-only entries to dbo.project_evidence
/// matching ck_evidence_source and uq_evidence_source when deliverables, progress reports,
/// locked meeting notes, or completed tasks with attached files occur.
/// </summary>
internal static class ProjectEvidenceAutoRecorder
{
    public static async Task TryRecordAsync(
        AipmsDbContext db,
        long projectId,
        long? majorId,
        string sourceType,
        long sourceId,
        string? notes,
        long submittedBy,
        DateTime submittedAtUtc,
        CancellationToken ct)
    {
        if (projectId <= 0 || sourceId <= 0 || submittedBy <= 0) return;

        var normalizedType = sourceType.Trim().ToUpperInvariant();
        if (normalizedType is not ("TASK" or "DELIVERABLE" or "MEETING" or "PROGRESS_REPORT" or "FILE"))
            return;

        var exists = await db.ProjectEvidence.AsNoTracking().AnyAsync(
            e => e.ProjectId == projectId
                && e.SourceType == normalizedType
                && e.SourceId == sourceId
                && e.MajorId == majorId,
            ct);
        if (exists) return;

        var trimmedNotes = string.IsNullOrWhiteSpace(notes)
            ? null
            : notes.Trim().Length > 2000 ? notes.Trim()[..2000] : notes.Trim();

        db.ProjectEvidence.Add(new M.ProjectEvidence
        {
            ProjectId = projectId,
            MajorId = majorId,
            SourceType = normalizedType,
            SourceId = sourceId,
            TaskId = normalizedType == "TASK" ? sourceId : null,
            DeliverableId = normalizedType == "DELIVERABLE" ? sourceId : null,
            MeetingId = normalizedType == "MEETING" ? sourceId : null,
            ProgressReportId = normalizedType == "PROGRESS_REPORT" ? sourceId : null,
            FileId = normalizedType == "FILE" ? sourceId : null,
            VerificationStatus = "PENDING",
            Notes = trimmedNotes,
            SubmittedBy = submittedBy,
            SubmittedAt = submittedAtUtc
        });
        await db.SaveChangesAsync(ct);
    }
}
