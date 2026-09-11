using System.Linq.Expressions;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class SupervisorRequestMapper
{
    public static readonly Expression<Func<SupervisorRequest, SupervisorRequestModel>> Projection = r =>
        new(r.Id, r.ProjectId, r.SupervisorProfileId, r.SupervisorProfile.UserId, r.RequestedBy,
            r.Status, r.RequestMessage, r.ResponseMessage, r.RequestedAt, r.RespondedAt,
            r.SupervisorAssignment != null && r.SupervisorAssignment.ProjectId == r.ProjectId
                && r.SupervisorAssignment.SupervisorProfileId == r.SupervisorProfileId
                ? r.SupervisorAssignment.Id : null);
}
