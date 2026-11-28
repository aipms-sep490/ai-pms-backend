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

public sealed partial class GroundedAiTextGenerationProvider(AiAssistantOptions? options = null) : IAiTextGenerationProvider
{
    private readonly AiAssistantOptions _options = options ?? new AiAssistantOptions();
    public string ModelVersion => "GROUNDED_LOCAL_V1";
    public string ConfigurationVersion => $"GROUNDED_V1_T{_options.TimeoutSeconds}_F{_options.SimulateFailure}_T{_options.SimulateTimeout}";

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

        if (systemPrompt.Contains("AI_SUITE_", StringComparison.Ordinal))
        {
            return GenerateAiSuiteJson(systemPrompt, userPrompt);
        }

        if (systemPrompt.Contains("MEETING_AI_MINUTES", StringComparison.Ordinal)
            || systemPrompt.Contains("MEETING_AI_PULSE", StringComparison.Ordinal))
        {
            return GenerateMeetingSummaryJson(userPrompt, systemPrompt.Contains("MEETING_AI_PULSE", StringComparison.Ordinal));
        }

        if (systemPrompt.Contains("MEETING_AI_ASK", StringComparison.Ordinal))
        {
            return GenerateMeetingAskJson(userPrompt);
        }

        if (systemPrompt.Contains("MEETING_AI_COACH", StringComparison.Ordinal))
        {
            return GenerateMeetingCoachJson(userPrompt);
        }

        if (systemPrompt.Contains("KNOWLEDGE_DOC_INGEST", StringComparison.Ordinal))
        {
            return GenerateKnowledgeIngestFallback(userPrompt);
        }

        if (systemPrompt.Contains("CHAT_AI_ASSIST", StringComparison.Ordinal))
        {
            return GenerateChatAssistJson(userPrompt);
        }

        if (systemPrompt.Contains("CHAT_AI_SUMMARY", StringComparison.Ordinal) ||
            (systemPrompt.Contains("actionItems", StringComparison.OrdinalIgnoreCase) &&
             systemPrompt.Contains("openQuestions", StringComparison.OrdinalIgnoreCase)))
        {
            return GenerateChatSummaryJson(userPrompt);
        }

