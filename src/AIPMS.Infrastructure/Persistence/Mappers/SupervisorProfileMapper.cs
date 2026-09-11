using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class SupervisorProfileMapper
{
    public static SupervisorProfileModel ToApplication(this SupervisorProfile entity) => new(
        entity.Id, entity.UserId, entity.User.FullName, entity.User.DepartmentId!.Value,
        entity.User.Department!.Name, entity.Bio, entity.IsAvailable,
        entity.SupervisorExpertises.OrderBy(e => e.ExpertiseName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id).Select(e => new SupervisorExpertiseModel(e.ExpertiseName, e.ProficiencyLevel)).ToArray());
}
