using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.DTOs;
using AIPMS.Application.Features.Industry;
using AIPMS.Application.Features.Industry.Abstractions;
using AIPMS.Infrastructure.Identity.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AIPMS.Api.Controllers;

[ApiController, AllowAnonymous, EnableCors("google-auth"), EnableRateLimiting("authentication")]
[Route("api/v1/auth/industry")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(503)]
public sealed class IndustryEnrollmentController(IIndustryService industry, IOpaqueTokenService opaque,
    IOptions<GoogleAuthSettings> google, IHostEnvironment environment) : ControllerBase
{
    private const string Cookie = "aipms-industry-binding";
    private void Origin()
    {
        if ((!environment.IsDevelopment() && !Request.IsHttps) || !google.Value.AllowedOrigins.Contains(Request.Headers.Origin.ToString(), StringComparer.Ordinal))
            throw new ForbiddenException("Allowed secure browser origin required.");
    }
    [HttpPost("google/challenge")]
    public async Task<GoogleChallenge> Challenge(CancellationToken ct)
    {
        Origin(); var browser = Request.Cookies[Cookie]; if (browser?.Length != 64) browser = opaque.Generate().Value;
        var result = await industry.Challenge(browser, ct);
        Response.Cookies.Append(Cookie, browser, new CookieOptions { HttpOnly = true, Secure = !environment.IsDevelopment() || Request.IsHttps,
            SameSite = SameSiteMode.Strict, Path = "/api/v1/auth/industry", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true });
        return result;
    }
    /// <summary>Consume email invitation plus nonce-bound Google identity. No global auto-link or academic role grant.</summary>
    [HttpPost("google/enroll")]
    [Consumes("application/json")]
    public Task<LoginResponse> Enroll(IndustryEnrollmentInput input, CancellationToken ct)
    {
        Origin(); var browser = Request.Cookies[Cookie]; if (browser?.Length != 64) throw new UnauthorizedException("Enrollment challenge missing.");
        return industry.Enroll(input, browser, ct);
    }
}
