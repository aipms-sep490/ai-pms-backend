using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Application.Features.Academic.Services;
using MediatR;

namespace AIPMS.Application.Features.Academic.Commands;

public sealed record VerifyAcademicProfileCommand(long UserId) : IRequest<AcademicProfileDto>;
public sealed record RejectAcademicProfileCommand(long UserId, string Reason) : IRequest<AcademicProfileDto>;
public sealed record UpdateAcademicProfileCommand(long UserId, UpdateAcademicProfileRequest Request) : IRequest<AcademicProfileDto>;
public sealed record BulkVerifyAcademicProfilesCommand(BulkVerifyAcademicProfilesRequest Request) : IRequest<BulkVerifyAcademicProfilesResultDto>;

public sealed class UpdateAcademicProfileHandler(
    IAcademicProfileRepository repository, AcademicAccessService access, IAuditTrail audit, TimeProvider clock)
    : IRequestHandler<UpdateAcademicProfileCommand, AcademicProfileDto>
{
    public Task<AcademicProfileDto> Handle(UpdateAcademicProfileCommand request, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var result = await repository.UpdateAcademicScopeAsync(request.UserId, request.Request.DepartmentId,
                request.Request.MajorId, request.Request.ConcurrencyToken, access.ActorUserId, clock.GetUtcNow().UtcDateTime, token);
            await audit.RecordAsync(new AuditEntry(access.ActorUserId, "ACADEMIC_PROFILE_SCOPE_UPDATED", "USER", request.UserId,
                new Dictionary<string, object?> { ["departmentId"] = result.DepartmentId, ["majorId"] = result.MajorId }), token);
            return result;
        }, ct);
}

public sealed class VerifyAcademicProfileHandler(
    IAcademicProfileRepository repository, AcademicAccessService access, IAuditTrail audit, TimeProvider clock)
    : IRequestHandler<VerifyAcademicProfileCommand, AcademicProfileDto>
{
    public Task<AcademicProfileDto> Handle(VerifyAcademicProfileCommand request, CancellationToken ct)
        => repository.InTransactionAsync(token => HandleAsync(request.UserId, "VERIFIED", null, token), ct);

    private async Task<AcademicProfileDto> HandleAsync(long userId, string status, string? reason, CancellationToken ct)
    {
        await repository.LockAsync(userId, ct);
        var profile = await repository.GetAsync(userId, ct) ?? throw new NotFoundException("AcademicProfile", userId);
        if (!profile.DepartmentId.HasValue || !profile.MajorId.HasValue || profile.Status == "VERIFIED" && status == "VERIFIED")
            throw new ConflictException("The student academic profile is incomplete or already verified.");
        await access.EnsureDepartmentStaffInDepartmentAsync(profile.DepartmentId.Value, ct);
        var result = await repository.SetStatusAsync(userId, status, access.ActorUserId, reason, clock.GetUtcNow().UtcDateTime, ct);
        await audit.RecordAsync(new AuditEntry(access.ActorUserId, "ACADEMIC_PROFILE_VERIFIED", "USER", userId,
            new Dictionary<string, object?> { ["status"] = status }), ct);
        return result;
    }
}

public sealed class RejectAcademicProfileHandler(
    IAcademicProfileRepository repository, AcademicAccessService access, IAuditTrail audit, TimeProvider clock)
    : IRequestHandler<RejectAcademicProfileCommand, AcademicProfileDto>
{
    public Task<AcademicProfileDto> Handle(RejectAcademicProfileCommand request, CancellationToken ct) =>
        repository.InTransactionAsync(token => RejectAsync(request, token), ct);

    private async Task<AcademicProfileDto> RejectAsync(RejectAcademicProfileCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ValidationException(new Dictionary<string, string[]> { ["reason"] = ["A rejection reason is required."] });
        await repository.LockAsync(request.UserId, ct);
        var profile = await repository.GetAsync(request.UserId, ct) ?? throw new NotFoundException("AcademicProfile", request.UserId);
        if (!profile.DepartmentId.HasValue) throw new ConflictException("The student academic profile has no department.");
        await access.EnsureDepartmentStaffInDepartmentAsync(profile.DepartmentId.Value, ct);
        var result = await repository.SetStatusAsync(request.UserId, "REJECTED", access.ActorUserId, request.Reason.Trim(), clock.GetUtcNow().UtcDateTime, ct);
        await audit.RecordAsync(new AuditEntry(access.ActorUserId, "ACADEMIC_PROFILE_REJECTED", "USER", request.UserId,
            new Dictionary<string, object?> { ["reason"] = request.Reason.Trim() }), ct);
        return result;
    }
}

public sealed class BulkVerifyAcademicProfilesHandler(
    IAcademicProfileRepository repository, AcademicAccessService access, IAuditTrail audit, TimeProvider clock)
    : IRequestHandler<BulkVerifyAcademicProfilesCommand, BulkVerifyAcademicProfilesResultDto>
{
    public Task<BulkVerifyAcademicProfilesResultDto> Handle(BulkVerifyAcademicProfilesCommand request, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            var result = await repository.BulkVerifyAsync(
                access.ActorUserId,
                request.Request.UserIds,
                request.Request.ClassCode,
                request.Request.MajorId,
                clock.GetUtcNow().UtcDateTime,
                token);
            await audit.RecordAsync(new AuditEntry(access.ActorUserId, "ACADEMIC_PROFILES_BULK_VERIFIED", "USER", null,
                new Dictionary<string, object?>
                {
                    ["verifiedCount"] = result.VerifiedCount,
                    ["skippedCount"] = result.SkippedCount,
                    ["classCode"] = request.Request.ClassCode,
                    ["majorId"] = request.Request.MajorId
                }), token);
            return result;
        }, ct);
}
