namespace AIPMS.Domain.Supervisors;

public sealed record SupervisorCapacity(
    int? ProfileLimit, int SemesterLimit, int ActiveProjects, int SemesterActiveProjects)
{
    public int RemainingSlots => SemesterLimit < 1 || ProfileLimit is <= 0
        ? 0
        : Math.Max(0, Math.Min(
            ProfileLimit.HasValue ? ProfileLimit.Value - ActiveProjects : int.MaxValue,
            SemesterLimit - SemesterActiveProjects));
}
