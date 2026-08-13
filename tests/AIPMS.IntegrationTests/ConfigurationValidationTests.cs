using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace AIPMS.IntegrationTests;

public sealed class ConfigurationValidationTests
{
    [Fact]
    public void Start_InvalidCorsOrigin_FailsConfigurationValidation()
    {
        using var factory = new InvalidCorsWebApplicationFactory();

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "Every CORS origin must be an absolute HTTP or HTTPS URL.",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    private sealed class InvalidCorsWebApplicationFactory : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cors:AllowedOrigins:0"] = "not-an-origin"
                });
            });
        }
    }
}
