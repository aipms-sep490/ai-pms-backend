using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.AccountSecurity.Commands;
using AIPMS.Application.Features.AccountSecurity.Models;
using AIPMS.Application.Features.AccountSecurity.Services;
using AIPMS.Application.Features.AccountSecurity.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class AccountSecurityHandlerTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SetStatus_LastActiveAdministrator_ThrowsConflict()
    {
        var repository = new StubUserAccountRepository
        {
            User = CreateAdmin(),
            ActiveAdminCount = 1
        };
        var handler = new SetUserStatusCommandHandler(
            repository,
            new AccountSecurityAccessService(new TestCurrentUser(1, AppRoles.Admin)),
            new RecordingAuditTrail(),
            new FixedTimeProvider(FixedNow));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new SetUserStatusCommand(1, "INACTIVE"),
            CancellationToken.None));

        Assert.Null(repository.StatusSet);
    }

    [Fact]
    public async Task SetStatus_WhenAnotherActiveAdministratorExists_DeactivatesAndAudits()
    {
        var repository = new StubUserAccountRepository
        {
            User = CreateAdmin(),
            ActiveAdminCount = 2
        };
        var audit = new RecordingAuditTrail();
        var handler = new SetUserStatusCommandHandler(
            repository,
            new AccountSecurityAccessService(new TestCurrentUser(1, AppRoles.Admin)),
            audit,
            new FixedTimeProvider(FixedNow));

        var result = await handler.Handle(
            new SetUserStatusCommand(1, "inactive"),
            CancellationToken.None);

        Assert.Equal("INACTIVE", result.Status);
        Assert.Equal("INACTIVE", repository.StatusSet);
        Assert.Contains(audit.Entries, static entry => entry.Action == "ACCOUNT_STATUS_CHANGED");
    }

    [Fact]
    public void CreateUserValidator_WeakPassword_IsRejected()
    {
        var validator = new CreateUserAccountCommandValidator();
        var result = validator.Validate(new CreateUserAccountCommand(
            null,
            null,
            "new.user@aipms.test",
            "weak",
            "New User",
            null,
            null,
            null,
            null,
            [1]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, static error => error.PropertyName == "Password");
    }

    private static AccountUser CreateAdmin(string status = "ACTIVE") =>
        new(
            1,
            null,
            null,
            "admin@aipms.test",
            "System Administrator",
            null,
            null,
            "ADMIN-001",
            "Administrator",
            status,
            0,
            null,
            null,
            null,
            FixedNow.UtcDateTime,
            FixedNow.UtcDateTime,
            [AppRoles.Admin]);

    private sealed class StubUserAccountRepository : IUserAccountRepository
    {
        public required AccountUser User { get; init; }
        public int ActiveAdminCount { get; init; }
        public string? StatusSet { get; private set; }

        public Task<AccountUser?> GetUserAsync(long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountUser?>(userId == User.Id ? User : null);

        public Task<int> CountActiveUsersInRoleAsync(string roleCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActiveAdminCount);

        public Task<AccountUser> SetStatusAsync(long userId, string status, DateTime utcNow, CancellationToken cancellationToken = default)
        {
            StatusSet = status;
            return Task.FromResult(User with { Status = status, UpdatedAt = utcNow });
        }

        public Task<PagedResult<AccountUser>> GetUsersAsync(string? search, string? status, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IdentityExistsAsync(string email, string? studentCode, string? employeeCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountUser> CreateUserAsync(CreateAccountData data, long actorUserId, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountUser>> CreateUsersAsync(IReadOnlyCollection<CreateAccountData> accounts, long actorUserId, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountUser> UpdateProfileAsync(long userId, UpdateProfileData data, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UserHasRoleAsync(long userId, string roleCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AssignRoleAsync(long userId, long roleId, long actorUserId, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveRoleAsync(long userId, long roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
