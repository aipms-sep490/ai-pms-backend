using AIPMS.Application.Common.Behaviors;
using AIPMS.Application.Features.Academic.Services;
using AIPMS.Application.Features.AccountSecurity.Services;
using AIPMS.Application.Features.Semesters.Services;
using AIPMS.Application.Features.Supervisors.Services;
using AIPMS.Application.Features.Teams.Abstractions;
using AIPMS.Application.Features.Teams.Services;
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
        services.AddScoped<SemesterAccessService>();
        services.AddScoped<SupervisorAccessService>();
        services.AddScoped<SupervisorRequestWorkflow>();
        services.AddScoped<SupervisorAssignmentWorkflow>();
        services.AddScoped<TeamWorkflow>();
        services.AddScoped<ITeamRegistrationGuard, TeamRegistrationGuard>();

        return services;
    }
}
