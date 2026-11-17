using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Application.Features.Files;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIPMS.Infrastructure.Services.AccountSecurity;

internal sealed class UserAvatarService(AipmsDbContext db, ICurrentUser current, IFileStorage storage,
    IAuditTrail audit, TimeProvider clock, ILogger<UserAvatarService> logger) : IUserAvatarService
{
    private long Actor => current.IsAuthenticated && current.UserId is long id ? id : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task RequireActive(CancellationToken ct)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == Actor && x.Status == "ACTIVE", ct))
            throw new ForbiddenException("An active account is required to manage the profile image.");
    }

    private static UserAvatarDto ToDto(UserAvatar? row) => row is null
        ? new(false, null, null, null, null)
        : new(true, row.ConcurrencyToken.ToString("N"), row.ContentType, row.SizeBytes,
            DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc));

    private async Task<UserAvatar?> Read(CancellationToken ct) =>
        await db.Set<UserAvatar>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == Actor, ct);

    public async Task<UserAvatarDto> GetMetadata(CancellationToken ct)
    {
        await RequireActive(ct);
        return ToDto(await Read(ct));
    }

    public async Task<FileDownload> Download(CancellationToken ct)
    {
        await RequireActive(ct);
        var row = await Read(ct) ?? throw new NotFoundException("UserAvatar", Actor);
        return new(await storage.OpenReadAsync(row.StorageKey, ct), row.ContentType,
            row.ContentType == "image/png" ? "avatar.png" : "avatar.jpg");
    }

    private static void CheckToken(UserAvatar? row, string? expected)
    {
        var actual = row?.ConcurrencyToken.ToString("N");
        if (!string.Equals(actual, string.IsNullOrWhiteSpace(expected) ? null : expected.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Profile image changed. Reload before trying again.", "AVATAR_VERSION_CONFLICT");
    }

    public async Task<UserAvatarDto> Upload(UploadContent file, string? expectedToken, CancellationToken ct)
    {
        await RequireActive(ct);
        if (file.Length > UploadPolicies.UserAvatarLimit)
            throw new PayloadTooLargeException(UploadPolicies.UserAvatarLimit, "Profile image exceeds 2 MiB.");
        if (Path.GetExtension(file.FileName).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg")
            || file.ContentType is not ("image/png" or "image/jpeg"))
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["Use a PNG or JPEG image."] });
        var validated = await UploadValidator.ReadAsync(file, ct);
        if (!HasValidImageEnvelope(validated.ContentType, validated.Bytes))
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["The image is incomplete or has invalid dimensions."] });
        var key = Guid.NewGuid().ToString("N");
        await using (var content = new MemoryStream(validated.Bytes, writable: false))
            await storage.WriteAsync(key, content, ct);

        var commitStarted = false;
        string? oldKey = null;
        try
        {
            await using var tx = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            await RequireActive(ct);
            var row = await db.Set<UserAvatar>().SingleOrDefaultAsync(x => x.UserId == Actor, ct);
            CheckToken(row, expectedToken);
            oldKey = row?.StorageKey;
            row ??= new UserAvatar { UserId = Actor };
            if (db.Entry(row).State == EntityState.Detached) db.Set<UserAvatar>().Add(row);
            row.StorageKey = key;
            row.ContentType = validated.ContentType;
            row.SizeBytes = validated.Bytes.LongLength;
            row.Sha256 = validated.Sha256;
            row.ConcurrencyToken = Guid.NewGuid();
            row.UpdatedAt = Now;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                throw new ConflictException("Profile image changed. Reload before trying again.", "AVATAR_VERSION_CONFLICT");
            }
            await audit.RecordAsync(new(Actor, "USER_AVATAR_UPDATED", "USER_AVATAR", Actor,
                new Dictionary<string, object?> { ["sha256"] = row.Sha256, ["sizeBytes"] = row.SizeBytes }), ct);
            commitStarted = true;
            if (tx is not null) await tx.CommitAsync(ct);
            if (oldKey is not null) await CleanupOld(oldKey);
            return ToDto(row);
        }
        catch
        {
            // A failed commit may have succeeded server-side: retain its object for reconciliation.
            if (!commitStarted) await CleanupNew(key);
            throw;
        }
    }

    private static bool HasValidImageEnvelope(string mime, byte[] bytes)
    {
        if (mime == "image/png")
        {
            if (bytes.Length < 45 || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8, 4)) != 13
                || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)
                || !bytes.AsSpan(bytes.Length - 8, 4).SequenceEqual("IEND"u8)) return false;
            var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
            return width is > 0 and <= 4096 && height is > 0 and <= 4096;
        }
        return bytes.Length > 100 && bytes[^2] == 0xFF && bytes[^1] == 0xD9;
    }

    public async Task<UserAvatarDto> Remove(string? expectedToken, CancellationToken ct)
    {
        await RequireActive(ct);
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        await RequireActive(ct);
        var row = await db.Set<UserAvatar>().SingleOrDefaultAsync(x => x.UserId == Actor, ct);
        CheckToken(row, expectedToken);
        if (row is null) return ToDto(null);
        db.Set<UserAvatar>().Remove(row);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "USER_AVATAR_REMOVED", "USER_AVATAR", Actor,
            new Dictionary<string, object?> { ["sha256"] = row.Sha256 }), ct);
        if (tx is not null) await tx.CommitAsync(ct);
        await CleanupOld(row.StorageKey);
        return ToDto(null);
    }

    private async Task CleanupOld(string key)
    {
        try { await storage.DeleteAsync(key, CancellationToken.None); }
        catch (Exception ex) { logger.LogError(ex, "Old profile image cleanup failed; storage reconciliation required"); }
    }

    private async Task CleanupNew(string key)
    {
        try { await storage.DeleteAsync(key, CancellationToken.None); }
        catch (Exception ex) { logger.LogError(ex, "Rejected profile image cleanup failed; storage reconciliation required"); }
    }
}
