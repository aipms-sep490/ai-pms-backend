using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AIPMS.IntegrationTests;

public class AipmsWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=(local);Database=AIPMS_Tests;",
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
                ["Observability:LogFilePath"] = Path.Combine(
                    Path.GetTempPath(),
                    "aipms-tests-.log")
            });
        });
    }
}
