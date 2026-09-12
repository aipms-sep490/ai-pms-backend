using AIPMS.Application.Features.FinalSubmissions.Abstractions;
using AIPMS.Application.Features.FinalSubmissions.Models;

namespace AIPMS.Application.Features.FinalSubmissions.Services;

public static class FinalSubmissionRules
{
    public static async Task<IReadOnlyList<string>> Blockers(IFinalSubmissionDraftRepository repository, FinalDraftProject project, FinalDraftPeriod? period, DateTime now, CancellationToken ct, int? openCount = null)
    {
        var result = new List<string>();
        if (!project.IsLeader) result.Add("LEADER_REQUIRED");
        if (project.Status != "ACTIVE") result.Add("PROJECT_NOT_ACTIVE");
        if (!project.AcademicScopeActive) result.Add("ACADEMIC_SCOPE_INACTIVE");
        if (period is null || period.SemesterId != project.SemesterId || period.Type != "FINAL_SUBMISSION")
            result.Add("FINAL_SUBMISSION_PERIOD_REQUIRED");
        else
        {
            if (!period.SemesterOpen) result.Add("SEMESTER_CLOSED");
            if (period.Status != "ACTIVE") result.Add("PERIOD_INACTIVE");
            if (now < period.StartAt) result.Add("WINDOW_NOT_STARTED");
            if (now >= period.EndAt) result.Add("WINDOW_CLOSED");
            if (period.StartAt <= now && now < period.EndAt
                && (openCount ?? await repository.CountOpenPeriodsAsync(project.SemesterId, now, ct)) != 1)
                result.Add("AMBIGUOUS_FINAL_SUBMISSION_WINDOW");
        }
        return result;
    }

}
