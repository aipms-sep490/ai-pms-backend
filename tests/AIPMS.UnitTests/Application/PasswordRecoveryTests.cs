using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.Commands;
using AIPMS.Application.Features.Auth.Validators;
using AIPMS.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AIPMS.UnitTests.Application;

public sealed class PasswordRecoveryTests
{
    [Fact]
    public async Task Forgot_passes_trimmed_email_to_queue_and_returns_accepted_message()
    {
        var queue = new QueueStub();
        var result = await new ForgotPasswordCommandHandler(queue).Handle(new(" user@example.test "), default);
        Assert.Equal("user@example.test", queue.Email);
        Assert.Contains("Request accepted", result.Message);
        Assert.DoesNotContain("have been sent", result.Message);
    }

    [Fact]
    public async Task Failed_enqueue_does_not_report_accepted()
    {
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => new ForgotPasswordCommandHandler(new QueueStub { Fail = true }).Handle(new("user@example.test"), default));
    }

    [Theory]
    [InlineData(1, 30)][InlineData(2, 60)][InlineData(3, 120)][InlineData(4, 240)][InlineData(10, 240)]
    public void Retry_delay_is_bounded(int attempt, int seconds) => Assert.Equal(seconds, PasswordRecoverySettings.RetryDelay(attempt).TotalSeconds);

    [Theory]
    [InlineData("short")][InlineData("nouppercase123!")][InlineData("NOLOWERCASE123!")][InlineData("NoNumberPassword!")][InlineData("NoSpecial12345")]
    public void Password_policy_remains_authoritative(string password)
    {
        Assert.False(new ResetPasswordCommandValidator().Validate(new ResetPasswordCommand("token", password)).IsValid);
        Assert.False(new ChangePasswordCommandValidator().Validate(new ChangePasswordCommand("old", password)).IsValid);
    }

    [Theory]
    [InlineData("Email:PasswordResetUrl", "http://remote.example/reset-password")]
    [InlineData("Email:PasswordResetUrl", "https://example.test/reset-password?token=bad")]
    [InlineData("Email:PasswordResetUrl", "https://example.test/reset-password#fragment")]
    [InlineData("PasswordRecovery:LookupKey", "short")]
    [InlineData("PasswordRecovery:KeyRingPath", "")]
    [InlineData("Email:Password", "")]
    [InlineData("Email:EnableSsl", "false")]
    public void Incomplete_configuration_is_unready_without_breaking_password_login(string setting, string value)
    {
        using var services = Configure(setting, value);
        Assert.False(services.GetRequiredService<IOptions<PasswordRecoverySettings>>().Value.IsReady);
    }

    [Fact]
    public void Complete_secure_configuration_is_ready()
    {
        using var services = Configure("Email:PasswordResetUrl", "https://example.test/reset-password");
        Assert.True(services.GetRequiredService<IOptions<PasswordRecoverySettings>>().Value.IsReady);
    }

    private static ServiceProvider Configure(string setting, string value)
    {
        var config = new Dictionary<string, string?>
        {
            ["PasswordRecovery:Enabled"] = "true", ["PasswordRecovery:KeyRingPath"] = Path.GetTempPath(),
            ["PasswordRecovery:LookupKey"] = "unit-test-only-lookup-key-at-least-32-characters",
            ["Email:PasswordResetUrl"] = "https://example.test/reset-password", ["Email:Host"] = "smtp.example.test",
            ["Email:Username"] = "test", ["Email:Password"] = "test", ["Email:EnableSsl"] = "true", ["Email:SenderAddress"] = "sender@example.test"
        };
        config[setting] = value;
        var services = new ServiceCollection();
        services.AddLogging(); services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(config).Build());
        services.AddSingleton<IHostEnvironment>(new EnvironmentStub());
        services.AddPasswordRecovery();
        return services.BuildServiceProvider();
    }

    private sealed class QueueStub : IPasswordRecoveryQueue
    {
        public string? Email { get; private set; }
        public bool Fail { get; init; }
        public Task EnqueueAsync(string email, CancellationToken ct) { if (Fail) throw new ServiceUnavailableException("Unavailable"); Email = email; return Task.CompletedTask; }
        public Task<bool> ProcessOneAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task CleanupAsync(CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class EnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
