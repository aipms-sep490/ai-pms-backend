using System.ComponentModel.DataAnnotations;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.DTOs;
using AIPMS.Infrastructure.Identity.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[EnableCors("google-auth")]
[EnableRateLimiting("authentication")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class GoogleAuthController(IGoogleAuthService service, IOpaqueTokenService opaque,
    IOptions<GoogleAuthSettings> options, IHostEnvironment environment) : ControllerBase
{
    private const string BindingCookie = "aipms-google-binding";

    [AllowAnonymous]
    [HttpPost("google/challenge")]
    [Consumes("application/json")]
    [ProducesResponseType<GoogleChallenge>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GoogleChallenge>> Challenge(GoogleChallengeInput input, CancellationToken ct)
    {
        GuardOrigin();
        var binding = Request.Cookies[BindingCookie];
        if (binding is null || binding.Length != 64) binding = opaque.Generate().Value;
        var result = await service.ChallengeAsync(input.Purpose, binding, ct);
        Response.Cookies.Append(BindingCookie, binding, new CookieOptions { HttpOnly = true,
            Secure = !environment.IsDevelopment() || Request.IsHttps, SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth/google", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true });
        Response.Headers.CacheControl = "no-store";
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("google/login")]
    [Consumes("application/json")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> Login(GoogleLoginInput input, CancellationToken ct)
    {
        GuardOrigin();
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.LoginAsync(input.ChallengeId, input.IdToken, Binding(), ct));
    }

    [Authorize]
    [HttpPost("google/link")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Link(GoogleLinkInput input, CancellationToken ct)
    {
        GuardOrigin();
        await service.LinkAsync(input.ChallengeId, input.IdToken, Binding(), input.CurrentPassword, ct);
        return NoContent();
    }

    [Authorize]
    [HttpPost("google/unlink")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlink(GoogleUnlinkInput input, CancellationToken ct)
    {
        GuardOrigin();
        await service.UnlinkAsync(input.CurrentPassword, ct);
        return NoContent();
    }

    [Authorize]
    [HttpGet("external-logins")]
    [ProducesResponseType<IReadOnlyList<ExternalLoginDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ExternalLoginDto>>> List(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.ListAsync(ct));
    }

    private void GuardOrigin()
    {
        if ((!environment.IsDevelopment() && !Request.IsHttps)
            || !options.Value.AllowedOrigins.Contains(Request.Headers.Origin.ToString(), StringComparer.Ordinal))
            throw new ForbiddenException("An allowed browser origin and secure connection are required.");
    }

    private string Binding() => Request.Cookies.TryGetValue(BindingCookie, out var value) && value.Length == 64
        ? value : throw new UnauthorizedException("Google login challenge is missing.");
}

public sealed record GoogleChallengeInput([Required, RegularExpression("^(LOGIN|LINK)$")] string Purpose);
public record GoogleLoginInput(Guid ChallengeId, [Required, StringLength(16384)] string IdToken);
public sealed record GoogleLinkInput(Guid ChallengeId, [Required, StringLength(16384)] string IdToken,
    [Required, StringLength(1024)] string CurrentPassword);
public sealed record GoogleUnlinkInput([Required, StringLength(1024)] string CurrentPassword);
