using AIPMS.Application.Features.Deliverables.DTOs;

namespace AIPMS.Application.Features.Deliverables.Models;

public sealed record DeliverableProject(long Id, long SemesterId, string Status, bool IsMember, bool IsLeader,
    long? AssignmentId);
public sealed record FileParent(string Type, long Id, long ProjectId, string Status, long OwnerId);
public sealed record StoredProjectFile(ProjectFileDto Dto, string StorageKey, long ProjectId, int ParentCount);
public sealed record UploadContent(string FileName, string ContentType, long Length, Stream Content);
public sealed record ValidatedUpload(string FileName, string ContentType, byte[] Bytes, string Sha256);
public sealed record DeliverableSearch(long ProjectId, string? Status, string? Search, int Page, int PageSize,
    long? MilestoneId = null, string? DeliverableType = null);
public sealed record FileSearch(long ProjectId, string? Search, int Page, int PageSize,
    string? ContentType = null, long? UploadedBy = null, DateTime? From = null, DateTime? To = null,
    string? ParentType = null, long? ParentId = null);
