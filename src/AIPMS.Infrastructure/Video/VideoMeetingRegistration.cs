using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Application.Features.Meetings.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AIPMS.Infrastructure.Video;

public static class VideoMeetingRegistration
{
    public static IServiceCollection AddVideoMeetings(this IServiceCollection services)
    {
        services.AddOptions<VideoMeetingOptions>().BindConfiguration(VideoMeetingOptions.SectionName).Validate(x => x.IsReady, "VideoMeeting configuration is invalid when enabled.").ValidateOnStart();
        services.AddHttpClient("livekit", client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<LiveKitWebhookVerifier>();
        services.AddScoped<IVideoMeetingProvider, LiveKitVideoMeetingProvider>();
        return services;
    }
}


