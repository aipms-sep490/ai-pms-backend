using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Services.AccountSecurity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.UnitTests.AccountSecurity;

public sealed class UserAvatarServiceTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Upload_Readback_And_Remove_Use_Private_Storage_And_Revision()
    {
        await using var db = Database();
        db.Users.Add(new User { Id = 1, Email = "student@example.test", FullName = "Student", PasswordHash = "hash", Status = "ACTIVE" });
        await db.SaveChangesAsync();
        var storage = new MemoryStorage();
        var service = Service(db, storage);

        Assert.False((await service.GetMetadata(CancellationToken.None)).HasAvatar);
        var first = await service.Upload(PngUpload(), null, CancellationToken.None);
        Assert.True(first.HasAvatar);
        Assert.Equal("image/png", first.ContentType);
        Assert.Single(storage.Files);
        var image = await service.Download(CancellationToken.None);
        await using (image.Content)
        {
            using var copy = new MemoryStream();
            await image.Content.CopyToAsync(copy);
            Assert.Equal(Png, copy.ToArray());
        }
        await Assert.ThrowsAsync<ConflictException>(() => service.Upload(PngUpload(), null, CancellationToken.None));
        Assert.Single(storage.Files);

        var next = await service.Upload(PngUpload(), first.ConcurrencyToken, CancellationToken.None);
        Assert.NotEqual(first.ConcurrencyToken, next.ConcurrencyToken);
        Assert.Single(storage.Files);
        await Assert.ThrowsAsync<ConflictException>(() => service.Remove(first.ConcurrencyToken, CancellationToken.None));
        Assert.True((await service.Remove(next.ConcurrencyToken, CancellationToken.None)).HasAvatar is false);
        Assert.Empty(storage.Files);
        await Assert.ThrowsAsync<NotFoundException>(() => service.Download(CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_NonImage_Oversize_And_Inactive_Account()
    {
        await using var db = Database();
        db.Users.Add(new User { Id = 1, Email = "student@example.test", FullName = "Student", PasswordHash = "hash", Status = "ACTIVE" });
        await db.SaveChangesAsync();
        var storage = new MemoryStorage();
        var service = Service(db, storage);
        await Assert.ThrowsAsync<ValidationException>(() => service.Upload(
            new UploadContent("notes.txt", "text/plain", 3, new MemoryStream("abc"u8.ToArray())), null, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.Upload(
            new UploadContent("broken.png", "image/png", 8, new MemoryStream(Png.AsSpan(0, 8).ToArray())), null, CancellationToken.None));
        await Assert.ThrowsAsync<PayloadTooLargeException>(() => service.Upload(
            new UploadContent("photo.png", "image/png", 2 * 1024 * 1024 + 1, new MemoryStream(Png)), null, CancellationToken.None));
        Assert.Empty(storage.Files);
        db.Users.Single().Status = "INACTIVE";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => service.Upload(PngUpload(), null, CancellationToken.None));
    }

    private static UploadContent PngUpload() => new("avatar.png", "image/png", Png.Length, new MemoryStream(Png));
    private static AipmsDbContext Database() => new(new DbContextOptionsBuilder<AipmsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static UserAvatarService Service(AipmsDbContext db, MemoryStorage storage) =>
        new(db, new CurrentUser(), storage, new NoopAudit(), TimeProvider.System, NullLogger<UserAvatarService>.Instance);

    private sealed class CurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => 1;
        public string? Email => null;
        public string? FullName => null;
        public IReadOnlyCollection<string> Roles => [];
    }

    private sealed class NoopAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MemoryStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public async Task WriteAsync(string key, Stream content, CancellationToken ct)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            Files.Add(key, copy.ToArray());
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream(Files[key]));
        public Task DeleteAsync(string key, CancellationToken ct) { Files.Remove(key); return Task.CompletedTask; }
    }
}
