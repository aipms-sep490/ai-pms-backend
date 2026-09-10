using AIPMS.Application.Features.Supervisors.Commands;
using AIPMS.Application.Features.Supervisors.Queries;
using AIPMS.Application.Features.Supervisors.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class SupervisorRequestValidatorTests
{
    [Theory]
    [InlineData(0, 1, 0, false)]
    [InlineData(1, 0, 0, false)]
    [InlineData(1, 2, 2001, false)]
    [InlineData(1, 2, 2000, true)]
    public void Send_validates_ids_and_database_message_limit(long project, long profile, int length, bool valid) =>
        Assert.Equal(valid, new SendSupervisorRequestCommandValidator()
            .Validate(new SendSupervisorRequestCommand(project, profile, new string('x', length))).IsValid);

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 2001, false)]
    [InlineData(1, 2000, true)]
    public void Responses_validate_ids_and_database_message_limit(long id, int length, bool valid)
    {
        Assert.Equal(valid, new AcceptSupervisorRequestCommandValidator().Validate(new AcceptSupervisorRequestCommand(id, new string('x', length))).IsValid);
        Assert.Equal(valid, new RejectSupervisorRequestCommandValidator().Validate(new RejectSupervisorRequestCommand(id, new string('x', length))).IsValid);
        Assert.Equal(id > 0, new CancelSupervisorRequestCommandValidator().Validate(new CancelSupervisorRequestCommand(id)).IsValid);
    }

    [Theory]
    [InlineData(null, 1, 20, true)]
    [InlineData("PENDING", 1_000_000, 100, true)]
    [InlineData("ACCEPTED", 1, 20, true)]
    [InlineData("REJECTED", 1, 20, true)]
    [InlineData("CANCELLED", 1, 20, true)]
    [InlineData("OTHER", 1, 20, false)]
    [InlineData(null, 0, 20, false)]
    [InlineData(null, 1, 101, false)]
    [InlineData(null, 1_000_001, 20, false)]
    public void List_validates_status_and_safe_paging(string? status, int page, int size, bool valid) =>
        Assert.Equal(valid, new GetSupervisorRequestsQueryValidator().Validate(new GetSupervisorRequestsQuery(null, status, page, size)).IsValid);
}
