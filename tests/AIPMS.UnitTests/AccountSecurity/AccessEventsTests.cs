using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.AccountSecurity.Models;
using AIPMS.Application.Features.AccountSecurity.Queries;
using AIPMS.Application.Features.AccountSecurity.Services;
using AIPMS.Application.Features.AccountSecurity.Validators;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.UnitTests.AccountSecurity;

public sealed class AccessEventsTests
{
    [Fact]
    public async Task Repository_Filters_AuthenticationEvents_Before_Paging()
    {
        await using var db = new AipmsDbContext(new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Users.Add(new User { Id = 7, Email = "member@example.test", FullName = "Member", PasswordHash = "hash", Status = "ACTIVE" });
        var day = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);
        db.AuditLogs.AddRange(
            Entry(1, 7, "AUTH_LOGIN", "SUCCESS", day.AddHours(1)),
            Entry(2, 7, "AUTH_LOGOUT", "SUCCESS", day.AddHours(2)),
            Entry(3, 7, "AUTH_GOOGLE_LOGIN", "SUCCESS", day.AddHours(3)),
            Entry(4, 7, "AUTH_PASSWORD_CHANGED", "SUCCESS", day.AddHours(4)),
            Entry(5, null, "AUTH_LOGIN", "FAILURE", day.AddHours(5)));
        await db.SaveChangesAsync();

        var repository = new AccountSecurityRepository(db);
        var first = await repository.GetAccessEventsAsync("member", null, "LOGIN", "SUCCESS", day, day.AddDays(1), 1, 1);
        var second = await repository.GetAccessEventsAsync("member", null, "LOGIN", "SUCCESS", day, day.AddDays(1), 2, 1);
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(3, Assert.Single(first.Items).Id);
        Assert.Equal("GOOGLE", first.Items[0].AuthenticationMethod);
        Assert.Equal("member@example.test", first.Items[0].Email);
        Assert.Equal(1, Assert.Single(second.Items).Id);

        var failed = await repository.GetAccessEventsAsync(null, null, null, "FAILURE", null, null, 1, 20);
        Assert.Null(Assert.Single(failed.Items).UserId);
        Assert.Null(failed.Items[0].Email);
    }

    [Fact]
    public async Task Query_Denies_NonAdmin_Before_Reading_Audit()
    {
        var repository = new RecordingRepository();
        var handler = new GetAccessEventsQueryHandler(repository,
            new AccountSecurityAccessService(new CurrentUser(["DEPARTMENT_STAFF"])));

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new GetAccessEventsQuery(null, null, null, null, null, null), CancellationToken.None));
        Assert.False(repository.WasRead);
    }

    [Fact]
    public void Validator_Rejects_Unknown_Event_And_Reversed_Dates()
    {
        var validator = new GetAccessEventsQueryValidator();
        var result = validator.Validate(new GetAccessEventsQuery(null, null, "LOCATION", null,
            new DateTime(2026, 11, 14), new DateTime(2026, 11, 13)));
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }

    private static AuditLog Entry(long id, long? userId, string action, string outcome, DateTime when) =>
        new() { Id = id, ActorUserId = userId, Action = action, EntityType = "USER",
            Outcome = outcome, OccurredAt = when, IpAddress = "192.0.2.1" };

    private sealed class CurrentUser(IReadOnlyCollection<string> roles) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => 1;
        public string? Email => null;
        public string? FullName => null;
        public IReadOnlyCollection<string> Roles => roles;
    }

    private sealed class RecordingRepository : IAuditLogRepository
    {
        public bool WasRead { get; private set; }
        public Task<PagedResult<AccessEvent>> GetAccessEventsAsync(string? search, long? userId, string? eventType,
            string? outcome, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize,
            CancellationToken cancellationToken = default)
        {
            WasRead = true;
            return Task.FromResult(new PagedResult<AccessEvent>([], page, pageSize, 0));
        }

        public Task<PagedResult<AuditRecord>> GetAuditLogsAsync(long? actorUserId, string? action,
            string? entityType, string? outcome, DateTime? fromUtc, DateTime? toUtc, int page,
            int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<AuditRecord>([], page, pageSize, 0));
    }
}
