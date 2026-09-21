using AIPMS.Application.Features.StudentQualifications.DTOs;
using AIPMS.Application.Features.StudentQualifications.Services;
using MediatR;

namespace AIPMS.Application.Features.StudentQualifications.Commands;

public sealed record SubmitStudentQualificationEvidenceCommand(
    SubmitStudentQualificationEvidenceRequest Request) : IRequest<StudentQualificationDto>;

public sealed record VerifyStudentQualificationCommand(long QualificationId)
    : IRequest<StudentQualificationDto>;

public sealed record RejectStudentQualificationCommand(long QualificationId, string? Reason)
    : IRequest<StudentQualificationDto>;

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
        workflow.VerifyAsync(request.QualificationId, cancellationToken);
}

public sealed class RejectStudentQualificationCommandHandler(StudentQualificationWorkflow workflow)
    : IRequestHandler<RejectStudentQualificationCommand, StudentQualificationDto>
{
    public Task<StudentQualificationDto> Handle(
        RejectStudentQualificationCommand request, CancellationToken cancellationToken) =>
        workflow.RejectAsync(request.QualificationId, request.Reason, cancellationToken);
}
