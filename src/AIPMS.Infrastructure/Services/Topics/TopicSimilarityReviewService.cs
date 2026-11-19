using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.Topics.Abstractions;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AIPMS.Infrastructure.Services.Topics;

internal sealed class TopicSimilarityReviewService(AipmsDbContext db, ICurrentUser currentUser,
    IAiTextGenerationProvider provider, IConfiguration configuration, IAuditTrail audit, TimeProvider clock)
    : ITopicSimilarityReviewService
{
    public async Task<TopicAiSimilarityResultDto> CheckAsync(TopicAiSimilarityRequest input, CancellationToken ct)
    {
        var scope = await Authorize(input, ct);
        var repository = new TopicRepository(db);
        var deterministic = await repository.CheckSimilarityAsync(scope.OrganizationId, scope.DepartmentId,
            input.Title, input.Keywords, input.Domain, 3, scope.TopicId, scope.ProjectId, ct);
        var periods = await db.ProjectPeriods.AsNoTracking()
            .Where(p => p.AcademicSemester.OrganizationId == scope.OrganizationId && p.PeriodType == "REGISTRATION")
            .OrderByDescending(p => p.StartAt).ThenByDescending(p => p.Id)
            .Select(p => new { p.Id, p.AcademicSemesterId }).Take(3).ToArrayAsync(ct);
        var periodIds = periods.Select(p => p.Id).ToArray();
        var semesterIds = periods.Select(p => p.AcademicSemesterId).Distinct().ToArray();
        var topics = db.Set<ProjectTopic>().AsNoTracking().Where(t => t.Status == "PUBLISHED"
            && periodIds.Contains(t.ProjectPeriodId) && t.LeadDepartmentId == scope.DepartmentId
            && (!scope.TopicId.HasValue || t.Id != scope.TopicId.Value));
        var projects = db.Projects.AsNoTracking().Where(p => p.Team.AcademicSemester.OrganizationId == scope.OrganizationId
            && semesterIds.Contains(p.Team.AcademicSemesterId)
            && (p.Status == "APPROVED" || p.Status == "ACTIVE" || p.Status == "COMPLETED")
            && (!scope.ProjectId.HasValue || p.Id != scope.ProjectId.Value)
            && (p.ProjectMajors.Any(pm => pm.Major.DepartmentId == scope.DepartmentId)
                || db.Set<ProjectRegistrationSnapshot>().Any(s => s.ProjectId == p.Id && s.LeadDepartmentId == scope.DepartmentId))
            && (p.TopicId == null || !topics.Any(t => t.Id == p.TopicId)));
        var total = await topics.CountAsync(ct) + await projects.CountAsync(ct);
        var catalogue = await topics.OrderByDescending(t => t.Id).Take(80).Select(t => new
        {
            t.Id, t.Title, SemesterCode = t.Period.AcademicSemester.Code,
            t.ProblemStatement, t.Objectives, t.ExpectedOutput, t.Domain, t.KeywordsJson
        }).ToArrayAsync(ct);
        // Only catalogue text is public. Other teams' project descriptions must never enter the provider prompt.
        var metadata = await projects.OrderByDescending(p => p.Id).Take(80).Select(p => new
        {
            p.Id, p.Title, SemesterCode = p.Team.AcademicSemester.Code,
            Domain = p.ProjectTags.Where(t => t.Tag.TagType == "DOMAIN").Select(t => t.Tag.Name).FirstOrDefault(),
            Keywords = p.ProjectTags.Where(t => t.Tag.TagType == "KEYWORD").Select(t => t.Tag.Name).ToArray()
        }).ToArrayAsync(ct);
        var candidates = catalogue.Select(t => new TopicSimilarityCandidate(t.Id, "TOPIC", t.Title, t.SemesterCode,
            t.ProblemStatement, t.Objectives, t.ExpectedOutput, t.Domain, Keywords(t.KeywordsJson)))
            .Concat(metadata.Select(p => new TopicSimilarityCandidate(p.Id, "PROJECT", p.Title, p.SemesterCode,
                null, null, null, p.Domain, p.Keywords))).ToArray();
        var truncated = total > candidates.Length;
        var analysis = await TopicSimilarityAnalyzer.Analyze(provider, configuration, input, candidates, truncated, ct);
        // Model latency must not permit a former leader or a changed academic scope to receive the result.
        if (await Authorize(input, ct) != scope)
            throw new ConflictException("Phạm vi nhóm đã thay đổi. Hãy tải lại và kiểm tra đề tài lần nữa.");
        var catalogueIds = catalogue.Select(t => t.Id).ToArray();
        if (await topics.CountAsync(t => catalogueIds.Contains(t.Id), ct) != catalogueIds.Length)
            throw new ConflictException("Danh mục đề tài đã thay đổi. Hãy kiểm tra lại để dùng nguồn hiện hành.");
        await audit.RecordAsync(new AuditEntry(scope.ActorId, "TOPIC_AI_SIMILARITY_CHECKED", "TEAM", input.TeamId,
            new Dictionary<string, object?> { ["candidateCount"] = total, ["reviewedCount"] = analysis.ReviewedCount,
                ["generationStatus"] = analysis.Status, ["modelVersion"] = analysis.Model }), ct);
        return new(deterministic.ExactMatch, deterministic.Similar, analysis.Status, analysis.Matches,
            analysis.Summary, analysis.Limitation, candidates.Length, analysis.ReviewedCount,
            truncated || candidates.Length > 12, clock.GetUtcNow().UtcDateTime, analysis.Model);
    }

    private async Task<ReviewScope> Authorize(TopicAiSimilarityRequest input, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long userId) throw new UnauthorizedException();
        var repository = new TopicRepository(db);
        var actor = await repository.GetActorAsync(userId, currentUser.Roles, ct);
        if (actor is not { IsStudent: true, HasEligibleStudentProfile: true, OrganizationId: not null, DepartmentId: not null })
            throw new ForbiddenException("Chỉ sinh viên có hồ sơ học vụ hợp lệ được kiểm tra đề tài của nhóm.");
        var team = await db.Teams.AsNoTracking().Where(t => t.Id == input.TeamId
            && t.AcademicSemester.OrganizationId == actor.OrganizationId
            && t.TeamMembers.Any(m => m.UserId == userId && m.IsLeader && m.LeftAt == null))
            .Select(t => new { ProjectId = t.Project == null ? (long?)null : t.Project.Id,
                TopicId = t.Project == null ? null : t.Project.TopicId }).SingleOrDefaultAsync(ct);
        if (team is null || input.ProjectId.HasValue && input.ProjectId != team.ProjectId)
            throw new ForbiddenException("Chỉ trưởng nhóm hiện tại được kiểm tra đề tài trong phạm vi nhóm của mình.");
        var configuration = await db.Set<TeamAcademicConfiguration>().AsNoTracking()
            .Where(s => s.TeamId == input.TeamId).Select(s => new { s.LeadDepartmentId, s.ConcurrencyToken }).SingleOrDefaultAsync(ct);
        var departmentId = configuration?.LeadDepartmentId ?? actor.DepartmentId.Value;
        if (!await repository.IsActiveDepartmentAsync(departmentId, actor.OrganizationId.Value, ct))
            throw new ForbiddenException("Phạm vi khoa chủ trì của nhóm không còn hợp lệ.");
        return new(userId, actor.OrganizationId.Value, departmentId, team.ProjectId, team.TopicId, configuration?.ConcurrencyToken);
    }

    private static string[] Keywords(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private sealed record ReviewScope(long ActorId, long OrganizationId, long DepartmentId,
        long? ProjectId, long? TopicId, Guid? ScopeVersion);
}
