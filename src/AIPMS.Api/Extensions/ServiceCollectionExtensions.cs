using AIPMS.AI.Services;
using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Features.Projects.Queries.GetProjectLifecycle;
using Microsoft.OpenApi.Models;

namespace AIPMS.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "AI-PMS API",
                Version = "v1",
                Description = "Academic project lifecycle management and AI decision support."
            });
        });

        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options =>
        {
            options.AddPolicy("Frontend", policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        services.AddScoped<GetProjectLifecycleQuery>();
        services.AddScoped<IProgressAnalysisService, RuleBasedProgressAnalysisService>();

        return services;
    }
}
