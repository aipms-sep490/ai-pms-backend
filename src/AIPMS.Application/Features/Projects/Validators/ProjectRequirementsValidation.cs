using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.DTOs;

namespace AIPMS.Application.Features.Projects.Validators;

public static class ProjectRequirementsValidation
{
    public static void Validate(IReadOnlyList<ProjectMajorRequirementInput>? requirements)
    {
        if (requirements is null || requirements.Count is < 1 or > 100 || requirements.Any(x => x is null
            || x.MajorId <= 0 || x.MinMembers < 1 || x.MaxMembers < x.MinMembers
            || string.IsNullOrWhiteSpace(x.Responsibility) || x.Responsibility.Trim().Length > 2000)
            || requirements.Select(x => x.MajorId).Distinct().Count() != requirements.Count)
            throw new ValidationException(new Dictionary<string, string[]> { ["requirements"] =
                ["Provide 1-100 distinct majors, positive member bounds and a responsibility of 1-2000 characters."] });
    }

    public static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ValidationException(new Dictionary<string, string[]> { ["page"] = ["Invalid pagination; pageSize must be 1-100."] });
    }
}
