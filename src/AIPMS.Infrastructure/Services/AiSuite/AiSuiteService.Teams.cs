using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AiSuite;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.AiSuite;

internal sealed partial class AiSuiteService
{
    public async Task<TeamSkillGapDto> AnalyzeTeamSkillGapAsync(long teamId, TeamSkillGapRequest request, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var actor = RequireActor();

        var team = await db.Teams.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == teamId, ct)
            ?? throw new NotFoundException("Team", teamId);

        var isMember = await db.TeamMembers.AsNoTracking()
            .AnyAsync(m => m.TeamId == teamId && m.UserId == actor && m.LeftAt == null, ct);

        var teamProject = await db.Projects.AsNoTracking()
            .Where(p => p.TeamId == teamId)
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync(ct);

        var canSee = IsStaffOrAdmin || isMember
            || (teamProject is not null && await projectAccess.CanAccessAsync(actor, teamProject.Id, ct));
        if (!canSee) throw new ForbiddenException("Bạn không có quyền phân tích năng lực nhóm này.");

        var actorName = await db.Users.AsNoTracking().Where(u => u.Id == actor).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? $"Người dùng #{actor}";

        var members = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == teamId && m.LeftAt == null)
            .Select(m => new
            {
                m.UserId,
                m.User.FullName,
                m.IsLeader,
                MajorName = m.User.Major != null ? m.User.Major.Name : null,
                MajorCode = m.User.Major != null ? m.User.Major.Code : null
            })
            .ToListAsync(ct);

        var memberIds = members.Select(m => m.UserId).ToArray();
        var qualifications = await db.Set<StudentQualification>().AsNoTracking()
            .Where(q => memberIds.Contains(q.UserId))
            .Select(q => new { q.UserId, q.QualificationType, q.TrainingStatus, q.VerificationStatus })
            .ToListAsync(ct);

        var academicConfig = await db.Set<TeamAcademicConfiguration>().AsNoTracking()
            .Include(c => c.Requirements)
            .SingleOrDefaultAsync(c => c.TeamId == teamId, ct);

        var majorMap = await db.Majors.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        string topicContext = Clip(request.CustomTopicDescription, 1200);
        if (request.TopicId is long topicId && topicId > 0)
        {
            var topic = await db.Set<ProjectTopic>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == topicId, ct);
            if (topic is not null) topicContext = $"{topic.Title}\n{Clip(topic.Description, 1000)}";
        }
        else if (teamProject is not null && string.IsNullOrWhiteSpace(topicContext))
        {
            topicContext = $"{teamProject.Title}\n{Clip(teamProject.Description, 1000)}";
        }

        var projectMode = academicConfig?.ProjectMode ?? (members.Select(m => m.MajorCode).Where(x => x != null).Distinct().Count() > 1 ? "INTERDISCIPLINARY" : "SINGLE_MAJOR");

        var systemPrompt = AiSuiteIdentity + " " +
            "[AI_SUITE_TEAM_SKILL_GAP] Đánh giá độ phủ kỹ năng, cơ cấu chuyên ngành (đặc biệt với đồ án liên ngành) và chứng chỉ sẵn sàng của nhóm sinh viên so với đề tài. " +
            "Chỉ ra mảng kỹ năng đã phủ tốt (COVERED), còn yếu (WEAK), hoặc đang thiếu (MISSING); đề xuất vai trò tối ưu cho từng thành viên và lộ trình bù đắp lỗ hổng. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"readinessScore\":number,\"summary\":string,\"coverageMatrix\":[{\"skillOrRole\":string,\"status\":\"COVERED\"|\"WEAK\"|\"MISSING\",\"coveredByMembers\":string[],\"note\":string}],\"roleSuggestions\":[{\"userId\":number,\"suggestedRole\":string,\"strengths\":string[]}],\"onboardingRecommendations\":string[]}.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            teamCode = team.Code,
            teamName = team.Name,
            projectMode,
            topicContext,
            majorRequirements = academicConfig?.Requirements.Select(r => new
            {
                major = majorMap.GetValueOrDefault(r.MajorId, $"Ngành #{r.MajorId}"),
                r.MinMembers,
                r.MaxMembers,
                r.Responsibility
            }).ToArray() ?? [],
            members = members.Select(m => new
            {
                m.UserId,
                m.FullName,
                Role = m.IsLeader ? "Trưởng nhóm" : "Thành viên",
                m.MajorName,
                qualifications = qualifications.Where(q => q.UserId == m.UserId).Select(q => new { q.QualificationType, q.TrainingStatus, q.VerificationStatus }).ToArray()
            }).ToArray()
        }, ct);

        var status = "COMPLETED";
        int readinessScore = 75;
        string summary = $"Đánh giá cấu trúc năng lực của nhóm {team.Name} ({members.Count} thành viên, mô hình {projectMode}).";
        var matrix = new List<SkillCoverageItemDto>();
        var roleSuggestions = new List<MemberRoleSuggestionDto>();
        string[] onboarding = [];

        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            readinessScore = ReadInt(r, "readinessScore", 75, 0, 100);
            summary = ReadString(r, "summary", summary, 1800);
            onboarding = ReadStringArray(r, "onboardingRecommendations", 6, 300);

            if (r.TryGetProperty("coverageMatrix", out var cArr) && cArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in cArr.EnumerateArray().Take(10))
                {
                    var skill = ReadString(c, "skillOrRole", "", 180);
                    if (string.IsNullOrWhiteSpace(skill)) continue;
                    var st = ReadString(c, "status", "WEAK", 12).ToUpperInvariant();
                    if (st is not ("COVERED" or "WEAK" or "MISSING")) st = "WEAK";
                    matrix.Add(new SkillCoverageItemDto(skill, st, ReadStringArray(c, "coveredByMembers", 8, 120), ReadString(c, "note", "", 350)));
                }
            }

            var memberById = members.ToDictionary(m => m.UserId);
            if (r.TryGetProperty("roleSuggestions", out var rArr) && rArr.ValueKind == JsonValueKind.Array)
            {
                var seen = new HashSet<long>();
                foreach (var rs in rArr.EnumerateArray())
                {
                    if (!rs.TryGetProperty("userId", out var uidProp) || !uidProp.TryGetInt64(out var uid)) continue;
                    if (!memberById.TryGetValue(uid, out var m) || !seen.Add(uid)) continue;
                    roleSuggestions.Add(new MemberRoleSuggestionDto(uid, m.FullName, m.MajorName, ReadString(rs, "suggestedRole", "Thành viên triển khai", 140), ReadStringArray(rs, "strengths", 4, 160)));
                }
            }
        }
        else
        {
            status = "FALLBACK";
        }

        var snapPayload = new { projectMode, readinessScore, summary, matrix, roleSuggestions, onboarding };
        var meta = await SaveSnapshotAsync("TEAM_SKILL_GAP", "TEAM", teamId, teamProject?.Id, null, status, $"Đánh giá kỹ năng · {team.Name}", snapPayload, actor, actorName, ct);
        return new TeamSkillGapDto(teamId, team.Name, projectMode, readinessScore, summary, matrix, roleSuggestions, onboarding, meta);
    }

    public async Task<WorkingAgreementDraftDto> DraftWorkingAgreementAsync(long projectId, WorkingAgreementDraftRequest request, CancellationToken ct)
    {
        if (aiProvider is null) throw new ServiceUnavailableException("Dịch vụ AI hiện chưa sẵn sàng.", "AI_UNAVAILABLE");
        var (project, actor, actorName) = await RequireProjectAccessAsync(projectId, ct);

        var members = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == project.TeamId && m.LeftAt == null)
            .Select(m => new { m.User.FullName, Role = m.IsLeader ? "Trưởng nhóm" : "Thành viên", Major = m.User.Major != null ? m.User.Major.Name : null })
            .ToListAsync(ct);

        var academicConfig = await db.Set<TeamAcademicConfiguration>().AsNoTracking()
            .Include(c => c.Requirements)
            .SingleOrDefaultAsync(c => c.TeamId == project.TeamId, ct);

        var systemPrompt = AiSuiteIdentity + " " +
            "[AI_SUITE_WORKING_AGREEMENT] Soạn bản nháp Thỏa thuận làm việc nhóm (Working Agreement) thực tế, chặt chẽ, phù hợp đúng cơ cấu thành viên & chuyên ngành của đồ án. " +
            "Trả ONLY raw JSON (không bọc code fence): " +
            "{\"summary\":string,\"sections\":[{\"title\":string,\"body\":string}]}. " +
            "Tạo 5–6 mục rõ ràng: Mục tiêu & Kỷ luật, Lịch họp & Kênh giao tiếp, Phân công & Review chéo, Quản lý Task & Deadline, Quy tắc xử lý trễ hạn/xung đột, Cam kết liêm chính học thuật.";

        var raw = await CallAiAsync(systemPrompt, new
        {
            projectCode = project.Code,
            projectTitle = project.Title,
            projectDescription = Clip(project.Description, 900),
            projectMode = academicConfig?.ProjectMode ?? "SINGLE_MAJOR",
            members,
            teamPreferences = Clip(request.TeamPreferences, 600)
        }, ct);

        var status = "COMPLETED";
        var summary = $"Bản nháp Thỏa thuận làm việc nhóm cho đồ án {project.Title}.";
        var sections = new List<WorkingAgreementSectionDraftDto>();

        using var doc = TryParseJsonObject(raw);
        if (doc is not null)
        {
            var r = doc.RootElement;
            summary = ReadString(r, "summary", summary, 1000);
            if (r.TryGetProperty("sections", out var sArr) && sArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in sArr.EnumerateArray().Take(10))
                {
                    var st = ReadString(s, "title", "", 200);
                    var sb = ReadString(s, "body", "", 2500);
                    if (!string.IsNullOrWhiteSpace(st) && !string.IsNullOrWhiteSpace(sb))
                        sections.Add(new WorkingAgreementSectionDraftDto(st, sb));
                }
            }
        }
        else
        {
            status = "FALLBACK";
        }

        var snapPayload = new { summary, sections };
        var meta = await SaveSnapshotAsync("WORKING_AGREEMENT_DRAFT", "PROJECT", projectId, projectId, null, status, $"Nháp thỏa thuận nhóm · {project.Code}", snapPayload, actor, actorName, ct);
        return new WorkingAgreementDraftDto(projectId, summary, sections, meta);
    }
}
