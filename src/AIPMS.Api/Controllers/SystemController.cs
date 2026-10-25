using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/system")]
public sealed class SystemController : ControllerBase
{
    private static readonly string? CachedCommitSha = ResolveCommitSha();

    [HttpGet]
    [ProducesResponseType<SystemInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<SystemInfoResponse> Get() =>
        Ok(new SystemInfoResponse("AI-PMS API", "v1", "ok", CachedCommitSha));

    private static string? ResolveCommitSha()
    {
        var envSha = Environment.GetEnvironmentVariable("COMMIT_SHA")
            ?? Environment.GetEnvironmentVariable("GIT_COMMIT_SHA")
            ?? Environment.GetEnvironmentVariable("APP_COMMIT_SHA");

        if (!string.IsNullOrWhiteSpace(envSha))
        {
            return envSha.Trim();
        }

        var infoVer = typeof(SystemController).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(infoVer))
        {
            var plusIdx = infoVer.IndexOf('+');
            if (plusIdx >= 0 && plusIdx < infoVer.Length - 1)
            {
                return infoVer[(plusIdx + 1)..].Trim();
            }
            return infoVer.Trim();
        }

        return null;
    }
}

public sealed record SystemInfoResponse(string Name, string Version, string Status, string? CommitSha = null);
