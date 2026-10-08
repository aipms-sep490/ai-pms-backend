using AIPMS.Application.Features.StudentQualifications.DTOs;
using AIPMS.Application.Features.StudentQualifications.Services;
using MediatR;

namespace AIPMS.Application.Features.StudentQualifications.Commands;

public sealed record SubmitStudentQualificationEvidenceCommand(
    SubmitStudentQualificationEvidenceRequest Request) : IRequest<StudentQualificationDto>;

public sealed record UploadStudentQualificationCertificateCommand(SubmitStudentQualificationEvidenceRequest Request,
    AIPMS.Application.Features.Deliverables.Models.UploadContent File) : IRequest<StudentQualificationDto>;

public sealed class UploadStudentQualificationCertificateCommandHandler(StudentQualificationWorkflow workflow)
    : IRequestHandler<UploadStudentQualificationCertificateCommand, StudentQualificationDto>
{
    public Task<StudentQualificationDto> Handle(UploadStudentQualificationCertificateCommand request, CancellationToken ct) =>
        workflow.UploadCertificateAsync(request.Request, request.File, ct);
}

public sealed record VerifyStudentQualificationCommand(long QualificationId, Guid? ExpectedConcurrencyToken = null)
    : IRequest<StudentQualificationDto>;

public sealed record RejectStudentQualificationCommand(long QualificationId, string? Reason, Guid? ExpectedConcurrencyToken = null)
    : IRequest<StudentQualificationDto>;

public sealed record SetProjectPeriodQualificationPolicyCommand(
    long ProjectPeriodId,
    SetProjectPeriodQualificationPolicyRequest Request)
    : IRequest<ProjectPeriodQualificationPolicyDto>;

public sealed class SubmitStudentQualificationEvidenceCommandHandler(StudentQualificationWorkflow workflow)
    : IRequestHandler<SubmitStudentQualificationEvidenceCommand, StudentQualificationDto>
{
    public Task<StudentQualificationDto> Handle(
        SubmitStudentQualificationEvidenceCommand request, CancellationToken cancellationToken) =>
        workflow.SubmitEvidenceAsync(request.Request, cancellationToken);
}

public sealed class VerifyStudentQualificationCommandHandler(StudentQualificationWorkflow workflow)
    : IRequestHandler<VerifyStudentQualificationCommand, StudentQualificationDto>
{
    public Task<StudentQualificationDto> Handle(
        VerifyStudentQualificationCommand request, CancellationToken cancellationToken) =>
        workflow.VerifyAsync(request.QualificationId, request.ExpectedConcurrencyToken, cancellationToken);
}

public sealed class RejectStudentQualificationCommandHandler(StudentQualificationWorkflow workflow)
    : IRequestHandler<RejectStudentQualificationCommand, StudentQualificationDto>
{
    public Task<StudentQualificationDto> Handle(
        RejectStudentQualificationCommand request, CancellationToken cancellationToken) =>
        workflow.RejectAsync(request.QualificationId, request.Reason, request.ExpectedConcurrencyToken, cancellationToken);
}

public sealed class SetProjectPeriodQualificationPolicyCommandHandler(StudentQualificationWorkflow workflow)
    : IRequestHandler<SetProjectPeriodQualificationPolicyCommand, ProjectPeriodQualificationPolicyDto>
{
    public Task<ProjectPeriodQualificationPolicyDto> Handle(
        SetProjectPeriodQualificationPolicyCommand request, CancellationToken cancellationToken) =>
        workflow.SetPeriodPolicyAsync(request.ProjectPeriodId, request.Request, cancellationToken);
}
