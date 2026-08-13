using AIPMS.AI.Services;
using AIPMS.Application.Abstractions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AIPMS.AI;

public static class DependencyInjection
{
    public static IServiceCollection AddAI(this IServiceCollection services)
    {
        services.AddTransient<IProgressAnalysisService, RuleBasedProgressAnalysisService>();
        return services;
    }
}
