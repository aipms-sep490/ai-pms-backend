using System;
using System.Linq;
using AIPMS.Application.Features.Projects.DTOs;
using ProjectEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Project;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class ProjectMapper
{
    public static ProjectDto ToDto(this ProjectEntity project) =>
        new(
            project.Id,
            project.TeamId,
            project.Team.Name,
            project.Code,
            project.Title,
            project.Description,
            project.Objectives,
            project.Status,
            project.RegisteredAt,
            project.SubmittedAt,
            project.ApprovedAt,
            project.CompletedAt,
            project.CreatedBy,
            project.CreatedByNavigation.FullName,
            project.CreatedAt,
            project.UpdatedAt,
            project.ProblemStatement,
            project.ExpectedOutput,
            Convert.ToBase64String(project.RowVersion),
            project.ProjectMajors.Select(static pm => new ProjectMajorDto(
                pm.Id,
                pm.MajorId,
                pm.Major.Code,
                pm.Major.Name)).ToArray(),
            project.ProjectTags.Select(static pt => new ProjectTagDto(
                pt.TagId,
                pt.Tag.Name,
                pt.Tag.TagType)).ToArray()
        );

    public static ProjectSummaryDto ToSummaryDto(this ProjectEntity project) =>
        new(
            project.Id,
            project.TeamId,
            project.Team.Name,
            project.Code,
            project.Title,
            project.Status,
            project.CreatedAt,
            project.SubmittedAt,
            project.ProjectMajors.Select(static pm => new ProjectMajorDto(
                pm.Id,
                pm.MajorId,
                pm.Major.Code,
                pm.Major.Name)).ToArray(),
            project.ProjectTags.Select(static pt => new ProjectTagDto(
                pt.TagId,
                pt.Tag.Name,
                pt.Tag.TagType)).ToArray()
        );
}
