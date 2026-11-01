using AIPMS.Application.Features.Projects.DTOs;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed record ProgressRiskCounts(int Tasks, int Milestones, int Overdue, int Blocked, int LateMilestones,
    int Stale, int MissingDeliverables, int RepeatedReturns, int MissedMeetings, int MissingDeadlines);

internal static class ProgressRiskRules
{
    internal static string Level(int score) => score < 25 ? "LOW" : score < 50 ? "MEDIUM" : score < 75 ? "HIGH" : "CRITICAL";

    internal static ProjectProgressAnalysisDto Apply(ProjectProgressAnalysisDto result, ProgressRiskCounts c)
    {
        var signals = new List<ProgressRiskSignalDto>();
        var recommendations = new List<string>();
        var score = 0;
        void Add(string code, int count, int weight, string description, string advice)
        {
            if (count <= 0) return;
            score += weight;
            signals.Add(new(code, weight >= 20 ? "HIGH" : "MEDIUM", count, description));
            recommendations.Add(advice);
        }
        Add("OVERDUE_TASKS", c.Overdue, 20, $"Có {c.Overdue} công việc quá hạn.", "Ưu tiên xử lý công việc quá hạn và xác nhận người phụ trách.");
        Add("BLOCKED_TASKS", c.Blocked, 20, $"Có {c.Blocked} công việc bị vướng mắc.", "Trao đổi với Primary/Mentor để gỡ vướng.");
        Add("LATE_MILESTONE_OR_SPRINT", c.LateMilestones, 20, $"Có {c.LateMilestones} mốc hoặc sprint đã trễ.", "Rà soát đầu ra còn thiếu của mốc hoặc sprint bị trễ.");
        Add("STALE_TASKS", c.Stale, 15, $"Có {c.Stale} công việc chưa cập nhật quá 7 ngày.", "Cập nhật tiến độ thực tế và trở ngại của công việc.");
        Add("DELIVERABLE_WITHOUT_VERSION", c.MissingDeliverables, 20, $"Có {c.MissingDeliverables} hạng mục đến hạn trong 7 ngày hoặc đã quá hạn nhưng chưa có phiên bản.", "Chuẩn bị phiên bản đầu tiên cho hạng mục sắp đến hạn.");
        Add("REPEATED_RETURNS", c.RepeatedReturns, 15, $"Có {c.RepeatedReturns} công việc bị trả lại ít nhất 2 lần.", "Đối chiếu yêu cầu và phản hồi trước khi gửi duyệt lại.");
        Add("MISSED_MEETINGS", c.MissedMeetings, 5, $"Có {c.MissedMeetings} cuộc họp ghi nhận vắng mặt trong 30 ngày.", "Xem biên bản và xác nhận việc cần thực hiện sau cuộc họp.");
        var enough = c.Tasks + c.Milestones > 0;
        var limited = c.MissingDeadlines > 0 ? $" Có {c.MissingDeadlines} công việc chưa có hạn; chưa thể xác định trễ hạn cho các việc này." : "";
        return result with {
            DataStatus = enough ? "SUFFICIENT" : "INSUFFICIENT_DATA", RiskScore = enough ? Math.Min(100, score) : null,
            RiskLevel = enough ? Level(Math.Min(100, score)) : "INSUFFICIENT_DATA",
            Confidence = enough ? Math.Round(Math.Max(.3, .9 - (c.Tasks == 0 ? .2 : .4 * c.MissingDeadlines / c.Tasks)), 2) : null,
            Signals = signals, Factors = signals.Select(s => new RiskFactorDto(s.Code, s.Code, s.Count, s.Severity, s.Description)).ToArray(),
            Recommendations = recommendations.Count > 0 ? recommendations : ["Duy trì cập nhật công việc và kiểm tra các hạn nộp."],
            Summary = enough ? signals.Count == 0 ? "Chưa ghi nhận tín hiệu chậm tiến độ trong dữ liệu hiện có." :
                $"Ghi nhận {signals.Count} nhóm tín hiệu cần chú ý." : "Chưa có công việc hoặc mốc tiến độ để dự báo.",
            RuleVersion = "PROGRESS_RISK_MVP_V1", FeatureVersion = "PROGRESS_RISK_V1", ModelVersion = "RULE_BASED",
            Limitations = "Đây là quy tắc cảnh báo, độ tin cậy phản ánh độ đầy đủ dữ liệu, không phải xác suất dự đoán đã kiểm định." + limited
        };
    }
}
