using System.Collections.Generic;

namespace AIPMS.Application.Features.Tasks.DTOs;

public sealed record OverdueBlockedTasksDto(
    IReadOnlyList<TaskDto> OverdueTasks,
    IReadOnlyList<TaskDto> BlockedTasks);
