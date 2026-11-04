using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.Models;
using AIPMS.Application.Features.ExecutionCapabilities.Abstractions;
using AIPMS.Application.Features.FinalSubmissions.Services;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class WorkflowAssistanceService(AipmsDbContext db, AiActorScope access, IAiContextRetriever context,
    IProjectCapabilityService capabilities, FinalSubmissionWorkflow submissions, IAuditTrail audit, TimeProvider clock)
    : IWorkflowAssistanceService
{
    private static readonly string[] Modes = ["NEXT_STEPS", "BLOCKERS", "SUBMISSION_CHECK", "MEETING_PREP",
        "MEETING_FOLLOWUP", "CONSISTENCY", "COORDINATION", "SUPERVISOR_BRIEF"];
    private static string Clip(string? s, int max = 500) => string.IsNullOrWhiteSpace(s) ? "" : s.Length > max ? s[..max] + "…" : s;
    private static bool Open(string status) => status is not ("DONE" or "COMPLETED" or "CANCELLED");

    public async Task<WorkflowAssistanceDto> GetAsync(long projectId, string mode, long? meetingId, CancellationToken ct)
    {
        if (!Modes.Contains(mode) || projectId <= 0 || (meetingId.HasValue && (meetingId <= 0 || mode != "MEETING_FOLLOWUP")))
            throw new ArgumentException("Chọn nhu cầu hỗ trợ và cuộc họp hợp lệ.");
        var scope = await access.Read(projectId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var items = new List<WorkflowAssistanceItem>();
        var limits = new List<string> { "Ảnh chụp dữ liệu tại thời điểm kiểm tra. Quyền và điều kiện được kiểm tra lại khi lưu/nộp; chưa có dữ liệu nghiệp vụ nào bị thay đổi." };
        if (!scope.Whole) limits.Add("Chỉ tổng hợp công việc thuộc ngành được phân công; không đại diện toàn bộ đồ án.");
        string status = "CHECKED", summary = "Giai đoạn: " + WorkflowAssistanceLabels.Stage(scope.Status) + ".";
        AssistantDraftDto? draft = null;
        IReadOnlyList<WorkflowMeetingOption>? meetings = null;

        if (mode is "BLOCKERS" or "NEXT_STEPS")
        {
            if (scope.Whole)
            {
                var caps = await capabilities.GetAsync(projectId, ct);
                if (caps.WorkingAgreement is { Required: true } agreement)
                    items.Add(new("WORKING_AGREEMENT", agreement.Status == "READY" ? "READY" : "BLOCKED", "Cam kết nhóm bắt buộc",
                        agreement.Status == "READY" ? "Các thành viên đã đáp ứng yêu cầu cam kết hiện hành."
                            : string.Join(" ", agreement.Reasons.Select(WorkflowAssistanceLabels.Reason).Distinct()),
                        "Trưởng nhóm chuẩn bị; từng thành viên xác nhận", "Chính sách kỳ và cam kết hiện hành", "agreement"));
                foreach (var action in caps.Actions)
                {
                    if (WorkflowAssistanceLabels.Action(action.Code) is not { } label) continue;
                    if (mode == "NEXT_STEPS" && !action.Allowed) continue;
                    items.Add(new(action.Code, action.Allowed ? "AVAILABLE" : "BLOCKED", label.Title,
                        action.Allowed ? "Tài khoản hiện tại có thể mở thao tác. Các điều kiện của từng bản ghi vẫn được kiểm tra tại biểu mẫu."
                            : string.Join(" ", action.Reasons.Select(WorkflowAssistanceLabels.Reason).Distinct()),
                        label.Owner, "Quyền thao tác hiện hành của backend", label.Target));
                }
            }
            else limits.Add("Không kết luận quyền nghiệp vụ toàn đồ án từ phân công theo ngành. Kiểm tra quyền tại trang thao tác.");
        }

        if (mode == "SUBMISSION_CHECK")
        {
            try
            {
                var check = await submissions.Checklist(projectId, ct);
                status = check.CanSubmit ? "READY" : "BLOCKED";
                summary = check.CanSubmit ? "Checklist bàn giao hiện tại đủ điều kiện. Bạn vẫn cần kiểm tra gói và tự bấm nộp."
                    : "Gói bàn giao chưa đủ điều kiện nộp.";
                foreach (var reason in check.Blockers.Select(WorkflowAssistanceLabels.Reason).Distinct())
                    items.Add(new("SUBMISSION_BLOCKER", "BLOCKED", "Điều kiện bàn giao", reason, "Trưởng nhóm; Khoa xử lý cấu hình kỳ/yêu cầu", "Checklist bàn giao chính thức", "final-submission"));
                foreach (var item in check.Items)
                    items.Add(new("REQUIRED_DELIVERABLE", item.IsComplete ? "READY" : "MISSING", item.Title,
                        item.IsComplete ? "Đã chọn phiên bản và tệp hợp lệ trong gói." : "Chưa đủ phiên bản/tệp hợp lệ trong gói bàn giao.",
                        "Nhóm thực hiện, trưởng nhóm kiểm tra gói", "Hạng mục bắt buộc do Khoa cấu hình", "deliverables"));
                if (check.Deadline.HasValue) limits.Add("Hạn bàn giao (giờ Việt Nam): " + check.Deadline.Value.AddHours(7).ToString("dd/MM/yyyy HH:mm") + ".");
                limits.Add("Đây là checklist bàn giao cuối kỳ; không thay thế checklist từng báo cáo hoặc review gate.");
            }
            catch (ForbiddenException)
            {
                status = "INSUFFICIENT_DATA";
                summary = "Tài khoản này không được xem checklist gói bàn giao của sinh viên. Nhờ thành viên nhóm kiểm tra trong trang bàn giao.";
            }
        }
        else if (mode != "BLOCKERS")
        {
            var tasksQuery = access.Tasks(scope);
            var total = await tasksQuery.CountAsync(ct);
            var tasks = await tasksQuery.OrderByDescending(t => t.Status == "BLOCKED")
                .ThenBy(t => t.DueAt == null).ThenBy(t => t.DueAt).ThenBy(t => t.Id).Take(100)
                .Select(t => new { t.Id, t.Title, t.Status, t.DueAt, t.UpdatedAt,
                    HasFiles = t.Files.Any(),
                    Owners = t.TaskAssignees.Select(a => a.User.FullName).ToArray() }).ToArrayAsync(ct);
            limits.Add($"Đã kiểm tra {tasks.Length}/{total} công việc trong phạm vi được phép. Dữ liệu chưa cập nhật không phản ánh chắc chắn khối lượng thực tế.");
            var open = tasks.Where(t => Open(t.Status)).ToArray();
            var attention = open.OrderByDescending(t => t.DueAt < now).ThenByDescending(t => t.Status == "BLOCKED").ThenBy(t => t.DueAt ?? DateTime.MaxValue).Take(5).ToArray();
            if (mode is "NEXT_STEPS" or "MEETING_PREP" or "SUPERVISOR_BRIEF")
            {
                var priorities = attention.Select(t => new WorkflowAssistanceItem("TASK_ATTENTION", "REVIEW", t.Title,
                    (t.DueAt < now ? "Đã quá hạn. " : "") + (t.Status == "BLOCKED" ? "Đang bị vướng mắc. " : "Công việc chưa hoàn thành. ")
                    + (t.DueAt.HasValue ? "Hạn: " + t.DueAt.Value.AddHours(7).ToString("dd/MM/yyyy HH:mm") + " (giờ Việt Nam)." : "Chưa đặt hạn."),
                    t.Owners.Length > 0 ? string.Join(", ", t.Owners) : "Chưa phân công; trưởng nhóm/GVHD cần xác nhận", "Công việc hiện hành", "tasks", t.Id));
                items.AddRange(priorities);
                if (mode == "NEXT_STEPS") items = items.OrderBy(i => i.Status == "BLOCKED" ? 0 : i.Code == "TASK_ATTENTION" ? 1 : i.Status == "AVAILABLE" ? 2 : 3).ToList();
                if (mode == "NEXT_STEPS") items = items.Take(5).ToList();
            }
            if (mode == "CONSISTENCY")
            {
                foreach (var t in tasks.Where(t => t.Status is "DONE" or "COMPLETED" && !t.HasFiles).Take(15))
                    items.Add(new("DONE_WITHOUT_FILE", "REVIEW", t.Title, "Đã đánh dấu hoàn thành nhưng chưa có tệp đính kèm trực tiếp. Kiểm tra minh chứng ở nơi khác trước khi kết luận; không phải mọi task đều bắt buộc có file.",
                        "Người phụ trách và GVHD", "Trạng thái task và file đính kèm", "tasks", t.Id));
            }
            if (mode is "CONSISTENCY" or "MEETING_PREP" or "MEETING_FOLLOWUP" or "SUPERVISOR_BRIEF")
            {
                var bounded = await context.RetrieveProjectContextAsync(projectId, "workflow assistance", ct);
                var payload = JsonSerializer.Deserialize<ProjectEvidencePayload>(bounded.FormattedEvidenceText)!;
                limits.Add("Báo cáo: tối đa 5 bản gần nhất được phép xem. Cuộc họp: tối đa 5 cuộc gần nhất có bạn tham gia; dùng trang cuộc họp để mở các bản cũ hơn.");
                if (mode == "CONSISTENCY")
                    foreach (var report in payload.ProgressReports)
                        foreach (var t in open.Where(t => t.Title.Length >= 8 && (report.CompletedWork ?? "").Contains(t.Title, StringComparison.OrdinalIgnoreCase)).Take(10))
                            items.Add(new("REPORT_TASK_REVIEW", "REVIEW", "Đối chiếu: " + t.Title,
                                "Mục đã hoàn thành của báo cáo nhắc nguyên tên công việc, nhưng task vẫn đang mở. Có thể khác phạm vi hoặc chưa cập nhật; cần người phụ trách xác nhận.", "Người viết báo cáo và phụ trách task", "Báo cáo tiến độ · " + report.Period, "reports", report.ReportId));
                if (mode is "MEETING_PREP" or "SUPERVISOR_BRIEF")
                    foreach (var r in payload.ProgressReports.Where(r => !string.IsNullOrWhiteSpace(r.Blockers) || !string.IsNullOrWhiteSpace(r.Risks)).Take(3))
                        items.Add(new("REPORT_SUPPORT", "REVIEW", "Nội dung cần trao đổi · " + r.Period,
                            "Vướng mắc đã ghi: " + Clip(r.Blockers) + "\nRủi ro đã ghi: " + Clip(r.Risks), "Người viết báo cáo và GVHD", "Báo cáo tiến độ", "reports", r.ReportId));
                if (mode == "MEETING_FOLLOWUP")
                {
                    meetings = payload.Meetings.Select(m => new WorkflowMeetingOption(m.MeetingId, m.Title, DateTime.Parse(m.StartAt, System.Globalization.CultureInfo.InvariantCulture))).ToArray();
                    if (!meetingId.HasValue) { status = "NEEDS_SELECTION"; summary = "Chọn cuộc họp để xem việc cần theo dõi từ biên bản."; }
                    else
                    {
                        var m = payload.Meetings.SingleOrDefault(m => m.MeetingId == meetingId) ?? throw new NotFoundException("Accessible meeting", meetingId.Value);
                        var full = await db.Meetings.AsNoTracking().Where(x => x.Id == m.MeetingId && x.ProjectId == projectId
                            && x.MeetingParticipants.Any(p => p.UserId == scope.Actor))
                            .Select(x => new { x.MeetingNotes, x.Minutes, x.Decisions, x.Blockers }).SingleOrDefaultAsync(ct)
                            ?? throw new NotFoundException("Accessible meeting", m.MeetingId);
                        var notes = Clip(string.Join("\n", new[] { full.MeetingNotes, full.Minutes, full.Decisions, full.Blockers }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct()), 6000);
                        if (string.IsNullOrWhiteSpace(notes)) { status = "INSUFFICIENT_DATA"; summary = "Cuộc họp chưa có biên bản/quyết định để lập việc theo dõi."; }
                        else
                        {
                            items.Add(new("MEETING_NOTES", "REVIEW", m.Title, notes, "Người tổ chức và thành viên tham gia", "Biên bản cuộc họp được chọn", "meetings", m.MeetingId));
                            var children = notes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().Take(8)
                                .Select(line => new AssistantDraftDto("TASK", Clip("Xác nhận và thực hiện: " + line.TrimStart('-', '*', ' '), 180),
                                    "Đề xuất cần kiểm tra từ cuộc họp “" + m.Title + "”.\nGhi chú nguồn: " + Clip(line, 2500) + "\n[Cần xác nhận] Đây có phải đầu việc cần làm? Bổ sung kết quả, người phụ trách, mốc và hạn; kiểm tra trùng với task hiện có trước khi tạo.")).ToArray();
                            draft = new("WORK_PLAN", "Việc đề xuất sau cuộc họp", "Các dòng biên bản là gợi ý để rà soát, chưa phải phân công chính thức.", Tasks: children);
                            limits.Add("Đọc tối đa 6.000 ký tự biên bản, tạo tối đa 8 gợi ý từ các dòng đầu. Có thể có dòng chỉ là thông tin; không tự suy người/hạn hoặc chứng minh task đã/chưa được tạo.");
                        }
                    }
                }
            }
            if (mode == "COORDINATION")
            {
                var visible = tasksQuery.Select(t => t.Id);
                var deps = await db.TaskDependencies.AsNoTracking().Where(d => visible.Contains(d.TaskId) && visible.Contains(d.DependsOnTaskId)
                        && d.Task.Status != "DONE" && d.Task.Status != "COMPLETED" && d.Task.Status != "CANCELLED"
                        && d.DependsOnTask.Status != "DONE" && d.DependsOnTask.Status != "COMPLETED" && d.DependsOnTask.Status != "CANCELLED")
                    .OrderBy(d => d.Id).Take(101).Select(d => new { d.TaskId, d.DependsOnTaskId, d.Task.Title, DependencyTitle = d.DependsOnTask.Title }).ToArrayAsync(ct);
                var ids = deps.Take(100).SelectMany(d => new[] { d.TaskId, d.DependsOnTaskId }).Distinct().ToArray();
                var majors = await (from d in db.TaskDisciplines.AsNoTracking() join m in db.Majors on d.MajorId equals m.Id
                    where ids.Contains(d.TaskId) select new { d.TaskId, m.Name }).ToArrayAsync(ct);
                foreach (var d in deps.Take(100))
                {
                    string Names(long id) => string.Join(", ", majors.Where(m => m.TaskId == id).Select(m => m.Name).Distinct());
                    items.Add(new("DEPENDENCY", "REVIEW", d.Title, "Có phụ thuộc đã khai báo với “" + d.DependencyTitle + "” chưa hoàn thành. Ngành của việc này: "
                        + (Names(d.TaskId) is { Length: > 0 } a ? a : "chưa gắn") + "; ngành của việc phụ thuộc: " + (Names(d.DependsOnTaskId) is { Length: > 0 } b ? b : "chưa gắn")
                        + ". Xác nhận loại phụ thuộc và đầu ra cần phối hợp tại task.", "Người phụ trách hai công việc và trưởng nhóm", "Quan hệ phụ thuộc task", "tasks", d.TaskId));
                }
                limits.Add(deps.Length > 100 ? "Chỉ hiển thị 100 quan hệ đầu tiên." : "Chỉ đọc dependency đã khai báo và cả hai task đều thuộc quyền xem. Không suy ra quan hệ giữa các ngành.");
            }
        }
        if (items.Count == 0 && status == "CHECKED") { status = "INSUFFICIENT_DATA"; summary += " Chưa có mục cần hiển thị trong dữ liệu đã kiểm tra; điều này không xác nhận đồ án đã hoàn tất."; }
        if (mode == "MEETING_PREP")
        {
            var agenda = "Nội dung đề xuất để trao đổi, chưa phải kết luận:\n" + Clip(string.Join("\n", items.Select(i => "- " + i.Title + ": " + i.Detail)), 3300)
                + "\n- Câu hỏi với GVHD: cần ưu tiên gì, tháo gỡ vướng mắc nào, minh chứng tiếp theo là gì?\n- [Cần bổ sung] Thời gian, thành phần tham gia và mục tiêu buổi họp.";
            draft = new("MEETING", "Trao đổi tiến độ và vướng mắc", agenda);
        }
        if (mode == "SUPERVISOR_BRIEF") limits.Add("Tổng hợp riêng đồ án đang mở, không phải xếp hạng các nhóm toàn khoa.");
        if ((await access.Read(projectId, ct)).Hash != scope.Hash) throw new ForbiddenException("Phân công đã thay đổi; tải lại trước khi xem.");
        await audit.RecordAsync(new(scope.Actor, "AI_WORKFLOW_ASSISTANCE_READ", "PROJECT", projectId,
            new Dictionary<string, object?> { ["mode"] = mode, ["itemCount"] = items.Count }), ct);
        return new(projectId, mode, now, status, summary, items, limits, draft, meetings);
    }
}
