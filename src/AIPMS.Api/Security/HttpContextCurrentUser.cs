using System.Security.Claims;
using AIPMS.Application.Abstractions.Security;

namespace AIPMS.Api.Security;

internal sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public long? UserId => long.TryParse(
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier),
        out var userId)
        ? userId
        : null;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public string? FullName => Principal?.FindFirstValue(ClaimTypes.Name);

    public IReadOnlyCollection<string> Roles => Principal?
        .FindAll(ClaimTypes.Role)
        .Select(static claim => claim.Value)
        .Distinct(StringComparer.Ordinal)
        .ToArray()
        ?? [];
}
