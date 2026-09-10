using AIPMS.Application.Features.Supervisors.Models;

namespace AIPMS.Application.Features.Supervisors.DTOs;

public sealed record SupervisorExpertiseDto(string Name, string? ProficiencyLevel);
public sealed record SupervisorProfileDto(long Id, long UserId, string FullName,
    long DepartmentId, string DepartmentName, string? Bio, bool IsAvailable,
    IReadOnlyList<SupervisorExpertiseDto> Expertise);
public sealed record UpdateSupervisorProfileRequest(string? Bio, bool IsAvailable);
public sealed record ReplaceSupervisorExpertiseRequest(IReadOnlyList<SupervisorExpertiseDto> Expertise);

public static class SupervisorDtoMapper
{
    public static SupervisorProfileDto ToDto(this SupervisorProfileModel model) => new(
        model.Id, model.UserId, model.FullName, model.DepartmentId, model.DepartmentName,
        model.Bio, model.IsAvailable,
        model.Expertise.Select(e => new SupervisorExpertiseDto(e.Name, e.ProficiencyLevel)).ToArray());
}
