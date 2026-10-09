using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.StudentRosters;
using AIPMS.Application.Features.StudentRosters.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize(Roles = AppRoles.Admin)]
[Route("api/v1/users/curriculum-import")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(409)]
public sealed class StudentCurriculumController(IStudentRosterService service) : ControllerBase
{
    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    [ProducesResponseType<CurriculumPreviewDto>(200)]
    [ProducesResponseType<ProblemDetails>(413)]
    public async Task<ActionResult<CurriculumPreviewDto>> Preview(IFormFile file, CancellationToken ct)
    {
        await using var content = file.OpenReadStream();
        return Ok(await service.PreviewAsync(content, file.FileName, ct));
    }

    [HttpPost("commit")]
    [ProducesResponseType<CurriculumCommitDto>(200)]
    public async Task<ActionResult<CurriculumCommitDto>> Commit(CurriculumCommitRequest request, CancellationToken ct) =>
        Ok(await service.CommitAsync(request, ct));
}
