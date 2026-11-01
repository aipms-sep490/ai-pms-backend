using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal static class DeliverableReviewAccess
{
    public static IQueryable<DeliverableVersion> VisibleVersions(AipmsDbContext db, AiScope scope) =>
        db.DeliverableVersions.AsNoTracking().Where(v => v.Deliverable.ProjectId == scope.ProjectId &&
            (scope.Whole || (v.Deliverable.TargetMajorId != null && scope.Majors.Contains(v.Deliverable.TargetMajorId.Value)) ||
                ((v.Deliverable.EvidenceScope == "COMMON" || v.Deliverable.EvidenceScope == null) && v.Deliverable.TargetMajorId == null && v.Deliverable.TargetStudentId == null)) &&
            (!db.TeamMembers.Any(m => m.TeamId == v.Deliverable.Project.TeamId && m.UserId == scope.Actor && m.LeftAt == null) ||
                ((v.Deliverable.TargetStudentId == null || v.Deliverable.TargetStudentId == scope.Actor) &&
                 (v.Deliverable.TargetMajorId == null || db.Users.Any(u => u.Id == scope.Actor && u.MajorId == v.Deliverable.TargetMajorId)))));
}
