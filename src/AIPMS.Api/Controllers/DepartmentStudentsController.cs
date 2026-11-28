using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Application.Features.DepartmentStudents;
using AIPMS.Application.Features.DepartmentStudents.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
public sealed class DepartmentStudentsController(IDepartmentStudentService service) : ControllerBase
{
    [HttpGet("/api/v1/departments/specializations")]
    [ProducesResponseType<IReadOnlyList<DepartmentSpecializationDto>>(200)]
    public Task<IReadOnlyList<DepartmentSpecializationDto>> ListSpecializations(
        [FromQuery] long? departmentId,
        [FromQuery] long? majorId,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
        => service.ListSpecializationsAsync(departmentId, majorId, includeInactive, ct);

    [Authorize(Roles = AppRoles.DepartmentStaff)]
    [HttpPost("/api/v1/departments/me/specializations")]
    [ProducesResponseType<DepartmentSpecializationDto>(201)]
    public async Task<ActionResult<DepartmentSpecializationDto>> CreateSpecialization(
        CreateDepartmentSpecializationRequest request,
        CancellationToken ct = default)
    {
        var created = await service.CreateSpecializationAsync(request, ct);
        return StatusCode(201, created);
    }

    [Authorize(Roles = AppRoles.DepartmentStaff)]
    [HttpPut("/api/v1/departments/me/specializations/{specializationId:long}")]
    [ProducesResponseType<DepartmentSpecializationDto>(200)]
    public Task<DepartmentSpecializationDto> UpdateSpecialization(
        long specializationId,
        UpdateDepartmentSpecializationRequest request,
        CancellationToken ct = default)
        => service.UpdateSpecializationAsync(specializationId, request, ct);

    [Authorize(Roles = AppRoles.DepartmentStaff)]
    [HttpGet("/api/v1/departments/me/students")]
    [ProducesResponseType<DepartmentStudentDirectoryPageDto>(200)]
    public Task<DepartmentStudentDirectoryPageDto> ListDepartmentStudents(
        [FromQuery] long? semesterId,
        [FromQuery] string? search,
        [FromQuery] long? majorId,
        [FromQuery] long? specializationId,
        [FromQuery] string? classCode,
        [FromQuery] string? curriculumCode,
        [FromQuery] string? profileStatus,
        [FromQuery] string? qualificationStatus,
        [FromQuery] string? teamStatus,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => service.ListDepartmentStudentsAsync(
            new(semesterId, search, majorId, specializationId, classCode, curriculumCode, profileStatus, qualificationStatus, teamStatus, page, pageSize),
            ct);

    [Authorize(Roles = AppRoles.DepartmentStaff)]
    [HttpGet("/api/v1/departments/me/students/{studentUserId:long}")]
    [ProducesResponseType<DepartmentStudentDetailDto>(200)]
    public Task<DepartmentStudentDetailDto> GetDepartmentStudentDetail(
        long studentUserId,
        [FromQuery] long? semesterId,
        CancellationToken ct = default)
        => service.GetDepartmentStudentDetailAsync(studentUserId, semesterId, ct);

    [Authorize(Roles = AppRoles.DepartmentStaff)]
    [HttpPut("/api/v1/departments/me/students/{studentUserId:long}/academic")]
    [ProducesResponseType<DepartmentStudentDetailDto>(200)]
    public Task<DepartmentStudentDetailDto> UpdateDepartmentStudentAcademic(
        long studentUserId,
        UpdateDepartmentStudentAcademicRequest request,
        [FromQuery] long? semesterId = null,
        CancellationToken ct = default)
        => service.UpdateDepartmentStudentAcademicAsync(studentUserId, request, semesterId, ct);

    [Authorize(Roles = AppRoles.DepartmentStaff)]
    [HttpGet("/api/v1/departments/me/students/export")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/problem+json")]
    public async Task<IActionResult> ExportDepartmentStudents(
        [FromQuery] long? semesterId,
        [FromQuery] string? search,
        [FromQuery] long? majorId,
        [FromQuery] long? specializationId,
        [FromQuery] string? classCode,
        [FromQuery] string? curriculumCode,
        [FromQuery] string? profileStatus,
        [FromQuery] string? qualificationStatus,
        [FromQuery] string? teamStatus,
        CancellationToken ct = default)
    {
        var file = await service.ExportDepartmentStudentsAsync(
            new(semesterId, search, majorId, specializationId, classCode, curriculumCode, profileStatus, qualificationStatus, teamStatus, 1, 100),
            ct);
        Response.Headers.CacheControl = "no-store";
        return File(file.Content, file.ContentType, file.FileName);
    }

    [Authorize(Roles = AppRoles.DepartmentStaff)]
    [HttpGet("/api/v1/departments/me/emergency-contacts/export")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/problem+json")]
    public async Task<IActionResult> ExportEmergencyContacts(
        [FromQuery] long? semesterId,
        [FromQuery] string? search,
        [FromQuery] long? majorId,
        [FromQuery] long? specializationId,
        [FromQuery] string? classCode,
        [FromQuery] string? curriculumCode,
        [FromQuery] string? profileStatus,
        [FromQuery] string? qualificationStatus,
        [FromQuery] string? teamStatus,
        CancellationToken ct = default)
    {
        var file = await service.ExportEmergencyContactsAsync(
            new(semesterId, search, majorId, specializationId, classCode, curriculumCode, profileStatus, qualificationStatus, teamStatus, 1, 100),
            ct);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }

    [Authorize(Roles = AppRoles.Student)]
    [HttpPut("/api/v1/users/me/academic-profile")]
    [ProducesResponseType<AcademicProfileDto>(200)]
    public Task<AcademicProfileDto> UpdateMyStudentAcademic(
        UpdateMyStudentAcademicRequest request,
        CancellationToken ct = default)
        => service.UpdateMyStudentAcademicAsync(request, ct);
}
