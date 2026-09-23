using AIPMS.AI.Providers;
using AIPMS.AI.Services;
using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AIPMS.AI;

public static class DependencyInjection
{
    public static IServiceCollection AddAI(this IServiceCollection services)
    {
        services.AddTransient<IProgressAnalysisService, RuleBasedProgressAnalysisService>();
        services.AddScoped<IAiContextRetriever, AiContextRetriever>();
        services.AddScoped<IAiTextGenerationProvider, GroundedAiTextGenerationProvider>();
        services.AddScoped<IAiAssistantService, AiAssistantService>();
        return services;
    }
}
