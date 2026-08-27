using System.Linq.Expressions;
using AIPMS.Application.Features.Deliverables.DTOs;
using Db = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Mappings;

internal static class DeliverableMappings
{
    internal static readonly Expression<Func<Db.Deliverable, DeliverableDto>> ToDto = x =>
        new(x.Id,x.ProjectId,x.MilestoneId,x.Title,x.Description,x.DeliverableType,x.DueAt,x.Status,x.CreatedBy,x.CreatedAt);

    internal static readonly Expression<Func<Db.DeliverableVersion, DeliverableVersionDto>> VersionToDto = x =>
        new(x.Id,x.DeliverableId,x.VersionNumber,x.SubmittedBy,x.SubmissionNote,x.Status,x.SubmittedAt,
            x.Files.Select(f=>new FileMetadataDto(f.Id,f.OriginalFileName,f.MimeType,f.FileSizeBytes,f.ChecksumSha256,f.CreatedAt,f.UploadedBy)).ToList(),
            x.SupervisorFeedbacks.Select(f=>new SupervisorFeedbackDto(f.Id,f.SupervisorAssignmentId,f.FeedbackText,f.CreatedAt)).ToList());
}
