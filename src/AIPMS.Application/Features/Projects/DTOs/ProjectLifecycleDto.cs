namespace AIPMS.Application.Features.Projects.DTOs;

public sealed record ProjectLifecycleDto(IReadOnlyList<ProjectStateDto> States);

public sealed record ProjectStateDto(string Name, IReadOnlyList<string> AllowedNextStates);
