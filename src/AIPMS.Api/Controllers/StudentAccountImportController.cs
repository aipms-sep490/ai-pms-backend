using AIPMS.Application.Features.StudentRosters;
using AIPMS.Application.Features.StudentRosters.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

public sealed class StudentAccountImportFileRequest
{
    public IFormFile File { get; set; } = null!;
    public long MajorId { get; set; }
}

[ApiController]
[Authorize(Roles = "ADMIN")]
[Route("api/v1/users/student-import")]
[ProducesResponseType<ProblemDetails>(400)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(409)]
public sealed class StudentAccountImportController(IStudentAccountImportService service) : ControllerBase
{
    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ProducesResponseType<StudentAccountImportPreview>(200)]
    [ProducesResponseType<ProblemDetails>(413)]
    public async Task<ActionResult<StudentAccountImportPreview>> Preview([FromForm] StudentAccountImportFileRequest request, CancellationToken ct)
    {
        if (request.File is null) return BadRequest();
        await using var stream = request.File.OpenReadStream();
        return Ok(await service.PreviewAsync(stream, request.File.FileName, request.MajorId, ct));
    }

    [HttpPost("commit")]
    [ProducesResponseType<StudentAccountImportResult>(201)]
    public async Task<ActionResult<StudentAccountImportResult>> Commit(StudentAccountImportCommit request, CancellationToken ct) =>
        StatusCode(201, await service.CommitAsync(request, ct));
}
