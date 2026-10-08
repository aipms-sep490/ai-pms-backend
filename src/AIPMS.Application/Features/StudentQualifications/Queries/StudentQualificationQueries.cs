using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.StudentQualifications.DTOs;
using AIPMS.Application.Features.StudentQualifications.Models;
using AIPMS.Application.Features.StudentQualifications.Services;
using AIPMS.Application.Features.Deliverables.DTOs;
using MediatR;

namespace AIPMS.Application.Features.StudentQualifications.Queries;

public sealed record GetMyStudentQualificationQuery(
    string QualificationType = StudentQualificationTypes.CapstoneReadiness)
    : IRequest<StudentQualificationDto?>;

public sealed record GetStudentQualificationCertificateQuery(long QualificationId)
    : IRequest<StudentQualificationCertificateDto>;

public sealed record DownloadStudentQualificationCertificateQuery(long QualificationId)
    : IRequest<FileDownload>;

public sealed record GetStudentQualificationVerificationQueueQuery(
    string? VerificationStatus,
    string? Search,
    int Page = 1,
    int PageSize = 20)
    : IRequest<PagedResult<StudentQualificationDto>>;

public sealed record GetProjectPeriodQualificationPolicyQuery(long ProjectPeriodId)
    : IRequest<ProjectPeriodQualificationPolicyDto>;

public sealed class GetMyStudentQualificationQueryHandler(StudentQualificationWorkflow workflow)
    : IRequestHandler<GetMyStudentQualificationQuery, StudentQualificationDto?>
{
    public Task<StudentQualificationDto?> Handle(
        GetMyStudentQualificationQuery request, CancellationToken cancellationToken) =>
        workflow.MineAsync(request.QualificationType, cancellationToken);
}

public sealed class GetStudentQualificationCertificateQueryHandler(StudentQualificationWorkflow workflow)
    : IRequestHandler<GetStudentQualificationCertificateQuery, StudentQualificationCertificateDto>
{
    public Task<StudentQualificationCertificateDto> Handle(GetStudentQualificationCertificateQuery request, CancellationToken ct) =>
        workflow.CertificateAsync(request.QualificationId, ct);
}

public sealed class DownloadStudentQualificationCertificateQueryHandler(StudentQualificationWorkflow workflow)
    : IRequestHandler<DownloadStudentQualificationCertificateQuery, FileDownload>
{
    public Task<FileDownload> Handle(DownloadStudentQualificationCertificateQuery request, CancellationToken ct) =>
        workflow.DownloadCertificateAsync(request.QualificationId, ct);
}

public sealed class GetStudentQualificationVerificationQueueQueryHandler(StudentQualificationWorkflow workflow)
    : IRequestHandler<GetStudentQualificationVerificationQueueQuery, PagedResult<StudentQualificationDto>>
{
    public Task<PagedResult<StudentQualificationDto>> Handle(
        GetStudentQualificationVerificationQueueQuery request, CancellationToken cancellationToken) =>
        workflow.QueueAsync(request.VerificationStatus, request.Search, request.Page, request.PageSize, cancellationToken);
}

public sealed class GetProjectPeriodQualificationPolicyQueryHandler(StudentQualificationWorkflow workflow)
    : IRequestHandler<GetProjectPeriodQualificationPolicyQuery, ProjectPeriodQualificationPolicyDto>
{
    public Task<ProjectPeriodQualificationPolicyDto> Handle(
        GetProjectPeriodQualificationPolicyQuery request, CancellationToken cancellationToken) =>
        workflow.GetPeriodPolicyAsync(request.ProjectPeriodId, cancellationToken);
}
