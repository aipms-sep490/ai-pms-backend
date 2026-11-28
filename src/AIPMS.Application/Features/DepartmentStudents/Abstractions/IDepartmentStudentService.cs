using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Application.Features.StudentRosters;

namespace AIPMS.Application.Features.DepartmentStudents.Abstractions;

public interface IDepartmentStudentService
{
    Task<IReadOnlyList<DepartmentSpecializationDto>> ListSpecializationsAsync(
        long? departmentId,
        long? majorId,
        bool includeInactive,
        CancellationToken ct = default);

    Task<DepartmentSpecializationDto> CreateSpecializationAsync(
        CreateDepartmentSpecializationRequest request,
        CancellationToken ct = default);

    Task<DepartmentSpecializationDto> UpdateSpecializationAsync(
        long specializationId,
        UpdateDepartmentSpecializationRequest request,
        CancellationToken ct = default);

    Task<DepartmentStudentDirectoryPageDto> ListDepartmentStudentsAsync(
        DepartmentStudentQuery query,
        CancellationToken ct = default);

    Task<DepartmentStudentDetailDto> GetDepartmentStudentDetailAsync(
        long studentUserId,
        long? semesterId,
        CancellationToken ct = default);

    Task<DepartmentStudentDetailDto> UpdateDepartmentStudentAcademicAsync(
        long studentUserId,
        UpdateDepartmentStudentAcademicRequest request,
        long? semesterId = null,
        CancellationToken ct = default);

    Task<RosterFile> ExportDepartmentStudentsAsync(
        DepartmentStudentQuery query,
        CancellationToken ct = default);

    Task<AcademicProfileDto> UpdateMyStudentAcademicAsync(
        UpdateMyStudentAcademicRequest request,
        CancellationToken ct = default);

    Task<RosterFile> ExportAdminUserAccountsAsync(
        AdminUserAccountExportQuery query,
        CancellationToken ct = default);

    Task<RosterFile> ExportEmergencyContactsAsync(
        DepartmentStudentQuery query,
        CancellationToken ct = default);
}
