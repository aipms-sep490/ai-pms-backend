using System.Net;
using System.Net.Mail;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Infrastructure.Email;
using AIPMS.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class ExternalIntegrationTests
{
    [Fact]
    public void Drive_settings_require_explicit_folder_and_bounded_timeout()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GoogleDrive:ClientId"] = "drive.apps.googleusercontent.com", ["GoogleDrive:ClientSecret"] = "secret",
            ["GoogleDrive:RefreshToken"] = "refresh", ["GoogleDrive:FolderId"] = "folder-id", ["GoogleDrive:TimeoutSeconds"] = "30"
        }).Build();
        Assert.True(GoogleDriveSettings.Read(config).IsValid);
        Assert.False(GoogleDriveSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["GoogleDrive:ClientId"] = "id", ["GoogleDrive:ClientSecret"] = "secret", ["GoogleDrive:RefreshToken"] = "refresh" }).Build()).IsValid);
    }

    [Fact]
    public async Task Drive_storage_scopes_every_operation_to_folder_and_rejects_duplicates()
    {
        var client = new FakeDriveClient { Existing = ["one", "two"] };
        using var storage = new GoogleDriveFileStorage(Settings(), client);
        await Assert.ThrowsAsync<IOException>(() => storage.OpenReadAsync(Key(), default));
        Assert.Equal("folder-id", client.LastFolder);
        client.Existing = [];
        await storage.WriteAsync(Key(), new MemoryStream([1, 2]), default);
        Assert.Equal("folder-id", client.LastFolder);
        Assert.Single(client.Uploads);
    }

    [Fact]
    public async Task Drive_timeout_is_sanitized_and_does_not_retry()
    {
        var client = new FakeDriveClient { Delay = Timeout.InfiniteTimeSpan };
        using var storage = new GoogleDriveFileStorage(Settings(1), client);
        var error = await Assert.ThrowsAsync<IOException>(() => storage.WriteAsync(Key(), new MemoryStream([1]), default));
        Assert.Contains("timed out", error.Message);
        Assert.Equal(1, client.FindCount);
        Assert.Equal(0, client.UploadCount);
    }

    [Fact]
    public async Task SMTP_sender_marks_transport_failure_as_unsent_and_timeout_is_bounded()
    {
        var transport = new FakeSmtpTransport { Throw = new SmtpException("offline") };
        var sender = new SmtpNotificationEmailSender(Options.Create(EmailSettings()), new TestLogger(), transport);
        var delivery = new NotificationEmailDelivery(1, 1, "x@example.com", "Recipient", "subject", "body");
        var sent = await sender.TrySendAsync(delivery, default);
        Assert.False(sent);
        Assert.Equal(1, transport.Count);
        transport.Throw = new OperationCanceledException();
        sent = await sender.TrySendAsync(delivery, default);
        Assert.False(sent);
    }

    [Fact]
    public async Task Drive_missing_download_and_delete_do_not_access_any_other_folder()
    {
        var client = new FakeDriveClient(); using var storage = new GoogleDriveFileStorage(Settings(), client);
        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.OpenReadAsync(Key(), default));
        await storage.DeleteAsync(Key(), default);
        Assert.Equal(0, client.DeleteCount); Assert.Equal("folder-id", client.LastFolder);
        client.Existing = ["file-id"];
        using var read = await storage.OpenReadAsync(Key(), default);
        Assert.Equal(7, read.ReadByte());
        await storage.DeleteAsync(Key(), default); Assert.Equal(1, client.DeleteCount);
        await Assert.ThrowsAsync<IOException>(() => storage.WriteAsync(Key(), new MemoryStream(), default));
        Assert.Equal(0, client.UploadCount);
    }

    [Theory]
    [InlineData("revoked")][InlineData("forbidden")][InlineData("network")]
    public async Task Drive_provider_errors_do_not_leak_raw_provider_responses(string failure)
    {
        var client = new FakeDriveClient { Failure = failure switch
        {
            "revoked" => new Google.Apis.Auth.OAuth2.Responses.TokenResponseException(new Google.Apis.Auth.OAuth2.Responses.TokenErrorResponse { Error = "invalid_grant", ErrorDescription = "sensitive" }),
            "forbidden" => new Google.GoogleApiException("drive", "sensitive") { HttpStatusCode = HttpStatusCode.Forbidden },
            _ => new HttpRequestException("sensitive")
        }};
        using var storage = new GoogleDriveFileStorage(Settings(), client);
        var error = await Assert.ThrowsAsync<IOException>(() => storage.OpenReadAsync(Key(), default));
        Assert.DoesNotContain("sensitive", error.ToString()); Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved_for_drive_and_mail()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        using var storage = new GoogleDriveFileStorage(Settings(), new FakeDriveClient { Delay = Timeout.InfiniteTimeSpan });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.OpenReadAsync(Key(), cancel.Token));
        var transport = new FakeSmtpTransport { Delay = true };
        var sender = new SmtpNotificationEmailSender(Options.Create(EmailSettings()), new TestLogger(), transport);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.TrySendAsync(new(1, 1, "x@example.com", "User", "subject", "body"), cancel.Token));
    }

    [Fact]
    public async Task SMTP_timeout_never_reports_success_and_reset_failure_does_not_escape()
    {
        var transport = new FakeSmtpTransport { Delay = true };
        var sender = new SmtpNotificationEmailSender(Options.Create(EmailSettings()), new TestLogger(), transport);
        Assert.False(await sender.TrySendAsync(new(1, 1, "x@example.com", "User", "subject", "body"), default));
        var reset = new SmtpPasswordResetNotifier(Options.Create(EmailSettings()), Microsoft.Extensions.Logging.Abstractions.NullLogger<SmtpPasswordResetNotifier>.Instance, transport);
        await reset.SendAsync("x@example.com", "User", "raw-reset-token", DateTime.UtcNow.AddMinutes(5));
        transport.Delay = false; transport.Throw = new SmtpException("denied");
        await reset.SendAsync("x@example.com", "User", "raw-reset-token", DateTime.UtcNow.AddMinutes(5));
    }

    [Fact]
    public async Task SMTP_success_preserves_sender_and_plain_text_content()
    {
        var transport = new FakeSmtpTransport();
        var sender = new SmtpNotificationEmailSender(Options.Create(EmailSettings()), new TestLogger(), transport);
        Assert.True(await sender.TrySendAsync(new(1, 1, "x@example.com", "User", "subject", "body"), default));
        Assert.Equal("AI-PMS", transport.SenderName); Assert.Contains("body", transport.Body);
    }

    [Fact]
    public void SMTP_configuration_requires_tls_credentials_when_enabled()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["NotificationEmail:Enabled"] = "true" }).Build();
        var result = new IntegrationConfigurationValidator(config).Validate(null, new EmailSettings { Host = "smtp.example.com", SenderAddress = "bad" });
        Assert.True(result.Failed);
        Assert.True(new IntegrationConfigurationValidator(config).Validate(null, EmailSettings()).Succeeded);
        var empty = new ConfigurationBuilder().Build();
        Assert.True(new IntegrationConfigurationValidator(empty).Validate(null, new EmailSettings()).Succeeded);
    }

    private static GoogleDriveSettings Settings(int timeout = 30) => new() { ClientId = "id", ClientSecret = "secret", RefreshToken = "refresh", FolderId = "folder-id", TimeoutSeconds = timeout };
    private static string Key() => "0123456789abcdef0123456789abcdef";
    private static EmailSettings EmailSettings() => new() { Host = "smtp.example.com", Port = 587, SenderAddress = "no-reply@example.com", Username = "user", Password = "pass", TimeoutSeconds = 1 };

    private sealed class FakeDriveClient : IGoogleDriveClient
    {
        public IReadOnlyList<string> Existing { get; set; } = [];
        public TimeSpan Delay { get; set; }
        public string? LastFolder { get; private set; }
        public int FindCount { get; private set; }
        public int UploadCount { get; private set; }
        public int DeleteCount { get; private set; }
        public Exception? Failure { get; init; }
        public List<string> Uploads { get; } = [];
        public async Task<IReadOnlyList<string>> FindAsync(string folderId, string key, CancellationToken ct) { LastFolder = folderId; FindCount++; if (Failure is not null) throw Failure; await Task.Delay(Delay, ct); return Existing; }
        public Task UploadAsync(string folderId, string key, Stream content, CancellationToken ct) { LastFolder = folderId; UploadCount++; Uploads.Add(key); return Task.CompletedTask; }
        public Task DownloadAsync(string id, Stream output, CancellationToken ct) { Assert.Equal("file-id", id); output.WriteByte(7); return Task.CompletedTask; }
        public Task DeleteAsync(string id, CancellationToken ct) { Assert.Equal("file-id", id); DeleteCount++; return Task.CompletedTask; }
        public Task CheckFolderAsync(string folderId, CancellationToken ct) { LastFolder = folderId; return Task.CompletedTask; }
    }
    private sealed class FakeSmtpTransport : ISmtpTransport
    {
        public Exception? Throw { get; set; }
        public int Count { get; private set; }
        public bool Delay { get; set; }
        public string? SenderName { get; private set; }
        public string? Body { get; private set; }
        public Task SendAsync(EmailSettings settings, MailMessage message, CancellationToken ct)
        { Count++; SenderName = message.From?.DisplayName; Body = message.Body;
            return Delay ? Task.Delay(Timeout.Infinite, ct) : Throw is null ? Task.CompletedTask : Task.FromException(Throw); }
    }
    private sealed class TestLogger : Microsoft.Extensions.Logging.ILogger<SmtpNotificationEmailSender>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
