using AIPMS.Application.Features.Projects.Commands;
using AIPMS.Application.Features.Projects.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class ArchiveProjectValidatorTests
{
    [Fact]
    public void Requires_positive_id_and_base64_concurrency_token()
    {
        var validator = new ArchiveProjectCommandValidator();
        Assert.True(validator.Validate(new ArchiveProjectCommand(1, "dG9rZW4=", null)).IsValid);
        Assert.False(validator.Validate(new ArchiveProjectCommand(0, "dG9rZW4=", null)).IsValid);
        Assert.False(validator.Validate(new ArchiveProjectCommand(1, "stale", null)).IsValid);
        Assert.False(validator.Validate(new ArchiveProjectCommand(1, "dG9rZW4=", new string('x', 1001))).IsValid);
    }
}
