using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.Commands;
using AIPMS.Application.Features.Notifications.Models;
using AIPMS.Application.Features.Notifications.Queries;
using AIPMS.Application.Features.Notifications.Services;
using AIPMS.Application.Features.Notifications.Validators;

namespace AIPMS.UnitTests.Application.Notifications;

public sealed class NotificationInboxTests
{
    private static readonly DateTime Now = new(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handlers_scope_every_operation_to_current_user_and_propagate_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new Repository();
        var inbox = new NotificationInboxService(repository, new Actor(true, 42), new Clock());
        var page = await new GetNotificationsQueryHandler(inbox).Handle(new(false, " TYPE ", 2, 5), cancellation.Token);
        Assert.Equal((42L, false, "TYPE", 2, 5), repository.Filter);
        Assert.Equal(2, page.Page);
        Assert.Equal(5, page.PageSize);
        Assert.Equal(7, page.TotalCount);
        Assert.Equal(DateTimeKind.Utc, Assert.Single(page.Items).CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, page.Items[0].ReadAt!.Value.Kind);
        Assert.Equal(3, (await new GetUnreadNotificationCountQueryHandler(inbox).Handle(new(), cancellation.Token)).Count);
        await new MarkNotificationReadCommandHandler(inbox).Handle(new(9), cancellation.Token);
        Assert.Equal(9, repository.NotificationId);
        Assert.Equal(Now, repository.Time);
        await new MarkAllNotificationsReadCommandHandler(inbox).Handle(new(), cancellation.Token);
        Assert.Equal(42, repository.User);
        Assert.Equal(cancellation.Token, repository.Token);
        Assert.Equal(4, repository.Calls);
    }

    [Theory]
    [InlineData(false, 42L)]
    [InlineData(true, null)]
    [InlineData(true, 0L)]
    public async Task Missing_identity_blocks_every_operation_before_repository(bool authenticated, long? id)
    {
        var repository = new Repository();
        var inbox = new NotificationInboxService(repository, new Actor(authenticated, id), new Clock());
        await Assert.ThrowsAsync<UnauthorizedException>(() => inbox.GetAsync(null, null, 1, 20, default));
        await Assert.ThrowsAsync<UnauthorizedException>(() => inbox.CountUnreadAsync(default));
        await Assert.ThrowsAsync<UnauthorizedException>(() => inbox.MarkReadAsync(1, default));
        await Assert.ThrowsAsync<UnauthorizedException>(() => inbox.MarkAllReadAsync(default));
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Cancelled_request_does_not_access_repository()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var repository = new Repository();
        var inbox = new NotificationInboxService(repository, new Actor(true, 42), new Clock());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inbox.GetAsync(null, null, 1, 20, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inbox.MarkAllReadAsync(cancellation.Token));
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task Unowned_notification_returns_not_found_and_blank_type_means_no_filter()
    {
        var repository = new Repository { Owned = false };
        var inbox = new NotificationInboxService(repository, new Actor(true, 42), new Clock());
        await Assert.ThrowsAsync<NotFoundException>(() => inbox.MarkReadAsync(9, default));
        await inbox.GetAsync(null, "  ", 1, 20, default);
        Assert.Null(repository.Filter.Type);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1000001, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Invalid_pagination_is_rejected(int page, int size) =>
        Assert.False(new GetNotificationsQueryValidator().Validate(new GetNotificationsQuery(null, null, page, size)).IsValid);

    [Fact]
    public void Validators_accept_defaults_and_enforce_type_and_id_bounds()
    {
        var validator = new GetNotificationsQueryValidator();
        Assert.True(validator.Validate(new GetNotificationsQuery()).IsValid);
        Assert.True(validator.Validate(new GetNotificationsQuery(false, new string('A', 50), 1000000, 100)).IsValid);
        Assert.False(validator.Validate(new GetNotificationsQuery(null, new string('A', 51))).IsValid);
        Assert.False(new MarkNotificationReadCommandValidator().Validate(new MarkNotificationReadCommand(0)).IsValid);
        Assert.False(new MarkNotificationReadCommandValidator().Validate(new MarkNotificationReadCommand(-1)).IsValid);
        Assert.True(new MarkNotificationReadCommandValidator().Validate(new MarkNotificationReadCommand(1)).IsValid);
    }

    private sealed record Actor(bool IsAuthenticated, long? UserId) : ICurrentUser
    {
        public string? Email => null;
        public string? FullName => null;
        public IReadOnlyCollection<string> Roles => [];
    }
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private sealed class Repository : INotificationInboxRepository
    {
        public bool Owned { get; init; } = true;
        public int Calls { get; private set; }
        public long User { get; private set; }
        public long NotificationId { get; private set; }
        public DateTime Time { get; private set; }
        public CancellationToken Token { get; private set; }
        public (long User, bool? Read, string? Type, int Page, int Size) Filter { get; private set; }
        private void Record(long user, CancellationToken ct) { User = user; Token = ct; Calls++; }
        public Task<PagedResult<NotificationInboxItem>> GetAsync(long userId, bool? isRead, string? type, int page, int size, CancellationToken ct)
        {
            Record(userId, ct);
            Filter = (userId, isRead, type, page, size);
            return Task.FromResult(new PagedResult<NotificationInboxItem>(
                [new(9, "TYPE", "Title", "Content", null, null, DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), true,
                    DateTime.SpecifyKind(Now, DateTimeKind.Unspecified))], page, size, 7));
        }
        public Task<long> CountUnreadAsync(long userId, CancellationToken ct) { Record(userId, ct); return Task.FromResult(3L); }
        public Task<bool> MarkReadAsync(long userId, long id, DateTime now, CancellationToken ct)
        {
            Record(userId, ct); NotificationId = id; Time = now; return Task.FromResult(Owned);
        }
        public Task MarkAllReadAsync(long userId, DateTime now, CancellationToken ct)
        {
            Record(userId, ct); Time = now; return Task.CompletedTask;
        }
    }
}
