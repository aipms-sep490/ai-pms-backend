using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.Commands;
using AIPMS.Application.Features.Academic.Models;
using AIPMS.Application.Features.Academic.Services;

namespace AIPMS.UnitTests.Application;

public sealed class AcademicStructureHandlerTests
{
    private static readonly DateTime ExistingTimestamp =
        new(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateMajor_DepartmentStaffOwnScope_CreatesNormalizedMajorAndAudit()
    {
        var repository = CreateRepository();
        var currentUser = new TestCurrentUser(11, AppRoles.DepartmentStaff);
        var accessService = new AcademicAccessService(currentUser, repository);
        var auditTrail = new RecordingAuditTrail();
        var handler = new CreateMajorCommandHandler(
            repository,
            accessService,
            auditTrail,
            TimeProvider.System);

        var result = await handler.Handle(
            new CreateMajorCommand(10, "  se_ai ", "  Software Engineering AI  ", "  Test  "),
            CancellationToken.None);

        Assert.Equal("SE_AI", result.Code);
        Assert.Equal("Software Engineering AI", result.Name);
        Assert.Equal("Test", result.Description);
        Assert.Single(auditTrail.Entries);
        Assert.Equal("ACADEMIC_MAJOR_CREATED", auditTrail.Entries[0].Action);
        Assert.Equal(11, auditTrail.Entries[0].ActorUserId);
    }

    [Fact]
    public async Task CreateMajor_DepartmentStaffOutsideScope_ThrowsForbidden()
    {
        var repository = CreateRepository();
        var currentUser = new TestCurrentUser(11, AppRoles.DepartmentStaff);
        var accessService = new AcademicAccessService(currentUser, repository);
        var handler = new CreateMajorCommandHandler(
            repository,
            accessService,
            new RecordingAuditTrail(),
            TimeProvider.System);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new CreateMajorCommand(20, "FIN", "Finance", null),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateMajor_DuplicateCodeOrName_ThrowsConflict()
    {
        var repository = CreateRepository();
        repository.MajorDuplicate = true;
        var currentUser = new TestCurrentUser(11, AppRoles.DepartmentStaff);
        var accessService = new AcademicAccessService(currentUser, repository);
        var handler = new CreateMajorCommandHandler(
            repository,
            accessService,
            new RecordingAuditTrail(),
            TimeProvider.System);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateMajorCommand(10, "SE", "Software Engineering", null),
            CancellationToken.None));
    }

    private static StubAcademicStructureRepository CreateRepository()
    {
        var repository = new StubAcademicStructureRepository();
        repository.Organizations[1] = new AcademicOrganization(
            1,
            "FPTU",
            "FPT University",
            null,
            true,
            ExistingTimestamp,
            ExistingTimestamp);
        repository.Departments[10] = new AcademicDepartment(
            10,
            1,
            "FPTU",
            "FPT University",
            "IT",
            "Information Technology",
            null,
            true,
            ExistingTimestamp,
            ExistingTimestamp);
        repository.Departments[20] = new AcademicDepartment(
            20,
            1,
            "FPTU",
            "FPT University",
            "BUS",
            "Business",
            null,
            true,
            ExistingTimestamp,
            ExistingTimestamp);
        repository.UserScopes[11] = new AcademicUserScope(1, 10);
        return repository;
    }
}
