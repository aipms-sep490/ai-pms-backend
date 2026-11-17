using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Services.AccountSecurity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIPMS.IntegrationTests.AccountSecurity;

public sealed class UserAvatarSqlTests
{
    [Fact]
    public async Task Fresh_schema_and_replayed_migration_preserve_private_avatar_metadata()
    {
        await using var database = new IsolatedSqlDatabase();
        await database.StartAsync(Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION"));
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        await using (var insert = new SqlCommand("""
            INSERT dbo.users(email, password_hash, full_name) VALUES(N'avatar-e2e@example.test', N'unused', N'Avatar test');
            DECLARE @user_id bigint = SCOPE_IDENTITY();
            INSERT dbo.user_avatars(user_id, storage_key, content_type, size_bytes, sha256, concurrency_token, updated_at)
            VALUES(@user_id, 'private-object-key', 'image/png', 123, REPLICATE('a', 64), NEWID(), SYSUTCDATETIME());
            """, connection))
            await insert.ExecuteNonQueryAsync();

        var migration = await File.ReadAllTextAsync(Path.Combine(SqlSchemaBootstrap.Root, "db", "changes", "20261117_user_avatars.sql"));
        await SqlSchemaBootstrap.ExecuteAsync(database.ConnectionString, migration);
        await SqlSchemaBootstrap.ExecuteAsync(database.ConnectionString, migration);

        await using var count = new SqlCommand("SELECT COUNT_BIG(*) FROM dbo.user_avatars WHERE storage_key = 'private-object-key'", connection);
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);

        await using var invalid = new SqlCommand("""
            INSERT dbo.user_avatars(user_id, storage_key, content_type, size_bytes, sha256, concurrency_token, updated_at)
            VALUES(99999999, 'invalid-key', 'image/png', 123, REPLICATE('b', 64), NEWID(), SYSUTCDATETIME());
            """, connection);
        await Assert.ThrowsAsync<SqlException>(() => invalid.ExecuteNonQueryAsync());

        await using (var clear = new SqlCommand("DELETE dbo.user_avatars WHERE storage_key = 'private-object-key'", connection))
            await clear.ExecuteNonQueryAsync();
        var userId = (long)(await new SqlCommand("SELECT id FROM dbo.users WHERE email = N'avatar-e2e@example.test'", connection).ExecuteScalarAsync())!;
        await using var context = new AipmsDbContext(new DbContextOptionsBuilder<AipmsDbContext>()
            .UseSqlServer(database.ConnectionString).Options);
        var storage = new MemoryStorage();
        var service = new UserAvatarService(context, new CurrentUser(userId), storage,
            new NoopAudit(), TimeProvider.System, NullLogger<UserAvatarService>.Instance);
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");
        var saved = await service.Upload(new UploadContent("portrait.png", "image/png", bytes.Length, new MemoryStream(bytes)), null, CancellationToken.None);
        Assert.True(saved.HasAvatar);
        Assert.Equal(saved.ConcurrencyToken, (await service.GetMetadata(CancellationToken.None)).ConcurrencyToken);
        await using (var downloaded = (await service.Download(CancellationToken.None)).Content)
        {
            using var copy = new MemoryStream();
            await downloaded.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }
        await service.Remove(saved.ConcurrencyToken, CancellationToken.None);
        Assert.False((await service.GetMetadata(CancellationToken.None)).HasAvatar);
        Assert.Empty(storage.Files);
    }

    private sealed class CurrentUser(long userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => userId;
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
