using AIPMS.AI.Providers;
using AIPMS.AI.Services;
using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIPMS.AI;

public static class DependencyInjection
{
    public static IServiceCollection AddAI(this IServiceCollection services)
    {
        services.AddTransient<IProgressAnalysisService, RuleBasedProgressAnalysisService>();
        services.TryAddScoped<IAiContextRetriever, AiContextRetriever>();
        services.AddScoped<IAiTextGenerationProvider, GroundedAiTextGenerationProvider>();
        services.AddScoped<AiAssistantService>();
        services.TryAddScoped<IAiAssistantService, AiAssistantService>();
        return services;
    }
}
