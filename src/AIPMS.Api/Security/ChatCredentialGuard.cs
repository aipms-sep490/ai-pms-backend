using System.Security.Claims;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Chat;

namespace AIPMS.Api.Security;

internal sealed class ChatCredentialGuard(IHttpContextAccessor http, IAccessTokenAccountValidator validator) : IChatCredentialGuard
{
    public async Task CheckAsync(long actor,CancellationToken ct)
    {
        var user=http.HttpContext?.User;
        if(user?.Identity?.IsAuthenticated!=true || user.FindFirstValue(ClaimTypes.NameIdentifier)!=actor.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || !long.TryParse(user.FindFirstValue("pwd"),out var ticks) || ticks<0 || ticks>DateTime.MaxValue.Ticks
            || !await validator.IsValidAsync(actor,ticks==0?null:new DateTime(ticks,DateTimeKind.Utc),ct))
            throw new UnauthorizedException("CHAT_SESSION_EXPIRED");
    }
}
