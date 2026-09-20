using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.AI.Configuration;
using AIPMS.Application.Features.AiAssistant.Abstractions;

namespace AIPMS.AI.Providers;

public sealed class GroundedAiTextGenerationProvider(AiAssistantOptions? options = null) : IAiTextGenerationProvider
{
    private readonly AiAssistantOptions _options = options ?? new AiAssistantOptions();

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
        if (userPrompt.Contains("<report_evidence"))
        {
            return GenerateReportSummaryJson(userPrompt);
        }

        // Project Q&A request
        return GenerateProjectAnswer(userPrompt);
    }

    private static string GenerateReportSummaryJson(string userPrompt)
    {
        var evidencePayloadMatch = Regex.Match(userPrompt, @"<evidence_payload>(.*?)</evidence_payload>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var evidenceText = evidencePayloadMatch.Success ? evidencePayloadMatch.Groups[1].Value : userPrompt;

        var summary = ExtractXmlTag(evidenceText, "summary");
        var completed = ExtractXmlTag(evidenceText, "completed_work");
        var planned = ExtractXmlTag(evidenceText, "planned_work");
        var issues = ExtractXmlTag(evidenceText, "issues_and_risks");

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

        // Provider parsing: provider must parse evidence ONLY from the authoritative evidence section (<evidence_payload>)
        // provider must NEVER regex-search the raw prompt or user query block for evidence tags
        var evidencePayloadMatch = Regex.Match(userPrompt, @"<evidence_payload>(.*?)</evidence_payload>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var evidenceText = evidencePayloadMatch.Success ? evidencePayloadMatch.Groups[1].Value : string.Empty;

        // Check task truncation metadata from evidence
        var tasksTagMatch = Regex.Match(evidenceText, @"<tasks\s+total_count=""(\d+)""\s+retrieved_count=""(\d+)""\s+truncated=""(true|false)""[^>]*>", RegexOptions.IgnoreCase);
        var totalTasks = tasksTagMatch.Success ? int.Parse(tasksTagMatch.Groups[1].Value) : 0;
        var retrievedTasks = tasksTagMatch.Success ? int.Parse(tasksTagMatch.Groups[2].Value) : 0;
        var isTasksTruncated = tasksTagMatch.Success && bool.Parse(tasksTagMatch.Groups[3].Value);

        // Extract tasks, milestones, reports, meetings ONLY from evidenceText
        var taskMatches = Regex.Matches(evidenceText, @"<task id=""([^""]+)"" title=""([^""]+)"" status=""([^""]+)""[^>]*>(.*?)</task>", RegexOptions.Singleline);
        var milestoneMatches = Regex.Matches(evidenceText, @"<milestone id=""([^""]+)"" title=""([^""]+)"" status=""([^""]+)""[^>]*>(.*?)</milestone>", RegexOptions.Singleline);
        var reportMatches = Regex.Matches(evidenceText, @"<report id=""([^""]+)"" type=""([^""]+)""[^>]*>(.*?)</report>", RegexOptions.Singleline);
        var meetingMatches = Regex.Matches(evidenceText, @"<meeting id=""([^""]+)"" title=""([^""]+)"" status=""([^""]+)""[^>]*>(.*?)</meeting>", RegexOptions.Singleline);

        var answerBuilder = new StringBuilder();

        // Check if query is about status/progress
        if (query.Contains("progress", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("status", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("overview", StringComparison.OrdinalIgnoreCase))
        {
            answerBuilder.AppendLine("Based on current project evidence:");
            if (milestoneMatches.Count > 0)
            {
                var m = milestoneMatches[0];
                answerBuilder.AppendLine($"- Current Milestone: [{m.Groups[1].Value}] '{m.Groups[2].Value}' (Status: {m.Groups[3].Value}).");
            }
            if (taskMatches.Count > 0)
            {
                var done = taskMatches.Cast<Match>().Count(t => t.Groups[3].Value.Equals("DONE", StringComparison.OrdinalIgnoreCase));
                var blocked = taskMatches.Cast<Match>().Count(t => t.Groups[3].Value.Equals("BLOCKED", StringComparison.OrdinalIgnoreCase));
                if (isTasksTruncated)
                {
                    answerBuilder.AppendLine($"- Tasks ({retrievedTasks} of {totalTasks} retrieved): {done} completed, {blocked} blocked in retrieved sample.");
                }
                else
                {
                    var total = taskMatches.Count;
                    answerBuilder.AppendLine($"- Tasks: {done}/{total} completed, {blocked} blocked.");
                }
                foreach (var t in taskMatches.Cast<Match>())
                {
                    answerBuilder.AppendLine($"  * [{t.Groups[1].Value}] '{t.Groups[2].Value}' (Status: {t.Groups[3].Value})");
                }
            }
            if (reportMatches.Count > 0)
            {
                var r = reportMatches[0];
                answerBuilder.AppendLine($"- Latest Progress Report: [{r.Groups[1].Value}] ({r.Groups[2].Value}).");
            }
            return answerBuilder.ToString().Trim();
        }

        // Check if query is about blockers or risks
        if (query.Contains("block", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("risk", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("issue", StringComparison.OrdinalIgnoreCase))
        {
            var blockedTasks = taskMatches.Cast<Match>()
                .Where(t => t.Groups[3].Value.Equals("BLOCKED", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (blockedTasks.Count > 0)
            {
                answerBuilder.AppendLine("The following blockers were identified in project tasks:");
                foreach (var bt in blockedTasks)
                {
                    answerBuilder.AppendLine($"- [{bt.Groups[1].Value}] '{bt.Groups[2].Value}' is currently blocked.");
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
        if (query.Contains("task", StringComparison.OrdinalIgnoreCase) && taskMatches.Count > 0)
        {
            answerBuilder.AppendLine("Relevant project tasks include:");
            foreach (var t in taskMatches.Cast<Match>().Take(3))
            {
                answerBuilder.AppendLine($"- [{t.Groups[1].Value}] '{t.Groups[2].Value}' (Status: {t.Groups[3].Value}).");
            }
            return answerBuilder.ToString().Trim();
        }

        if (query.Contains("milestone", StringComparison.OrdinalIgnoreCase) && milestoneMatches.Count > 0)
        {
            answerBuilder.AppendLine("Project milestones recorded:");
            foreach (var m in milestoneMatches.Cast<Match>().Take(3))
            {
                answerBuilder.AppendLine($"- [{m.Groups[1].Value}] '{m.Groups[2].Value}' (Status: {m.Groups[3].Value}).");
            }
            return answerBuilder.ToString().Trim();
        }

        if (query.Contains("meeting", StringComparison.OrdinalIgnoreCase) && meetingMatches.Count > 0)
        {
            answerBuilder.AppendLine("Recent meetings recorded:");
            foreach (var mtg in meetingMatches.Cast<Match>().Take(3))
            {
                answerBuilder.AppendLine($"- [{mtg.Groups[1].Value}] '{mtg.Groups[2].Value}' (Status: {mtg.Groups[3].Value}).");
            }
            return answerBuilder.ToString().Trim();
        }

        // General grounded summary
        if (taskMatches.Count > 0 || milestoneMatches.Count > 0)
        {
            answerBuilder.Append("According to project records, ");
            if (milestoneMatches.Count > 0)
            {
                var m = milestoneMatches[0];
                answerBuilder.Append($"milestone [{m.Groups[1].Value}] '{m.Groups[2].Value}' is {m.Groups[3].Value}, ");
            }
            if (taskMatches.Count > 0)
            {
                var t = taskMatches[0];
                answerBuilder.Append($"and recent task [{t.Groups[1].Value}] '{t.Groups[2].Value}' is {t.Groups[3].Value}.");
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
