using AIPMS.AI.Providers;
using AIPMS.AI.Services;
using AIPMS.AI.Configuration;
using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AIPMS.AI;

public static class DependencyInjection
{
    public static IServiceCollection AddAI(this IServiceCollection services)
    {
        services.AddOptions<AiAssistantOptions>().BindConfiguration(AiAssistantOptions.SectionName);
        services.AddTransient<IProgressAnalysisService, RuleBasedProgressAnalysisService>();
        services.TryAddScoped<IAiContextRetriever, AiContextRetriever>();
        services.AddHttpClient<OpenAiTextGenerationProvider>();
        services.AddScoped<IAiTextGenerationProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiAssistantOptions>>().Value;
            return string.Equals(options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(options.OpenAi.ApiKey)
                ? sp.GetRequiredService<OpenAiTextGenerationProvider>()
                : new GroundedAiTextGenerationProvider(options);
        });
        services.AddScoped<AiAssistantService>();
        services.TryAddScoped<IAiAssistantService, AiAssistantService>();
        return services;
    }
}
