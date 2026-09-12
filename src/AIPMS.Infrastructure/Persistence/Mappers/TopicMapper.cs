using System.Text.Json;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Application.Features.Topics.Models;
using AIPMS.Infrastructure.Persistence.Models;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class TopicMapper
{
    public static TopicDto ToDto(this ProjectTopic row, TopicActor actor) => new(row.Id, row.Code, row.Status,
        row.ProjectPeriodId, row.Period.AcademicSemesterId, row.Period.AcademicSemester.OrganizationId,
        row.LeadDepartmentId, row.LeadDepartment.Name, row.Title, row.Description, row.ProblemStatement,
        row.Objectives, row.ExpectedOutput, row.Domain, JsonSerializer.Deserialize<string[]>(row.TechnologiesJson)!,
        JsonSerializer.Deserialize<string[]>(row.KeywordsJson)!, row.ProjectMode, row.PrimaryMajorId,
        row.Requirements.OrderBy(r => r.MajorId).Select(r => new TopicMajorRequirementDto(r.MajorId, r.Major.Code,
            r.Major.Name, r.DepartmentId, r.Department.Name, r.MinMembers, r.MaxMembers, r.Responsibility)).ToArray(),
        row.CreatedBy, row.UpdatedBy, row.CreatedAt, row.UpdatedAt, row.PublishedBy, row.PublishedAt,
        row.ClosedBy, row.ClosedAt, row.CloseReason, row.ConcurrencyToken,
        actor.IsStudent ? actor.HasEligibleStudentProfile && row.Requirements.Any(r => r.MajorId == actor.MajorId) : null);
}
