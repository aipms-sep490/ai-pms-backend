using AIPMS.Application.Common.Behaviors;
using AIPMS.Application.Features.Academic.Services;
using AIPMS.Application.Features.AccountSecurity.Services;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AIPMS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var applicationAssembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(applicationAssembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(applicationAssembly, ServiceLifetime.Transient);
        services.AddScoped<AcademicAccessService>();
        services.AddScoped<AccountSecurityAccessService>();

        return services;
    }
}
