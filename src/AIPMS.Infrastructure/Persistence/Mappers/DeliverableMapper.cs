using System.Linq.Expressions;
using AIPMS.Application.Features.Deliverables.DTOs;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class DeliverableMapper
{
    public static readonly Expression<Func<M.Deliverable, DeliverableDto>> Projection = d =>
        new(d.Id, d.ProjectId, d.MilestoneId, d.Title, d.Description, d.DeliverableType, d.DueAt,
            d.Status, d.CreatedBy, d.DeliverableVersions.Select(v => (int?)v.VersionNumber).Max() ?? 0);

    public static readonly Expression<Func<M.File, ProjectFileDto>> FileProjection = f =>
        new(f.Id, f.DeliverableVersionId != null ? "VERSION" : f.ProgressReportId != null ? "REPORT" : f.MeetingId != null ? "MEETING" : "FEEDBACK",
            f.DeliverableVersionId ?? f.ProgressReportId ?? f.MeetingId ?? f.SupervisorFeedbackId ?? 0,
            f.OriginalFileName, f.MimeType ?? "application/octet-stream", f.FileSizeBytes,
            f.ChecksumSha256 ?? "", f.UploadedBy, f.CreatedAt);

    public static ProjectFileDto ToDto(this M.File f) => new(f.Id,
        f.DeliverableVersionId.HasValue ? "VERSION" : f.ProgressReportId.HasValue ? "REPORT" : f.MeetingId.HasValue ? "MEETING" : "FEEDBACK",
        f.DeliverableVersionId ?? f.ProgressReportId ?? f.MeetingId ?? f.SupervisorFeedbackId ?? 0,
        f.OriginalFileName, f.MimeType ?? "application/octet-stream", f.FileSizeBytes,
        f.ChecksumSha256 ?? "", f.UploadedBy, f.CreatedAt);

    public static DeliverableVersionDto ToDto(this M.DeliverableVersion v) =>
        new(v.Id, v.DeliverableId, v.VersionNumber, v.SubmittedBy, v.SubmissionNote, v.Status,
            v.SubmittedAt, v.Files.OrderBy(f => f.Id).Select(f => f.ToDto()).ToArray());

    public static readonly Expression<Func<M.SupervisorFeedback, DeliverableFeedbackDto>> FeedbackProjection = f =>
        new(f.Id, f.ProjectId, f.DeliverableVersionId!.Value, f.SupervisorAssignmentId,
            f.SupervisorAssignment.SupervisorProfile.UserId, f.FeedbackText, f.CreatedAt);
}
