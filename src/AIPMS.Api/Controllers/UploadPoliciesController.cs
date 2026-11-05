using AIPMS.Application.Features.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/upload-policies")]
[Route("v1/upload-policies")]
public sealed class UploadPoliciesController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UploadPolicyDto>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<UploadPolicyDto>> GetAll() => Ok(UploadPolicies.All);

    [HttpGet("{parentType}")]
    [ProducesResponseType<UploadPolicyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public ActionResult<UploadPolicyDto> GetByParentType(string parentType)
    {
        var policy = UploadPolicies.Find(parentType);
        return policy is not null ? Ok(policy) : NotFound(new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Not Found",
            Detail = $"No upload policy found for parent type '{parentType}'.",
            Instance = Request.Path
        });
    }
}
