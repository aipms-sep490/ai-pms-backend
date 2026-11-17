using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;

namespace AIPMS.Application.Features.AccountSecurity.Abstractions;

public interface IUserAvatarService
{
    Task<UserAvatarDto> GetMetadata(CancellationToken ct);
    Task<FileDownload> Download(CancellationToken ct);
    Task<UserAvatarDto> Upload(UploadContent file, string? expectedToken, CancellationToken ct);
    Task<UserAvatarDto> Remove(string? expectedToken, CancellationToken ct);
}
