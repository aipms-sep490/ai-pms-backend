using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services;

internal sealed partial class AcademicKnowledgeService
{
    public async Task<IReadOnlyList<AssistantRubricOption>> ReviewRubricsAsync(long projectId, CancellationToken ct)
    {
        var actor = await ActiveUserAsync(ct);
        await projectContext.RetrieveProjectContextAsync(projectId, "rubric", ct);
        return await RubricOptionsAsync(projectId, actor.DepartmentId, ct);
    }

    private async Task<AssistantRubricOption[]> RubricOptionsAsync(long projectId, long? departmentId, CancellationToken ct)
    {
        var semester = await db.Projects.Where(p => p.Id == projectId).Select(p => p.Team.AcademicSemesterId).SingleAsync(ct);
        var bound = from scheme in db.Set<EvaluationScheme>()
                    join component in db.Set<EvaluationSchemeComponent>() on scheme.Id equals component.SchemeId
                    where scheme.ProjectId == projectId && scheme.Status == "PUBLISHED"
                    select component.RubricId;
        // Each viewer selects only their department's published version for this semester.
        return await (from rubric in db.Rubrics.AsNoTracking()
                      join version in db.Set<RubricVersion>().AsNoTracking() on rubric.Id equals version.RubricId
                      where rubric.DepartmentId == departmentId && departmentId != null && rubric.AcademicSemesterId == semester
                          && rubric.IsActive && version.Status == "PUBLISHED"
                      orderby rubric.Name, version.VersionNumber descending, rubric.Id
                      select new AssistantRubricOption(rubric.Id, rubric.Name, version.VersionNumber, bound.Contains(rubric.Id)))
                      .Take(100).ToArrayAsync(ct);
    }

    private async Task<EvidenceReferenceDto[]> ReviewEvidenceAsync(long projectId, long rubricId, long? departmentId, CancellationToken ct)
    {
        var rubric = (await RubricOptionsAsync(projectId, departmentId, ct)).SingleOrDefault(x => x.Id == rubricId)
            ?? throw new NotFoundException("Published rubric in your department and project semester", rubricId);
        var criteria = await db.RubricCriteria.AsNoTracking().Where(c => c.RubricId == rubricId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.ParentId, c.Criterion.Name, c.Criterion.Description, c.WeightPercent, c.IsRequired })
            .Take(101).ToArrayAsync(ct);
        if (criteria.Length is 0 or > 100) throw new ConflictException("Rubric cần có từ 1 đến 100 tiêu chí để nhận xét báo cáo.");
        return criteria.Select(c => new EvidenceReferenceDto("RUBRIC", $"RUBRIC-{rubricId}-{c.Id}",
            $"{rubric.Title} · v{rubric.Version} · {c.Name}", null, null,
            $"{(c.ParentId.HasValue ? "Tiêu chí phụ của " + criteria.FirstOrDefault(p => p.Id == c.ParentId)?.Name : "Tiêu chí chính")}; " +
            $"Trọng số {c.WeightPercent}%; {(c.IsRequired ? "Bắt buộc" : "Không bắt buộc")}. {c.Description}. " +
            (rubric.AssignedToProject ? "Thuộc phương án đã công bố của đồ án." : "Bộ tiêu chí tham khảo đã công bố; chưa gắn vào phương án đồ án."))).ToArray();
    }

    internal static string ProgressSummary(ProjectBoundedContext context)
    {
        var status = context.ProjectStatus switch { "ACTIVE" => "Đang thực hiện", "FINAL_SUBMISSION" => "Bàn giao cuối kỳ", "COMPLETED" => "Đã hoàn thành", "ARCHIVED" => "Đã lưu trữ", _ => "Trạng thái " + context.ProjectStatus };
        var result = $"## Tiến độ đồ án\nGiai đoạn: {status}.\nĐã truy hồi {context.RetrievedTasks}/{context.TotalTasks} công việc. " +
            (context.TasksTruncated ? "Đây là một phần dữ liệu, không phải toàn bộ tiến độ nhóm.\n" : "\n");
        var payload = JsonSerializer.Deserialize<ProjectEvidencePayload>(context.FormattedEvidenceText);
        if (payload is null) return result + "Chưa có dữ liệu chi tiết để tổng hợp.";
        var actionable = payload.Tasks.Where(t => t.Status is not ("DONE" or "CANCELLED"))
            .OrderByDescending(t => t.IsOverdue).ThenByDescending(t => t.IsBlocked).ThenBy(t => t.DueAt ?? "9999").Take(10).ToArray();
        result += "\n## Việc cần chú ý\n" + (actionable.Length == 0 ? "Không có việc đang mở trong phần dữ liệu đã truy hồi.\n" :
            string.Join("\n", actionable.Select(t => $"- {t.Title}: {(t.IsOverdue ? "quá hạn" : t.IsBlocked ? "đang vướng mắc" : "đang mở")}; hạn {t.DueAt ?? "chưa đặt"} (UTC).")) + "\n");
        result += "\n## Mốc tiến độ\n" + string.Join("\n", payload.Milestones.Take(10).Select(m => $"- {m.Title}: {m.Status}; hạn {m.DueDate ?? "chưa đặt"}."));
        return result + "\n\nGợi ý: kiểm tra việc quá hạn/vướng mắc trước, xác nhận người phụ trách và cập nhật trạng thái sau khi hoàn tất. Đây là gợi ý, không tự thay đổi dữ liệu.";
    }
}
