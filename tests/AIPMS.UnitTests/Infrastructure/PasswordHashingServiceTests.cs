using AIPMS.Application.Abstractions.Security;
using AIPMS.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class PasswordHashingServiceTests
{
    private const string ExistingDatabaseHash =
        "AQAAAAIAAYagAAAAECDlj2anj0PYyt+p+4Y/ZoHOJK1yaPX5R0QW/kB8Q+7+HZfcCfIn2WbJH4rtYP+2sg==";

    [Fact]
    public void HashAndVerify_ValidPassword_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure();
        using var provider = services.BuildServiceProvider();
        var passwordHashingService = provider.GetRequiredService<IPasswordHashingService>();

        var hash = passwordHashingService.Hash("Password@123");

        Assert.True(passwordHashingService.Verify(hash, "Password@123"));
        Assert.False(passwordHashingService.Verify(hash, "wrong-password"));
    }

    [Fact]
    public void Verify_ExistingDatabaseIdentityHash_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure();
        using var provider = services.BuildServiceProvider();
        var passwordHashingService = provider.GetRequiredService<IPasswordHashingService>();

        Assert.True(passwordHashingService.Verify(ExistingDatabaseHash, "Aipms@123"));
    }
}
