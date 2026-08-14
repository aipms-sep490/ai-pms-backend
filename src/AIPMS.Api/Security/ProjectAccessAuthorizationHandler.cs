using AIPMS.Application.Abstractions.Security;
using Microsoft.AspNetCore.Authorization;

namespace AIPMS.Api.Security;

internal sealed class ProjectAccessAuthorizationHandler(
    ICurrentUser currentUser,
    IProjectAccessService projectAccessService,
    IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<ProjectAccessRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ProjectAccessRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext
            ?? httpContextAccessor.HttpContext;

        var routeProjectId = httpContext?.Request.RouteValues["projectId"]?.ToString();
        if (currentUser.UserId is null || !long.TryParse(routeProjectId, out var projectId))
        {
            return;
        }

        if (await projectAccessService.CanAccessAsync(
                currentUser.UserId.Value,
                projectId,
                httpContext?.RequestAborted ?? CancellationToken.None))
        {
            context.Succeed(requirement);
        }
    }
}
