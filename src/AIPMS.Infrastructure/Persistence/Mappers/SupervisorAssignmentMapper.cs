using System.Linq.Expressions;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class SupervisorAssignmentMapper
{
    public static readonly Expression<Func<SupervisorAssignment, SupervisorAssignmentModel>> Projection = a =>
        new(a.Id, a.ProjectId, a.SupervisorProfileId, a.SupervisorProfile.UserId,
            a.SupervisorProfile.User.FullName, a.SupervisorRequestId, a.IsPrimary,
            a.AssignedAt, a.EndedAt, a.Project.Status);
}
