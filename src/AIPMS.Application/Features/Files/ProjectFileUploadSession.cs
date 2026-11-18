using AIPMS.Application.Features.Deliverables.DTOs;

namespace AIPMS.Application.Features.Files;

public sealed record CreateProjectFileUploadSession(string ParentType, long ParentId, string FileName,
    string ContentType, long SizeBytes);

public sealed record ProjectFileUploadSession(string SessionId, string ParentType, long ParentId,
    string FileName, long TotalBytes, long NextOffset, int ChunkSizeBytes, DateTime ExpiresAt,
    string State, ProjectFileDto? File, string? Error);
