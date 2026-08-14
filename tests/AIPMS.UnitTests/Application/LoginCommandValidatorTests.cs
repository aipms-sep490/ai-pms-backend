using AIPMS.Application.Features.Auth.Commands.Login;

namespace AIPMS.UnitTests.Application;

public sealed class LoginCommandValidatorTests
{
    [Fact]
    public async Task Validate_InvalidEmailAndEmptyPassword_ReturnsErrors()
    {
        var validator = new LoginCommandValidator();

        var result = await validator.ValidateAsync(new LoginCommand("invalid", string.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(LoginCommand.Email));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(LoginCommand.Password));
    }

    [Fact]
    public async Task Validate_ValidCredentials_ReturnsNoErrors()
    {
        var validator = new LoginCommandValidator();

        var result = await validator.ValidateAsync(
            new LoginCommand("student@aipms.test", "Password@123"));

        Assert.True(result.IsValid);
    }
}
