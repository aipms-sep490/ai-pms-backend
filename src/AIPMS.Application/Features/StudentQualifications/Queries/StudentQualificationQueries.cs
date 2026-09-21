using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.StudentQualifications.DTOs;
using AIPMS.Application.Features.StudentQualifications.Models;
using AIPMS.Application.Features.StudentQualifications.Services;
using MediatR;

namespace AIPMS.Application.Features.StudentQualifications.Queries;

public sealed record GetMyStudentQualificationQuery(
    string QualificationType = StudentQualificationTypes.CapstoneReadiness)
    : IRequest<StudentQualificationDto?>;

public sealed record GetStudentQualificationVerificationQueueQuery(
    string? VerificationStatus,
    string? Search,
    int Page = 1,
    int PageSize = 20)
    : IRequest<PagedResult<StudentQualificationDto>>;

public sealed class GetMyStudentQualificationQueryHandler(StudentQualificationWorkflow workflow)
    : IRequestHandler<GetMyStudentQualificationQuery, StudentQualificationDto?>
{
    public Task<StudentQualificationDto?> Handle(
        GetMyStudentQualificationQuery request, CancellationToken cancellationToken) =>
        workflow.MineAsync(request.QualificationType, cancellationToken);
}

public sealed class GetStudentQualificationVerificationQueueQueryHandler(StudentQualificationWorkflow workflow)
    : IRequestHandler<GetStudentQualificationVerificationQueueQuery, PagedResult<StudentQualificationDto>>
{
    public Task<PagedResult<StudentQualificationDto>> Handle(
        GetStudentQualificationVerificationQueueQuery request, CancellationToken cancellationToken) =>
        workflow.QueueAsync(request.VerificationStatus, request.Search, request.Page, request.PageSize, cancellationToken);
}
