using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.AI.Configuration;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.Models;

namespace AIPMS.AI.Providers;

public sealed class GroundedAiTextGenerationProvider(AiAssistantOptions? options = null) : IAiTextGenerationProvider
{
    private readonly AiAssistantOptions _options = options ?? new AiAssistantOptions();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<string> GenerateTextAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        if (_options.SimulateTimeout)
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.TimeoutSeconds + 1), cancellationToken);
            throw new TimeoutException("AI text generation provider timed out.");
        }

        if (_options.SimulateFailure)
        {
            throw new InvalidOperationException("Simulated AI text generation provider failure.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        cts.Token.ThrowIfCancellationRequested();

        // Check if report summary request
        if (systemPrompt.Contains("summarizing an academic project progress report", StringComparison.OrdinalIgnoreCase) ||
            userPrompt.Contains("<report_evidence", StringComparison.OrdinalIgnoreCase) ||
            (!userPrompt.Contains("<user_query_json>", StringComparison.OrdinalIgnoreCase) &&
             !userPrompt.Contains("<user_query>", StringComparison.OrdinalIgnoreCase) &&
             (userPrompt.Contains("\"ReportId\":") || userPrompt.Contains("\"reportId\":"))))
        {
            return GenerateReportSummaryJson(userPrompt);
        }

        // Project Q&A request
        return GenerateProjectAnswer(userPrompt);
    }

    private static string GenerateReportSummaryJson(string userPrompt)
    {
        var evidenceMatch = Regex.Match(userPrompt, @"<evidence_json>(.*?)</evidence_json>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!evidenceMatch.Success)
        {
            evidenceMatch = Regex.Match(userPrompt, @"<evidence_payload>(.*?)</evidence_payload>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }
        var evidenceText = evidenceMatch.Success ? evidenceMatch.Groups[1].Value.Trim() : userPrompt.Trim();

        ReportEvidencePayload? reportPayload = null;
        try
        {
            reportPayload = JsonSerializer.Deserialize<ReportEvidencePayload>(evidenceText, JsonOptions);
        }
        catch
        {
            // fallback if XML or unparseable
        }

        var summary = reportPayload?.Summary ?? ExtractXmlTag(evidenceText, "summary");
        var completed = reportPayload?.CompletedWork ?? ExtractXmlTag(evidenceText, "completed_work");
        var planned = reportPayload?.PlannedWork ?? ExtractXmlTag(evidenceText, "planned_work");
        var issues = reportPayload?.IssuesAndRisks ?? ExtractXmlTag(evidenceText, "issues_and_risks");

        var completedSummary = !string.IsNullOrWhiteSpace(completed)
            ? $"Completed items: {completed}"
            : "No completed work reported for this period.";

        var inProgressSummary = !string.IsNullOrWhiteSpace(planned)
            ? $"Work in progress: {planned}"
            : "No active in-progress work reported.";

        var blockersSummary = !string.IsNullOrWhiteSpace(issues) && (issues.Contains("block", StringComparison.OrdinalIgnoreCase) || issues.Contains("delay", StringComparison.OrdinalIgnoreCase) || issues.Contains("issue", StringComparison.OrdinalIgnoreCase))
            ? $"Identified blockers: {issues}"
            : "No active blockers reported.";

        var risksSummary = !string.IsNullOrWhiteSpace(issues) && (issues.Contains("risk", StringComparison.OrdinalIgnoreCase) || issues.Contains("uncertain", StringComparison.OrdinalIgnoreCase))
            ? $"Identified risks: {issues}"
            : (!string.IsNullOrWhiteSpace(issues) ? $"Potential risks noted: {issues}" : "No specific risks reported.");

        var nextActionsSummary = !string.IsNullOrWhiteSpace(planned)
            ? $"Next actions: {planned}"
            : (!string.IsNullOrWhiteSpace(summary) ? $"Follow-up actions based on summary: {summary}" : "No next actions specified.");

        var payload = new
        {
            completed = completedSummary,
            inProgress = inProgressSummary,
            blockers = blockersSummary,
            risks = risksSummary,
            nextActions = nextActionsSummary
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string GenerateProjectAnswer(string userPrompt)
    {
        // Extract query safely from <user_query_json> or fallback to <user_query>
        var query = string.Empty;
        var queryJsonMatch = Regex.Match(userPrompt, @"<user_query_json>(.*?)</user_query_json>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (queryJsonMatch.Success)
        {
            try
            {
                using var doc = JsonDocument.Parse(queryJsonMatch.Groups[1].Value.Trim());
                if (doc.RootElement.TryGetProperty("query", out var qProp))
                {
                    query = qProp.GetString() ?? string.Empty;
                }
            }
            catch
            {
                query = queryJsonMatch.Groups[1].Value.Trim();
            }
        }
        else
        {
            var queryMatch = Regex.Match(userPrompt, @"<user_query>(.*?)</user_query>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            query = queryMatch.Success ? queryMatch.Groups[1].Value.Trim() : string.Empty;
        }

        var evidenceMatch = Regex.Match(userPrompt, @"<evidence_json>(.*?)</evidence_json>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!evidenceMatch.Success)
        {
            evidenceMatch = Regex.Match(userPrompt, @"<evidence_payload>(.*?)</evidence_payload>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }
        var evidenceText = evidenceMatch.Success ? evidenceMatch.Groups[1].Value.Trim() : string.Empty;

        ProjectEvidencePayload? projectEvidence = null;
        try
        {
            projectEvidence = JsonSerializer.Deserialize<ProjectEvidencePayload>(evidenceText, JsonOptions);
        }
        catch
        {
            // fallback if XML
        }

        int totalTasks;
        int retrievedTasks;
        bool isTasksTruncated;
        IReadOnlyList<MilestoneEvidenceItem> milestones;
        IReadOnlyList<TaskEvidenceItem> tasks;
        IReadOnlyList<ProgressReportEvidenceItem> reports;
        IReadOnlyList<MeetingEvidenceItem> meetings;

        if (projectEvidence != null)
        {
            totalTasks = projectEvidence.TotalTasks;
            retrievedTasks = projectEvidence.RetrievedTasks;
            isTasksTruncated = projectEvidence.TasksTruncated;
            milestones = projectEvidence.Milestones ?? Array.Empty<MilestoneEvidenceItem>();
            tasks = projectEvidence.Tasks ?? Array.Empty<TaskEvidenceItem>();
            reports = projectEvidence.ProgressReports ?? Array.Empty<ProgressReportEvidenceItem>();
            meetings = projectEvidence.Meetings ?? Array.Empty<MeetingEvidenceItem>();
        }
        else
        {
            // Backward-compatible fallback for legacy XML
            var tasksTagMatch = Regex.Match(evidenceText, @"<tasks\s+total_count=""(\d+)""\s+retrieved_count=""(\d+)""\s+truncated=""(true|false)""[^>]*>", RegexOptions.IgnoreCase);
            totalTasks = tasksTagMatch.Success ? int.Parse(tasksTagMatch.Groups[1].Value) : 0;
            retrievedTasks = tasksTagMatch.Success ? int.Parse(tasksTagMatch.Groups[2].Value) : 0;
            isTasksTruncated = tasksTagMatch.Success && bool.Parse(tasksTagMatch.Groups[3].Value);

            var taskMatches = Regex.Matches(evidenceText, @"<task id=""([^""]+)"" title=""([^""]+)"" status=""([^""]+)""[^>]*>(.*?)</task>", RegexOptions.Singleline);
            var milestoneMatches = Regex.Matches(evidenceText, @"<milestone id=""([^""]+)"" title=""([^""]+)"" status=""([^""]+)""[^>]*>(.*?)</milestone>", RegexOptions.Singleline);
            var reportMatches = Regex.Matches(evidenceText, @"<report id=""([^""]+)"" type=""([^""]+)""[^>]*>(.*?)</report>", RegexOptions.Singleline);
            var meetingMatches = Regex.Matches(evidenceText, @"<meeting id=""([^""]+)"" title=""([^""]+)"" status=""([^""]+)""[^>]*>(.*?)</meeting>", RegexOptions.Singleline);

            milestones = milestoneMatches.Cast<Match>().Select(m => new MilestoneEvidenceItem(m.Groups[1].Value, 0, m.Groups[2].Value, m.Groups[3].Value, null, 0, null)).ToList();
            tasks = taskMatches.Cast<Match>().Select(t => new TaskEvidenceItem(t.Groups[1].Value, 0, t.Groups[2].Value, t.Groups[3].Value, null, null, false, false, null)).ToList();
            reports = reportMatches.Cast<Match>().Select(r => new ProgressReportEvidenceItem(r.Groups[1].Value, 0, r.Groups[2].Value, "", "", null, null, null)).ToList();
            meetings = meetingMatches.Cast<Match>().Select(mtg => new MeetingEvidenceItem(mtg.Groups[1].Value, 0, mtg.Groups[2].Value, mtg.Groups[3].Value, "", null, null)).ToList();
        }

        var answerBuilder = new StringBuilder();

        // Check if query is about status/progress
        if (query.Contains("progress", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("status", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("overview", StringComparison.OrdinalIgnoreCase))
        {
            answerBuilder.AppendLine("Based on current project evidence:");
            if (milestones.Count > 0)
            {
                var m = milestones[0];
                answerBuilder.AppendLine($"- Current Milestone: [{m.Id}] '{m.Title}' (Status: {m.Status}).");
            }
            if (tasks.Count > 0)
            {
                var done = tasks.Count(t => string.Equals(t.Status, "DONE", StringComparison.OrdinalIgnoreCase));
                var blocked = tasks.Count(t => string.Equals(t.Status, "BLOCKED", StringComparison.OrdinalIgnoreCase));
                if (isTasksTruncated)
                {
                    answerBuilder.AppendLine($"- Tasks ({retrievedTasks} of {totalTasks} retrieved): {done} completed, {blocked} blocked in retrieved sample.");
                }
                else
                {
                    var total = tasks.Count;
                    answerBuilder.AppendLine($"- Tasks: {done}/{total} completed, {blocked} blocked.");
                }
                foreach (var t in tasks)
                {
                    answerBuilder.AppendLine($"  * [{t.Id}] '{t.Title}' (Status: {t.Status})");
                }
            }
            if (reports.Count > 0)
            {
                var r = reports[0];
                answerBuilder.AppendLine($"- Latest Progress Report: [{r.Id}] ({r.ReportType}).");
            }
            return answerBuilder.ToString().Trim();
        }

        // Check if query is about blockers or risks
        if (query.Contains("block", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("risk", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("issue", StringComparison.OrdinalIgnoreCase))
        {
            var blockedTasks = tasks
                .Where(t => string.Equals(t.Status, "BLOCKED", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (blockedTasks.Count > 0)
            {
                answerBuilder.AppendLine("The following blockers were identified in project tasks:");
                foreach (var bt in blockedTasks)
                {
                    answerBuilder.AppendLine($"- [{bt.Id}] '{bt.Title}' is currently blocked.");
                }
                if (isTasksTruncated)
                {
                    answerBuilder.AppendLine($"Note: Inspected {retrievedTasks} of {totalTasks} total tasks. Additional non-retrieved tasks were not inspected.");
                }
            }
            else
            {
                if (isTasksTruncated)
                {
                    answerBuilder.AppendLine($"No blocked tasks found in the {retrievedTasks} retrieved tasks (out of {totalTasks} total). Non-retrieved tasks were not inspected.");
                }
                else
                {
                    answerBuilder.AppendLine("No blocked tasks were found in the current project records.");
                }
            }
            return answerBuilder.ToString().Trim();
        }

        // Check if query mentions tasks or milestones
        if (query.Contains("task", StringComparison.OrdinalIgnoreCase) && tasks.Count > 0)
        {
            answerBuilder.AppendLine("Relevant project tasks include:");
            foreach (var t in tasks.Take(3))
            {
                answerBuilder.AppendLine($"- [{t.Id}] '{t.Title}' (Status: {t.Status}).");
            }
            return answerBuilder.ToString().Trim();
        }

        if (query.Contains("milestone", StringComparison.OrdinalIgnoreCase) && milestones.Count > 0)
        {
            answerBuilder.AppendLine("Project milestones recorded:");
            foreach (var m in milestones.Take(3))
            {
                answerBuilder.AppendLine($"- [{m.Id}] '{m.Title}' (Status: {m.Status}).");
            }
            return answerBuilder.ToString().Trim();
        }

        if (query.Contains("meeting", StringComparison.OrdinalIgnoreCase) && meetings.Count > 0)
        {
            answerBuilder.AppendLine("Recent meetings recorded:");
            foreach (var mtg in meetings.Take(3))
            {
                answerBuilder.AppendLine($"- [{mtg.Id}] '{mtg.Title}' (Status: {mtg.Status}).");
            }
            return answerBuilder.ToString().Trim();
        }

        // General grounded summary
        if (tasks.Count > 0 || milestones.Count > 0)
        {
            answerBuilder.Append("According to project records, ");
            if (milestones.Count > 0)
            {
                var m = milestones[0];
                answerBuilder.Append($"milestone [{m.Id}] '{m.Title}' is {m.Status}, ");
            }
            if (tasks.Count > 0)
            {
                var t = tasks[0];
                answerBuilder.Append($"and recent task [{t.Id}] '{t.Title}' is {t.Status}.");
            }
            return answerBuilder.ToString().Trim();
        }

        return "No sufficient project evidence was found matching the inquiry.";
    }

    private static string ExtractXmlTag(string xml, string tagName)
    {
        var match = Regex.Match(xml, $@"<{tagName}>(.*?)</{tagName}>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }
}
