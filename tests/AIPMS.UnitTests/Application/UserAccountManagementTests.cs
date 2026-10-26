using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.AccountSecurity.Commands;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.AccountSecurity.Models;
using AIPMS.Application.Features.AccountSecurity.Services;
using AIPMS.Application.Features.AccountSecurity.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class UserAccountManagementTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdateUserAccount_AllFields_UpdatesAccountAndRecordsAudit()
    {
        var existing = CreateUser(10, "old@aipms.test", "Old Name", "0123456789", "STU-001", null, "Old Title", "token-v1");
        var repository = new InMemoryUserAccountRepository(existing);
        var audit = new RecordingAuditTrail();
        var handler = new UpdateUserAccountCommandHandler(
            repository,
            new AccountSecurityAccessService(new TestCurrentUser(1, AppRoles.Admin)),
            audit,
            new FixedTimeProvider(FixedNow));

        var command = new UpdateUserAccountCommand(
            10,
            "New Full Name",
            "0987654321",
            "Senior Lecturer",
            "STU-002",
            "EMP-001",
            "token-v1");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal("New Full Name", result.FullName);
        Assert.Equal("0987654321", result.Phone);
        Assert.Equal("Senior Lecturer", result.Title);
        Assert.Equal("STU-002", result.StudentCode);
        Assert.Equal("EMP-001", result.EmployeeCode);

        var auditEntry = Assert.Single(audit.Entries);
        Assert.Equal("ACCOUNT_UPDATED", auditEntry.Action);
        Assert.Equal("USER", auditEntry.EntityType);
        Assert.Equal(10, auditEntry.EntityId);

        var prev = Assert.IsAssignableFrom<IDictionary<string, object?>>(auditEntry.Context?["previous"]);
        var curr = Assert.IsAssignableFrom<IDictionary<string, object?>>(auditEntry.Context?["current"]);

        Assert.Equal("Old Name", prev["fullName"]);
        Assert.Equal("New Full Name", curr["fullName"]);
        Assert.Equal("STU-001", prev["studentCode"]);
        Assert.Equal("STU-002", curr["studentCode"]);
    }

    [Fact]
    public async Task UpdateUserAccount_PartialUpdate_PreservesUnspecifiedFields()
    {
        var existing = CreateUser(10, "student@aipms.test", "Student Name", "0123456789", "SE12345", null, "Scholar", "token-v1");
        var repository = new InMemoryUserAccountRepository(existing);
        var audit = new RecordingAuditTrail();
        var handler = new UpdateUserAccountCommandHandler(
            repository,
            new AccountSecurityAccessService(new TestCurrentUser(1, AppRoles.Admin)),
            audit,
            new FixedTimeProvider(FixedNow));

        // Only update FullName and Phone, leaving studentCode, employeeCode, and title null
        var command = new UpdateUserAccountCommand(
            10,
            "Updated Student Name",
            "0999888777",
            null,
            null,
            null,
            "token-v1");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal("Updated Student Name", result.FullName);
        Assert.Equal("0999888777", result.Phone);
        Assert.Equal("Scholar", result.Title); // Preserved
        Assert.Equal("SE12345", result.StudentCode); // Preserved
        Assert.Null(result.EmployeeCode);
    }

    [Fact]
    public async Task UpdateUserAccount_StaleConcurrencyToken_ThrowsConflict()
    {
        var existing = CreateUser(10, "test@aipms.test", "Test User", null, null, null, null, "token-v2");
        var repository = new InMemoryUserAccountRepository(existing);
        var handler = new UpdateUserAccountCommandHandler(
            repository,
            new AccountSecurityAccessService(new TestCurrentUser(1, AppRoles.Admin)),
            new RecordingAuditTrail(),
            new FixedTimeProvider(FixedNow));

        var command = new UpdateUserAccountCommand(
            10,
            "Concurrent Edit",
            null,
            null,
            null,
            null,
            "token-v1"); // Stale token

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUserAccount_DuplicateStudentCodeOnAnotherUser_ThrowsConflict()
    {
        var user1 = CreateUser(10, "user1@aipms.test", "User One", null, "SE10001", null, null, "token-1");
        var user2 = CreateUser(11, "user2@aipms.test", "User Two", null, "SE10002", null, null, "token-2");
        var repository = new InMemoryUserAccountRepository(user1, user2);
        var handler = new UpdateUserAccountCommandHandler(
            repository,
            new AccountSecurityAccessService(new TestCurrentUser(1, AppRoles.Admin)),
            new RecordingAuditTrail(),
            new FixedTimeProvider(FixedNow));

        // Attempt to change user 10's studentCode to SE10002 (which belongs to user 11)
        var command = new UpdateUserAccountCommand(
            10,
            null,
            null,
            null,
            "SE10002",
            null,
            "token-1");

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUserAccount_NonAdminActor_ThrowsForbidden()
    {
        var existing = CreateUser(10, "user@aipms.test", "User", null, null, null, null, "token-1");
        var repository = new InMemoryUserAccountRepository(existing);
        var handler = new UpdateUserAccountCommandHandler(
            repository,
            new AccountSecurityAccessService(new TestCurrentUser(10, AppRoles.Student)),
            new RecordingAuditTrail(),
            new FixedTimeProvider(FixedNow));

        var command = new UpdateUserAccountCommand(10, "Hacker", null, null, null, null, "token-1");

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUserAccount_UserNotFound_ThrowsNotFound()
    {
        var repository = new InMemoryUserAccountRepository();
        var handler = new UpdateUserAccountCommandHandler(
            repository,
            new AccountSecurityAccessService(new TestCurrentUser(1, AppRoles.Admin)),
            new RecordingAuditTrail(),
            new FixedTimeProvider(FixedNow));

        var command = new UpdateUserAccountCommand(999, "Ghost", null, null, null, null, "token-1");

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task SetStatus_LastActiveAdmin_ThrowsConflict()
    {
        var admin = CreateUser(1, "admin@aipms.test", "Admin User", null, null, "EMP-001", "Administrator", "token-admin", [AppRoles.Admin]);
        var repository = new InMemoryUserAccountRepository(admin) { ActiveAdminCount = 1 };
        var handler = new SetUserStatusCommandHandler(
            repository,
            new AccountSecurityAccessService(new TestCurrentUser(1, AppRoles.Admin)),
            new RecordingAuditTrail(),
            new FixedTimeProvider(FixedNow));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new SetUserStatusCommand(1, "INACTIVE"), CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new SetUserStatusCommand(1, "SUSPENDED"), CancellationToken.None));
    }

    [Fact]
    public async Task RemoveRole_LastActiveAdmin_ThrowsConflict()
    {
        var admin = CreateUser(1, "admin@aipms.test", "Admin User", null, null, "EMP-001", "Administrator", "token-admin", [AppRoles.Admin]);
        var repository = new InMemoryUserAccountRepository(admin) { ActiveAdminCount = 1 };
        var roleRepo = new StubRoleRepository(new SecurityRole(99, AppRoles.Admin, "System Administrator", null, true, FixedNow.UtcDateTime, FixedNow.UtcDateTime, []));
        var handler = new RemoveUserRoleCommandHandler(
            repository,
            roleRepo,
            new AccountSecurityAccessService(new TestCurrentUser(1, AppRoles.Admin)),
            new RecordingAuditTrail());

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new RemoveUserRoleCommand(1, 99), CancellationToken.None));
    }

    [Fact]
    public void Validator_RejectsInvalidUserIdAndExcessiveLengths()
    {
        var validator = new UpdateUserAccountCommandValidator();

        var invalidId = validator.Validate(new UpdateUserAccountCommand(0, "Name", null, null, null, null, null));
        Assert.False(invalidId.IsValid);
        Assert.Contains(invalidId.Errors, static e => e.PropertyName == "UserId");

        var emptyName = validator.Validate(new UpdateUserAccountCommand(1, "   ", null, null, null, null, null));
        Assert.False(emptyName.IsValid);
        Assert.Contains(emptyName.Errors, static e => e.PropertyName == "FullName");

        var longPhone = validator.Validate(new UpdateUserAccountCommand(1, "Name", new string('1', 35), null, null, null, null));
        Assert.False(longPhone.IsValid);
        Assert.Contains(longPhone.Errors, static e => e.PropertyName == "Phone");
    }

    [Fact]
    public async Task UpdateUserAccount_WhenAuditFails_RollsBackChanges_AndThrows()
    {
        var existing = CreateUser(10, "old@aipms.test", "Old Name", "0123456789", "STU-001", null, "Old Title", "token-v1");
        var repository = new InMemoryUserAccountRepository(existing);
        var failingAudit = new FailingAuditTrail();
        var handler = new UpdateUserAccountCommandHandler(
            repository,
            new AccountSecurityAccessService(new TestCurrentUser(1, AppRoles.Admin)),
            failingAudit,
            new FixedTimeProvider(FixedNow));

        var command = new UpdateUserAccountCommand(
            10,
            "New Name Should Not Persist",
            "0987654321",
            "Lecturer",
            "STU-002",
            null,
            "token-v1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(command, CancellationToken.None));

        // Changes must not be persisted in repository (atomic rollback)
        var persisted = await repository.GetUserAsync(10);
        Assert.NotNull(persisted);
        Assert.Equal("Old Name", persisted.FullName);
        Assert.Equal("0123456789", persisted.Phone);
        Assert.Equal("STU-001", persisted.StudentCode);
    }

    [Fact]
    public async Task UpdateUserAccount_ConcurrentUpdates_OnlyOneSucceedsAndSecondThrowsConflict()
    {
        var existing = CreateUser(10, "concurrent@aipms.test", "Initial Name", "0123456789", "STU-001", null, "Title", "token-v1");
        var repository = new InMemoryUserAccountRepository(existing);
        var audit = new RecordingAuditTrail();
        var handler = new UpdateUserAccountCommandHandler(
            repository,
            new AccountSecurityAccessService(new TestCurrentUser(1, AppRoles.Admin)),
            audit,
            new FixedTimeProvider(FixedNow));

        var command1 = new UpdateUserAccountCommand(10, "Winner Name", "0123456789", "Title", "STU-001", null, "token-v1");
        var command2 = new UpdateUserAccountCommand(10, "Loser Name", "0123456789", "Title", "STU-001", null, "token-v1");

        var firstResult = await handler.Handle(command1, CancellationToken.None);
        Assert.Equal("Winner Name", firstResult.FullName);

        // Second command with stale token-v1 must throw ConflictException
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command2, CancellationToken.None));
    }

    private static AccountUser CreateUser(
        long id,
        string email,
        string fullName,
        string? phone,
        string? studentCode,
        string? employeeCode,
        string? title,
        string concurrencyToken,
        IReadOnlyCollection<string>? roles = null) =>
        new(
            id,
            null,
            null,
            email,
            fullName,
            phone,
            studentCode,
            employeeCode,
            title,
            "ACTIVE",
            0,
            null,
            null,
            null,
            FixedNow.UtcDateTime,
            FixedNow.UtcDateTime,
            roles ?? [AppRoles.Student],
            concurrencyToken);

    private sealed class InMemoryUserAccountRepository : IUserAccountRepository
    {
        private readonly Dictionary<long, AccountUser> _users = [];
        public int ActiveAdminCount { get; set; } = 1;

        public InMemoryUserAccountRepository(params AccountUser[] users)
        {
            foreach (var user in users)
            {
                _users[user.Id] = user;
            }
        }

        public Task<AccountUser?> GetUserAsync(long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_users.GetValueOrDefault(userId));

        public Task<bool> IdentityExistsExcludingUserAsync(
            long userId,
            string? studentCode,
            string? employeeCode,
            CancellationToken cancellationToken = default)
        {
            var exists = _users.Values.Any(u => u.Id != userId
                && ((studentCode != null && string.Equals(u.StudentCode, studentCode, StringComparison.OrdinalIgnoreCase))
                    || (employeeCode != null && string.Equals(u.EmployeeCode, employeeCode, StringComparison.OrdinalIgnoreCase))));
            return Task.FromResult(exists);
        }

        public async Task<AccountUser> UpdateAccountAsync(
            long userId,
            UpdateAccountData data,
            string? concurrencyToken,
            DateTime utcNow,
            Func<AccountUser, Task>? onUpdatedAsync = null,
            CancellationToken cancellationToken = default)
        {
            var user = _users[userId];
            if (!string.IsNullOrEmpty(concurrencyToken) && user.ConcurrencyToken != concurrencyToken)
            {
                throw new ConflictException("Concurrency token mismatch.");
            }

            var updated = user with
            {
                FullName = data.FullName,
                Phone = data.Phone,
                Title = data.Title,
                StudentCode = data.StudentCode,
                EmployeeCode = data.EmployeeCode,
                UpdatedAt = utcNow,
                ConcurrencyToken = "token-next"
            };

            if (onUpdatedAsync != null)
            {
                await onUpdatedAsync(updated);
            }

            _users[userId] = updated;
            return updated;
        }

        public Task<AccountUser> SetStatusAsync(long userId, string status, DateTime utcNow, CancellationToken cancellationToken = default)
        {
            var user = _users[userId];
            var updated = user with { Status = status, UpdatedAt = utcNow };
            _users[userId] = updated;
            return Task.FromResult(updated);
        }

        public Task<int> CountActiveUsersInRoleAsync(string roleCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActiveAdminCount);

        public Task<bool> UserHasRoleAsync(long userId, string roleCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(_users.TryGetValue(userId, out var u) && u.Roles.Contains(roleCode, StringComparer.Ordinal));

        public Task<PagedResult<AccountUser>> GetUsersAsync(string? search, string? status, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IdentityExistsAsync(string email, string? studentCode, string? employeeCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountUser> CreateUserAsync(CreateAccountData data, long actorUserId, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountUser>> CreateUsersAsync(IReadOnlyCollection<CreateAccountData> accounts, long actorUserId, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AccountUser> UpdateProfileAsync(long userId, UpdateProfileData data, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AssignRoleAsync(long userId, long roleId, long actorUserId, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveRoleAsync(long userId, long roleId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubRoleRepository(SecurityRole role) : IRolePermissionRepository
    {
        public Task<SecurityRole?> GetRoleAsync(long roleId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SecurityRole?>(role.Id == roleId ? role : null);

        public Task<PagedResult<SecurityRole>> GetRolesAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RoleCodeExistsAsync(string code, long? excludedRoleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SecurityRole> CreateRoleAsync(string code, string name, string? description, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SecurityRole> UpdateRoleAsync(long roleId, string code, string name, string? description, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteRoleAsync(long roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<SecurityPermission>> GetPermissionsAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SecurityPermission?> GetPermissionAsync(long permissionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> PermissionCodeExistsAsync(string code, long? excludedPermissionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SecurityPermission> CreatePermissionAsync(string code, string name, string? description, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SecurityPermission> UpdatePermissionAsync(long permissionId, string code, string name, string? description, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePermissionAsync(long permissionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReplaceRolePermissionsAsync(long roleId, IReadOnlyCollection<long> permissionIds, long actorUserId, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PermissionMatrix> GetPermissionMatrixAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FailingAuditTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated audit write failure for atomicity verification.");
    }
}
