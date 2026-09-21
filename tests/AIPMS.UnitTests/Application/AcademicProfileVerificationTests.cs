using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.Commands;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Application.Features.Academic.Queries;
using AIPMS.Application.Features.Academic.Services;
using AIPMS.Application.Features.Academic.Models;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Security;

namespace AIPMS.UnitTests.Application;

public sealed class AcademicProfileVerificationTests
{
    [Fact]
    public async Task Verify_StaffOwnDepartment_UpdatesStatusAndAudit()
    {
        var repo = new StubProfileRepository(Profile(22, 10));
        var audit = new RecordingAuditTrail();
        var handler = new VerifyAcademicProfileHandler(repo,
            new AcademicAccessService(new TestCurrentUser(11, AppRoles.DepartmentStaff), new ScopeRepo(10)), audit, TimeProvider.System);

        var result = await handler.Handle(new VerifyAcademicProfileCommand(22), default);

        Assert.Equal("VERIFIED", result.Status);
        Assert.Equal("ACADEMIC_PROFILE_VERIFIED", Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task Verify_StaffOutsideDepartment_Forbidden()
    {
        var repo = new StubProfileRepository(Profile(22, 20));
        var handler = new VerifyAcademicProfileHandler(repo,
            new AcademicAccessService(new TestCurrentUser(11, AppRoles.DepartmentStaff), new ScopeRepo(10)),
            new RecordingAuditTrail(), TimeProvider.System);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new VerifyAcademicProfileCommand(22), default));
    }

    [Fact]
    public async Task Reject_RequiresReason()
    {
        var repo = new StubProfileRepository(Profile(22, 10));
        var handler = new RejectAcademicProfileHandler(repo,
            new AcademicAccessService(new TestCurrentUser(11, AppRoles.DepartmentStaff), new ScopeRepo(10)),
            new RecordingAuditTrail(), TimeProvider.System);

        await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(new RejectAcademicProfileCommand(22, "  "), default));
    }

    [Fact]
    public async Task List_StaffCannotOverrideDepartmentScope()
    {
        var handler = new GetAcademicProfilesHandler(new StubProfileRepository(Profile(22, 10)),
            new AcademicAccessService(new TestCurrentUser(11, AppRoles.DepartmentStaff), new ScopeRepo(10)));
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new GetAcademicProfilesQuery(null, 20, 1, 20), default));
    }

    private static AcademicProfileDto Profile(long id, long departmentId) =>
        new(id, "Student", "student@test", "S001", departmentId, "IT", 5, "SE", "PENDING", null, null, null);

    private sealed class StubProfileRepository(AcademicProfileDto profile) : IAcademicProfileRepository
    {
        public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) => action(ct);
        public Task<long?> GetReviewerDepartmentAsync(long actorId, CancellationToken ct) => Task.FromResult<long?>(10);
        public Task LockAsync(long userId, CancellationToken ct) => Task.CompletedTask;
        public AcademicProfileDto Current { get; private set; } = profile;
        public Task<AcademicProfileDto?> GetAsync(long userId, CancellationToken ct = default) => Task.FromResult<AcademicProfileDto?>(userId == Current.UserId ? Current : null);
        public Task<PagedResult<AcademicProfileDto>> SearchAsync(string? status, long? departmentId, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<AcademicProfileDto>([Current], page, pageSize, 1));
        public Task<AcademicProfileDto> SetStatusAsync(long userId, string status, long reviewerId, string? reason, DateTime now, CancellationToken ct = default)
        { Current = Current with { Status = status, ReviewedBy = reviewerId, ReviewedAt = now, RejectionReason = reason }; return Task.FromResult(Current); }
        public Task<bool> IsVerifiedAsync(long userId, CancellationToken ct = default) => Task.FromResult(Current.Status == "VERIFIED");
    }

    private sealed class ScopeRepo(long departmentId) : IAcademicStructureRepository
    {
        public Task<AcademicUserScope?> GetUserScopeAsync(long userId, CancellationToken ct = default) => Task.FromResult<AcademicUserScope?>(new(1, departmentId));
        public Task<PagedResult<AIPMS.Application.Features.Academic.Models.AcademicOrganization>> GetOrganizationsAsync(string? s, bool? a, int p, int z, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AIPMS.Application.Features.Academic.Models.AcademicOrganization?> GetOrganizationAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> OrganizationCodeOrNameExistsAsync(string c, string n, long? e, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AIPMS.Application.Features.Academic.Models.AcademicOrganization> CreateOrganizationAsync(string c, string n, string? d, DateTime t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AIPMS.Application.Features.Academic.Models.AcademicOrganization> UpdateOrganizationAsync(long i, string c, string n, string? d, DateTime t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AcademicOrganization> SetOrganizationActiveAsync(long i, bool a, DateTime t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<AIPMS.Application.Features.Academic.Models.AcademicDepartment>> GetDepartmentsAsync(long? o, string? s, bool? a, int p, int z, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AIPMS.Application.Features.Academic.Models.AcademicDepartment?> GetDepartmentAsync(long i, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DepartmentCodeOrNameExistsAsync(long o, string c, string n, long? e, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AIPMS.Application.Features.Academic.Models.AcademicDepartment> CreateDepartmentAsync(long o, string c, string n, string? d, DateTime t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AIPMS.Application.Features.Academic.Models.AcademicDepartment> UpdateDepartmentAsync(long i, string c, string n, string? d, DateTime t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AcademicDepartment> SetDepartmentActiveAsync(long i, bool a, DateTime t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResult<AIPMS.Application.Features.Academic.Models.AcademicMajor>> GetMajorsAsync(long? o, long? d, string? s, bool? a, int p, int z, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AIPMS.Application.Features.Academic.Models.AcademicMajor?> GetMajorAsync(long i, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> MajorCodeOrNameExistsAsync(long d, string c, string n, long? e, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AIPMS.Application.Features.Academic.Models.AcademicMajor> CreateMajorAsync(long d, string c, string n, string? x, DateTime t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AIPMS.Application.Features.Academic.Models.AcademicMajor> UpdateMajorAsync(long i, long d, string c, string n, string? x, DateTime t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AcademicMajor> SetMajorActiveAsync(long i, bool a, DateTime t, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AIPMS.Application.Features.Academic.Models.AcademicHierarchyOrganization>> GetHierarchyAsync(long? o, string? s, bool include, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