        // Project Q&A request
        return GenerateProjectAnswer(userPrompt);
    }

    private sealed record GroundedChatMessage(string Id, string Sender, string Body);

    private static List<GroundedChatMessage> ParseChatMessages(string payload)
    {
        var list = new List<GroundedChatMessage>();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("messages", out var arr)
                && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var id = item.TryGetProperty("messageId", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString()?.Trim() ?? "" : "";
                    var sender = item.TryGetProperty("sender", out var sEl) && sEl.ValueKind == JsonValueKind.String ? sEl.GetString()?.Trim() ?? "Thành viên" : "Thành viên";
                    var body = item.TryGetProperty("body", out var bEl) && bEl.ValueKind == JsonValueKind.String ? bEl.GetString()?.Trim() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(body))
                        list.Add(new(id, sender, body));
                }
                if (list.Count > 0) return list;
            }
        }
        catch (JsonException)
        {
            // Fall through to legacy line-format parser
        }

        return Regex.Matches(payload, @"messageId=(?<id>[^;]+);\s*time=(?<time>[^;]+);\s*sender=(?<sender>[^;]+);\s*body=(?<body>.*)", RegexOptions.Multiline)
            .Cast<Match>()
            .Select(m => new GroundedChatMessage(m.Groups["id"].Value.Trim(), m.Groups["sender"].Value.Trim(), m.Groups["body"].Value.Trim()))
            .ToList();
    }

    private static string GenerateChatSummaryJson(string transcript)
    {
        var messages = ParseChatMessages(transcript);
        if (messages.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                summary = "Chưa có đủ dữ liệu để tóm tắt.",
                decisions = Array.Empty<string>(),
                blockers = Array.Empty<string>(),
                openQuestions = Array.Empty<string>(),
                actionItems = Array.Empty<object>(),
                smartReplies = Array.Empty<string>(),
                suggestedMeeting = (object?)null
            });
        }

        var decisions = messages
            .Where(m => Regex.IsMatch(m.Body, @"(đồng ý|thống nhất|chốt|quyết định|xác nhận)", RegexOptions.IgnoreCase))
            .Select(m => m.Body)
            .Take(5)
            .ToArray();
        var blockers = messages
            .Where(m => Regex.IsMatch(m.Body, @"(lỗi|vướng|chặn|chậm|khó khăn|rủi ro|bug|block)", RegexOptions.IgnoreCase))
            .Select(m => m.Body)
            .Take(5)
            .ToArray();
        var questions = messages.Where(m => m.Body.Contains('?')).Select(m => m.Body).Take(5).ToArray();
        var actions = messages
            .Where(m => Regex.IsMatch(m.Body, @"\b(cần|hãy|phải|sẽ|làm|hoàn thành|kiểm tra|nộp|sửa)\b", RegexOptions.IgnoreCase))
            .Take(10)
            .Select(m => new
            {
                title = m.Body.Length > 240 ? m.Body[..240] : m.Body,
                ownerName = m.Sender,
                dueAt = (string?)null,
                sourceMessageId = string.IsNullOrWhiteSpace(m.Id) ? null : m.Id
            })
            .ToArray();
        var summary = $"Đã phân tích {messages.Count} tin nhắn. "
            + (decisions.Length > 0 ? $"Có {decisions.Length} quyết định được nêu. " : "")
            + (actions.Length > 0 ? $"Có {actions.Length} việc cần theo dõi." : "");
        return JsonSerializer.Serialize(new
        {
            summary,
            decisions,
            blockers,
            openQuestions = questions,
            actionItems = actions,
            smartReplies = new[]
            {
                "Mình đã nắm được thông tin và sẽ cập nhật tiến độ sớm nhất.",
                "Nhóm mình chốt rõ người phụ trách và hạn hoàn thành cho các đầu việc trên nhé.",
                "Nếu có vướng mắc kỹ thuật nào, mọi người báo ngay để cùng tháo gỡ."
            },
            suggestedMeeting = (object?)null
        });
    }

    private static string GenerateChatAssistJson(string userPrompt)
    {
        var messages = ParseChatMessages(userPrompt);
        var mode = "SUGGEST_REPLIES";
        var query = "";
        var draftText = "";
        try
        {
            using var doc = JsonDocument.Parse(userPrompt);
            var root = doc.RootElement;
            if (root.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String) mode = m.GetString()?.Trim().ToUpperInvariant() ?? mode;
            if (root.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String) query = q.GetString()?.Trim() ?? "";
            if (root.TryGetProperty("draftText", out var d) && d.ValueKind == JsonValueKind.String) draftText = d.GetString()?.Trim() ?? "";
        }
        catch (JsonException) { }

        if (mode == "POLISH_DRAFT")
        {
            var clean = string.IsNullOrWhiteSpace(draftText) ? "Chào mọi người, mình xin cập nhật tiến độ công việc hiện tại." : draftText;
            return JsonSerializer.Serialize(new
            {
                answer = "Đã đề xuất 3 phiên bản diễn đạt rõ ràng, lịch sự hơn nhưng vẫn giữ nguyên ý chính của bản nháp.",
                suggestions = new[]
                {
                    $"Chào mọi người, {clean}",
                    $"{clean} Nhờ mọi người xem và phản hồi giúp mình nhé.",
                    $"Em chào thầy/cô và nhóm, em xin trao đổi ngắn gọn như sau: {clean} Rất mong nhận được góp ý của thầy/cô."
                },
                references = Array.Empty<object>()
            });
        }

        if (mode == "ASK_THREAD")
        {
            var keywords = Regex.Split(query.ToLowerInvariant(), @"[^\p{L}\p{N}]+").Where(w => w.Length >= 2).Distinct().ToArray();
            var matched = messages
                .Select(m => new { Msg = m, Score = keywords.Count(k => m.Body.Contains(k, StringComparison.OrdinalIgnoreCase) || m.Sender.Contains(k, StringComparison.OrdinalIgnoreCase)) })
                .OrderByDescending(x => x.Score)
                .Take(3)
                .ToArray();
            var answer = matched.Length == 0
                ? "Chưa thấy nội dung liên quan trong lịch sử tin nhắn gần đây."
                : "Dựa trên lịch sử trò chuyện gần đây:\n" + string.Join("\n", matched.Select(x => $"- **{x.Msg.Sender}**: {x.Msg.Body}"));
            return JsonSerializer.Serialize(new
            {
                answer,
                suggestions = new[] { "Có đầu việc nào cần chốt hạn hoàn thành từ trao đổi này không?" },
                references = matched.Where(x => !string.IsNullOrWhiteSpace(x.Msg.Id)).Select(x => new
                {
                    messageId = x.Msg.Id,
                    excerpt = x.Msg.Body.Length > 160 ? x.Msg.Body[..160] : x.Msg.Body
                }).ToArray()
            });
        }

        var latest = messages.LastOrDefault();
        return JsonSerializer.Serialize(new
        {
            answer = latest is null
                ? "Chưa có tin nhắn gần đây để gợi ý phản hồi."
                : $"Phản hồi cho trao đổi gần nhất của {latest.Sender}.",
            suggestions = new[]
            {
                "Cảm ơn bạn đã cập nhật, mình đã nắm thông tin nhé!",
                "Bạn cho mình hỏi thêm chi tiết mốc hoàn thành cho phần này nhé?",
                "Mình đề xuất cập nhật đầu việc này lên bảng Task để cả nhóm cùng theo dõi."
            },
            references = latest is not null && !string.IsNullOrWhiteSpace(latest.Id)
                ? new object[] { new { messageId = latest.Id, excerpt = latest.Body.Length > 160 ? latest.Body[..160] : latest.Body } }
                : Array.Empty<object>()
        });
    }

    private static string GenerateKnowledgeIngestFallback(string userPrompt)
    {
        var fileName = "Tài liệu học vụ";
        var text = "";
        try
        {
            using var doc = JsonDocument.Parse(userPrompt);
            var root = doc.RootElement;
            if (root.TryGetProperty("fileName", out var fn) && fn.ValueKind == JsonValueKind.String) fileName = fn.GetString() ?? fileName;
            if (root.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String) text = tx.GetString() ?? "";
        }
        catch (JsonException) { }
        var snippet = text.Length > 1500 ? text[..1500] : text;
        return JsonSerializer.Serialize(new
        {
            suggestedTitle = Path.GetFileNameWithoutExtension(fileName),
            suggestedDocumentType = "PROCESS",
            summary = $"Trích xuất cục bộ từ tệp {fileName}.",
            chunks = string.IsNullOrWhiteSpace(snippet)
                ? Array.Empty<object>()
                : new object[] { new { heading = "Nội dung chính", pageNumber = (int?)null, content = snippet } },
            warnings = new[] { "Đang sử dụng bộ trích xuất cục bộ dự phòng." }
        });
    }

    private sealed record GroundedMeetingSegment(long SegmentId, string Speaker, string Time, string Text, bool Highlighted);

    private static (string Title, string Agenda, List<GroundedMeetingSegment> Segments, List<string> Tasks, string Query, string Mode) ParseMeetingPayload(string userPrompt)
    {
        var segments = new List<GroundedMeetingSegment>();
        var tasks = new List<string>();
        var title = "Cuộc họp nhóm";
        var agenda = "";
        var query = "";
        var mode = "AGENDA_CHECK";

        try
        {
            using var doc = JsonDocument.Parse(userPrompt);
            var root = doc.RootElement;
            if (root.TryGetProperty("meetingTitle", out var t) && t.ValueKind == JsonValueKind.String) title = t.GetString() ?? title;
            if (root.TryGetProperty("agenda", out var a) && a.ValueKind == JsonValueKind.String) agenda = a.GetString() ?? "";
            if (root.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.String) query = q.GetString() ?? "";
            if (root.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String) mode = m.GetString() ?? mode;

            if (root.TryGetProperty("segments", out var segs) && segs.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in segs.EnumerateArray())
                {
                    var id = item.TryGetProperty("segmentId", out var idProp) && idProp.TryGetInt64(out var parsedId) ? parsedId : 0;
                    var speaker = item.TryGetProperty("speaker", out var spProp) ? spProp.GetString() ?? "Thành viên" : "Thành viên";
                    var time = item.TryGetProperty("time", out var tmProp) ? tmProp.GetString() ?? "" : "";
                    var text = item.TryGetProperty("text", out var txProp) ? txProp.GetString() ?? "" : "";
                    var hi = item.TryGetProperty("highlighted", out var hiProp) && hiProp.ValueKind == JsonValueKind.True;
                    if (!string.IsNullOrWhiteSpace(text))
                        segments.Add(new(id, speaker, time, text, hi));
                }
            }

            if (root.TryGetProperty("projectTasks", out var pts) && pts.ValueKind == JsonValueKind.Array)
            {
                foreach (var pt in pts.EnumerateArray())
                {
                    var taskTitle = pt.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : "";
                    var status = pt.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(taskTitle)) tasks.Add($"{taskTitle} ({status})");
                }
            }
        }
        catch
        {
            // Keep safe defaults
        }

        return (title, agenda, segments, tasks, query, mode);
    }

    private static string GenerateMeetingSummaryJson(string userPrompt, bool isPulse)
    {
        var (title, agenda, segments, _, _, _) = ParseMeetingPayload(userPrompt);
        if (segments.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                executiveSummary = $"Chưa có nội dung lời nói được ghi nhận cho cuộc họp \"{title}\".",
                currentTopic = string.IsNullOrWhiteSpace(agenda) ? "Chưa xác định" : agenda.Split('\n')[0].Trim(),
                formattedMinutes = $"## Biên bản cuộc họp: {title}\n\nChưa có đoạn hội thoại nào được ghi nhận.",
                keyTopics = Array.Empty<object>(),
                decisions = Array.Empty<string>(),
                actionItems = Array.Empty<object>(),
                blockersAndRisks = Array.Empty<string>(),
                openQuestions = Array.Empty<string>(),
                agendaCoverage = Array.Empty<object>(),
                speakerInsights = Array.Empty<object>(),
                suggestedNextQuestions = new[] { "Bắt đầu điểm qua mục tiêu chính của buổi họp hôm nay là gì?" },
                nextMeetingSuggestion = (object?)null
            });
        }

        var decisions = segments
            .Where(s => Regex.IsMatch(s.Text, @"(thống nhất|chốt|đồng ý|quyết định|xác nhận|agree|decided)", RegexOptions.IgnoreCase))
            .Select(s => $"{s.Speaker}: {s.Text}")
            .Take(8)
            .ToArray();

        var blockers = segments
            .Where(s => Regex.IsMatch(s.Text, @"(lỗi|vướng|chặn|chậm|khó khăn|rủi ro|bug|block|delay|chưa chạy)", RegexOptions.IgnoreCase))
            .Select(s => $"{s.Speaker}: {s.Text}")
            .Take(8)
            .ToArray();

        var questions = segments
            .Where(s => s.Text.Contains('?') || Regex.IsMatch(s.Text, @"\b(hay không|như thế nào|bao giờ|ai sẽ|có nên)\b", RegexOptions.IgnoreCase))
            .Select(s => s.Text)
            .Take(8)
            .ToArray();

        var actions = segments
            .Where(s => Regex.IsMatch(s.Text, @"\b(sẽ|cần|phải|phụ trách|làm|hoàn thành|nộp|kiểm tra|fix|sửa|tạo|viết)\b", RegexOptions.IgnoreCase))
            .Take(10)
            .Select(s => new
            {
                title = s.Text.Length > 140 ? s.Text[..140] + "…" : s.Text,
                description = $"Ghi nhận từ phát biểu lúc {s.Time} của {s.Speaker}.",
                ownerName = s.Speaker,
                dueAt = (string?)null,
                priority = s.Highlighted ? "HIGH" : "MEDIUM",
                evidenceQuote = s.Text.Length > 200 ? s.Text[..200] : s.Text,
                sourceSegmentId = s.SegmentId > 0 ? (long?)s.SegmentId : null
            })
            .ToArray();

        var speakers = segments.GroupBy(s => s.Speaker).Select(g => new
        {
            speakerName = g.Key,
            summary = $"Phát biểu {g.Count()} lượt ({g.Count(x => x.Highlighted)} ý được đánh dấu quan trọng).",
            keyContributions = g.OrderByDescending(x => x.Highlighted).Take(2).Select(x => x.Text).ToArray()
        }).ToArray();

        var agendaItems = string.IsNullOrWhiteSpace(agenda)
            ? Array.Empty<object>()
            : agenda.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(6)
                .Select(line =>
                {
                    var clean = line.TrimStart('-', '*', '•', ' ');
                    var tokens = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).Take(4).ToArray();
                    var hits = tokens.Length == 0 ? 0 : segments.Count(s => tokens.Any(tok => s.Text.Contains(tok, StringComparison.OrdinalIgnoreCase)));
                    var status = hits >= 2 ? "COVERED" : hits == 1 ? "PARTIAL" : "MISSED";
                    return (object)new { item = clean, status, note = hits > 0 ? $"Đã được đề cập trong {hits} phát biểu." : "Chưa thấy trao đổi rõ trong transcript." };
                }).ToArray();

        var latestText = segments[^1].Text;
        var currentTopic = latestText.Length > 100 ? latestText[..100] + "…" : latestText;
        var executiveSummary = isPulse
            ? $"Cập nhật nhanh từ {segments.Count} phát biểu của {speakers.Length} thành viên: trọng tâm gần nhất xoay quanh \"{currentTopic}\". Ghi nhận {decisions.Length} điểm thống nhất, {actions.Length} đầu việc và {blockers.Length} vướng mắc."
            : $"Cuộc họp \"{title}\" ghi nhận {segments.Count} phát biểu từ {speakers.Length} thành viên. Nhóm đã trao đổi và ghi nhận {decisions.Length} quyết định, {actions.Length} đầu việc cần triển khai và {blockers.Length} vướng mắc/rủi ro cần theo dõi.";

        var minutes = new StringBuilder();
        minutes.AppendLine($"# Biên bản cuộc họp: {title}");
        minutes.AppendLine();
        minutes.AppendLine("## 1. Tóm tắt chung");
        minutes.AppendLine(executiveSummary);
        if (decisions.Length > 0)
        {
            minutes.AppendLine();
            minutes.AppendLine("## 2. Kết luận & Thống nhất");
            foreach (var d in decisions) minutes.AppendLine($"- {d}");
        }
        if (actions.Length > 0)
        {
            minutes.AppendLine();
            minutes.AppendLine("## 3. Phân công công việc");
            foreach (var act in actions) minutes.AppendLine($"- [{act.priority}] {act.title} — Phụ trách: {act.ownerName}");
        }
        if (blockers.Length > 0)
        {
            minutes.AppendLine();
            minutes.AppendLine("## 4. Vướng mắc & Rủi ro");
            foreach (var b in blockers) minutes.AppendLine($"- {b}");
        }

        return JsonSerializer.Serialize(new
        {
            executiveSummary,
            currentTopic,
            formattedMinutes = minutes.ToString().Trim(),
            keyTopics = new[]
            {
                new
                {
                    title = "Nội dung trao đổi chính",
                    summary = executiveSummary,
                    speakers = speakers.Select(s => s.speakerName).Take(5).ToArray()
                }
            },
            decisions,
            actionItems = actions,
            blockersAndRisks = blockers,
            openQuestions = questions,
            agendaCoverage = agendaItems,
            speakerInsights = speakers,
            suggestedNextQuestions = new[]
            {
                "Ai sẽ xác nhận lại thời hạn hoàn thành cho từng đầu việc vừa thống nhất?",
                "Có vướng mắc kỹ thuật nào cần trao đổi thêm với GVHD không?",
                "Mục tiếp theo trong agenda nhóm muốn chốt là gì?"
            },
            nextMeetingSuggestion = (object?)null
        });
    }

    private static string GenerateMeetingAskJson(string userPrompt)
    {
        var (_, _, segments, _, question, _) = ParseMeetingPayload(userPrompt);
        if (segments.Count == 0)
        {
            return JsonSerializer.Serialize(new
            {
                answer = "Chưa có đoạn hội thoại nào trong cuộc họp để trả lời câu hỏi này.",
                citations = Array.Empty<object>(),
                followUpQuestions = new[] { "Hãy bật Phụ đề AI (Live) hoặc thêm ghi chú lời nói để AI có thể phân tích." }
            });
        }

        var keywords = Regex.Split(question.ToLowerInvariant(), @"[^\p{L}\p{N}]+")
            .Where(w => w.Length >= 2)
            .Distinct()
            .ToArray();

        var ranked = segments
            .Select(s => new
            {
                Segment = s,
                Score = keywords.Count(k => s.Text.Contains(k, StringComparison.OrdinalIgnoreCase) || s.Speaker.Contains(k, StringComparison.OrdinalIgnoreCase))
                        + (s.Highlighted ? 1 : 0)
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Segment.SegmentId)
            .Take(4)
            .ToArray();

        var answer = new StringBuilder();
        answer.AppendLine("Dựa trên nội dung đã trao đổi trong cuộc họp:");
        foreach (var item in ranked)
        {
            answer.AppendLine($"- **{item.Segment.Speaker}** ({item.Segment.Time}): {item.Segment.Text}");
        }

        return JsonSerializer.Serialize(new
        {
            answer = answer.ToString().Trim(),
            citations = ranked.Where(r => r.Segment.SegmentId > 0).Select(r => new
            {
                segmentId = r.Segment.SegmentId,
                excerpt = r.Segment.Text.Length > 180 ? r.Segment.Text[..180] : r.Segment.Text
            }).ToArray(),
            followUpQuestions = new[]
            {
                "Những đầu việc nào cần tạo thành Task sau buổi họp?",
                "Có rủi ro hay điểm nghẽn nào đang cản tiến độ không?"
            }
        });
    }

    private static string GenerateMeetingCoachJson(string userPrompt)
    {
        var (title, agenda, segments, tasks, _, mode) = ParseMeetingPayload(userPrompt);
        var findings = new List<object>();

        if (mode == "TASK_AUDIT")
        {
            foreach (var task in tasks.Take(5))
            {
                var mentioned = segments.Any(s => task.Split(' ').Where(w => w.Length >= 4).Any(w => s.Text.Contains(w, StringComparison.OrdinalIgnoreCase)));
                findings.Add(new
                {
                    category = mentioned ? "AGENDA" : "UNMENTIONED_TASK",
                    severity = mentioned ? "LOW" : "HIGH",
                    title = mentioned ? $"Đã đề cập: {task}" : $"Chưa thấy nhắc tới task: {task}",
                    detail = mentioned ? "Công việc này đã được nhóm trao đổi trong buổi họp." : "Nên rà soát nhanh tiến độ hoặc vướng mắc của đầu việc này trước khi kết thúc cuộc họp.",
                    relatedEntity = task
                });
            }
        }
        else if (mode == "SUPERVISOR_FEEDBACK_DRAFT")
        {
            findings.Add(new
            {
                category = "FEEDBACK",
                severity = "MEDIUM",
                title = "Điểm mạnh & trao đổi của nhóm",
                detail = $"Nhóm đã ghi nhận {segments.Count} lượt trao đổi trong buổi họp \"{title}\".",
                relatedEntity = title
            });
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(agenda))
            {
                foreach (var line in agenda.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(4))
                {
                    var clean = line.TrimStart('-', '*', '•', ' ');
                    var covered = segments.Any(s => clean.Split(' ').Where(w => w.Length >= 4).Any(w => s.Text.Contains(w, StringComparison.OrdinalIgnoreCase)));
                    findings.Add(new
                    {
                        category = "AGENDA",
                        severity = covered ? "LOW" : "MEDIUM",
                        title = covered ? $"Đã bàn: {clean}" : $"Cần thảo luận thêm: {clean}",
                        detail = covered ? "Nội dung này đã xuất hiện trong trao đổi." : "Mục này có trong agenda nhưng chưa thấy đề cập rõ.",
                        relatedEntity = clean
                    });
                }
            }
            if (findings.Count == 0)
            {
                findings.Add(new
                {
                    category = "COACHING",
                    severity = "LOW",
                    title = "Gợi ý chốt kết luận rõ người phụ trách",
                    detail = $"Hiện đã có {segments.Count} đoạn ghi nhận lời nói. Hãy chốt rõ đầu việc, người phụ trách và hạn hoàn thành.",
                    relatedEntity = title
                });
            }
        }

        string? draftText = mode == "SUPERVISOR_FEEDBACK_DRAFT"
            ? $"Nhận xét buổi họp \"{title}\":\n- Nhóm đã trao đổi tích cực ({segments.Count} lượt phát biểu).\n- Cần cụ thể hóa rõ người phụ trách và hạn hoàn thành cho từng đầu việc trên bảng Task trước mốc tiếp theo."
            : null;

        return JsonSerializer.Serialize(new
        {
            headline = mode switch
            {
                "CATCH_UP" => $"Bắt kịp nhanh buổi họp \"{title}\"",
                "TASK_AUDIT" => "Đối chiếu nội dung họp với Task & Milestone đồ án",
                "SUPERVISOR_FEEDBACK_DRAFT" => "Bản nháp nhận xét của Giảng viên hướng dẫn",
                _ => "Kiểm tra Agenda & Gợi ý điều phối cuộc họp"
            },
            summary = segments.Count == 0
                ? "Chưa có lời nói được ghi nhận để phân tích sâu; đây là các mục cần chuẩn bị dựa trên hồ sơ cuộc họp và đồ án."
                : $"AI đã rà soát {segments.Count} lượt phát biểu cùng bối cảnh đồ án để đưa ra {findings.Count} lưu ý điều phối.",
            findings,
            suggestedPrompts = new[]
            {
                "Nhóm thống nhất mốc hoàn thành cụ thể cho các đầu việc chính là ngày nào?",
                "Có task nào đang bị chặn hoặc cần hỗ trợ kỹ thuật ngay tuần này không?"
            },
            draftText
        });
    }

    private static string GenerateReportSummaryJson(string userPrompt)
    {
        var evidenceMatch = Regex.Match(userPrompt, @"<evidence_json>(.*?)</evidence_json>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!evidenceMatch.Success)
        {
            evidenceMatch = Regex.Match(userPrompt, @"<evidence_payload>(.*?)</evidence_payload>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }
        if (!evidenceMatch.Success)
        {
            evidenceMatch = Regex.Match(userPrompt, @"<report_evidence>(.*?)</report_evidence>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
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

        var inProgressRaw = !string.IsNullOrWhiteSpace(reportPayload?.InProgressWork)
            ? reportPayload.InProgressWork
            : planned;
        var blockersRaw = !string.IsNullOrWhiteSpace(reportPayload?.Blockers)
            ? reportPayload.Blockers
            : issues;
        var risksRaw = !string.IsNullOrWhiteSpace(reportPayload?.Risks)
            ? reportPayload.Risks
            : issues;
        var nextActionsRaw = !string.IsNullOrWhiteSpace(reportPayload?.NextActions)
            ? reportPayload.NextActions
            : (!string.IsNullOrWhiteSpace(planned) ? planned : summary);

        var completedSummary = !string.IsNullOrWhiteSpace(completed)
            ? $"Completed items: {completed}"
            : "No completed work reported for this period.";

        var inProgressSummary = !string.IsNullOrWhiteSpace(inProgressRaw)
            ? $"Work in progress: {inProgressRaw}"
            : "No active in-progress work reported.";

        var blockersSummary = !string.IsNullOrWhiteSpace(reportPayload?.Blockers)
            ? $"Identified blockers: {reportPayload.Blockers}"
            : (!string.IsNullOrWhiteSpace(issues) && (issues.Contains("block", StringComparison.OrdinalIgnoreCase) || issues.Contains("delay", StringComparison.OrdinalIgnoreCase) || issues.Contains("issue", StringComparison.OrdinalIgnoreCase))
                ? $"Identified blockers: {issues}"
                : "No active blockers reported.");

        var risksSummary = !string.IsNullOrWhiteSpace(reportPayload?.Risks)
            ? $"Identified risks: {reportPayload.Risks}"
            : (!string.IsNullOrWhiteSpace(issues) && (issues.Contains("risk", StringComparison.OrdinalIgnoreCase) || issues.Contains("uncertain", StringComparison.OrdinalIgnoreCase))
                ? $"Identified risks: {issues}"
                : (!string.IsNullOrWhiteSpace(issues) ? $"Potential risks noted: {issues}" : "No specific risks reported."));

        var nextActionsSummary = !string.IsNullOrWhiteSpace(nextActionsRaw)
            ? $"Next actions: {nextActionsRaw}"
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
            query.Contains("overview", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("tiến độ", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("trạng thái", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("tổng quan", StringComparison.OrdinalIgnoreCase))
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
            query.Contains("issue", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("bị chặn", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("vướng", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("rủi ro", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("lỗi", StringComparison.OrdinalIgnoreCase))
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
        if ((query.Contains("task", StringComparison.OrdinalIgnoreCase) ||
             query.Contains("công việc", StringComparison.OrdinalIgnoreCase)) && tasks.Count > 0)
        {
            answerBuilder.AppendLine("Relevant project tasks include:");
            foreach (var t in tasks.Take(3))
            {
                answerBuilder.AppendLine($"- [{t.Id}] '{t.Title}' (Status: {t.Status}).");
            }
            return answerBuilder.ToString().Trim();
        }

        if ((query.Contains("milestone", StringComparison.OrdinalIgnoreCase) ||
             query.Contains("mốc", StringComparison.OrdinalIgnoreCase)) && milestones.Count > 0)
        {
            answerBuilder.AppendLine("Project milestones recorded:");
            foreach (var m in milestones.Take(3))
            {
                answerBuilder.AppendLine($"- [{m.Id}] '{m.Title}' (Status: {m.Status}).");
            }
            return answerBuilder.ToString().Trim();
        }

        if ((query.Contains("meeting", StringComparison.OrdinalIgnoreCase) ||
             query.Contains("cuộc họp", StringComparison.OrdinalIgnoreCase) ||
             query.Contains("họp", StringComparison.OrdinalIgnoreCase)) && meetings.Count > 0)
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
