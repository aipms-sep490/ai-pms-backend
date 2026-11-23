using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Meetings.Video;

namespace AIPMS.Application.Features.Meetings.Abstractions;

public interface IMeetingVideoAiService
{
    Task<MeetingTranscriptFeedDto> GetWorkspaceAsync(long meetingId, long? afterSegmentId, CancellationToken ct);
    Task<MeetingTranscriptSegmentDto> AppendSegmentAsync(long meetingId, CreateTranscriptSegmentRequest request, CancellationToken ct);
    Task<MeetingTranscriptSegmentDto> TranscribeAudioClipAsync(long meetingId, Stream audioStream, string fileName, string contentType, string? language, CancellationToken ct);
    Task<MeetingTranscriptSegmentDto> ToggleHighlightAsync(long meetingId, long segmentId, bool isHighlighted, CancellationToken ct);
    Task DeleteSegmentAsync(long meetingId, long segmentId, CancellationToken ct);
    Task<IReadOnlyList<MeetingAiInsightDto>> GetInsightsAsync(long meetingId, int limit, CancellationToken ct);
    Task<MeetingAiInsightDto> GenerateSummaryAsync(long meetingId, GenerateMeetingAiSummaryRequest request, CancellationToken ct);
    Task<MeetingAiAnswerDto> AskAsync(long meetingId, AskMeetingAiRequest request, CancellationToken ct);
    Task<MeetingAiCoachDto> CoachAsync(long meetingId, MeetingAiCoachRequest request, CancellationToken ct);
}
