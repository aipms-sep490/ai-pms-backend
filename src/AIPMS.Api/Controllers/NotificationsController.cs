using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Notifications.Commands;
using AIPMS.Application.Features.Notifications.DTOs;
using AIPMS.Application.Features.Notifications.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/notifications")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<NotificationDto>>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    public async Task<ActionResult<PagedResult<NotificationDto>>> List([FromQuery] bool? isRead = null,
        [FromQuery] string? notificationType = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        Ok(await sender.Send(new GetNotificationsQuery(isRead, notificationType, page, pageSize), ct));

    [HttpGet("unread-count")]
    [ProducesResponseType<UnreadNotificationCountDto>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    public async Task<ActionResult<UnreadNotificationCountDto>> UnreadCount(CancellationToken ct) =>
        Ok(await sender.Send(new GetUnreadNotificationCountQuery(), ct));

    [HttpPatch("{notificationId:long}/read")]
    [ProducesResponseType(204)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<IActionResult> MarkRead(long notificationId, CancellationToken ct)
    {
        await sender.Send(new MarkNotificationReadCommand(notificationId), ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    [ProducesResponseType(204)]
    [ProducesResponseType<ProblemDetails>(401)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await sender.Send(new MarkAllNotificationsReadCommand(), ct);
        return NoContent();
    }
}
