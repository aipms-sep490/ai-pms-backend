using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Topics.Abstractions;
using AIPMS.Domain.Topics;

namespace AIPMS.Application.Features.Topics.Services;

public sealed class TopicSelectionGuard(
    IProjectRepository projectRepository,
    ITeamRepository teamRepository,
    TopicWorkflow topicWorkflow,
    TimeProvider timeProvider) : ITopicSelectionGuard
{
    public async Task ValidateTopicSelectionAsync(
        long topicId,
        long projectId,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        // 1. Retrieve project
        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new NotFoundException("Project", projectId);

        // 2. Caller has Project Draft write permission (only Team Leader can modify draft)
        if (!await projectRepository.IsTeamLeaderAsync(project.TeamId, actorUserId, cancellationToken))
        {
            throw new ForbiddenException("Only the Team Leader can select a topic for the project.");
        }

        // 3. Project Draft is still editable
        if (project.Status != "DRAFT" && project.Status != "REVISION_REQUIRED")
        {
            throw new ConflictException("Cannot select a topic on a submitted or non-editable project proposal.");
        }

        // 4. Retrieve topic
        var topic = await topicWorkflow.GetForSelection(topicId, cancellationToken);
        if (topic is null)
        {
            throw new NotFoundException("Topic", topicId);
        }

        // 5. Topic must be PUBLISHED
        if (!string.Equals(topic.Status, TopicSelectionRules.StatusPublished, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Only published topics can be selected.");
        }

        // 6. Retrieve team and verify period/window
        var team = await teamRepository.GetAsync(project.TeamId, cancellationToken)
            ?? throw new NotFoundException("Team", project.TeamId);

        var window = await teamRepository.GetOpenWindowAsync(team.SemesterId, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        if (window is null || window.PeriodId <= 0)
        {
            throw new ConflictException("REGISTRATION_WINDOW_UNAVAILABLE");
        }
        var teamPeriodId = window.PeriodId;

        static bool Enabled(string csv, string value) => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        if (!Enabled(window.AllowedProjectModes, topic.ProjectMode))
            throw new ConflictException("PROJECT_MODE_NOT_ALLOWED_BY_PERIOD");
        if (!Enabled(window.AllowedProposalSources, "PUBLISHED_TOPIC"))
            throw new ConflictException("PROPOSAL_SOURCE_NOT_ALLOWED_BY_PERIOD");

        var topicRequirements = topic.Requirements
            .Select(r => new TopicMajorRequirementRule(r.MajorId, r.MinMembers, r.MaxMembers))
            .ToList();

        var memberEvidences = team.Members
            .Select(m => new TeamMemberEvidence(
                m.MajorId,
                IsVerifiedActive: m.IsEligibleStudent && m.MajorId.HasValue,
                IsFormer: false))
            .ToList();

        var issues = TopicSelectionRules.ValidateSelection(
            topic.Status,
            project.Status,
            topic.ProjectPeriodId,
            teamPeriodId,
            topic.ProjectMode,
            topic.PrimaryMajorId,
            topicRequirements,
            team.AcademicScope?.ProjectMode,
            team.AcademicScope?.PrimaryMajorId,
            memberEvidences);

        if (issues.Count > 0)
        {
            throw new ConflictException(string.Join(", ", issues));
        }
    }
}
