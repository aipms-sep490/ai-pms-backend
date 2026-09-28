namespace AIPMS.Application.Abstractions.Projects;

public enum ExecutionResource { Task, Milestone, ProgressReport, Meeting }

public sealed record ExecutionWrite(ExecutionResource Resource, long Id, string? ConcurrencyToken);

public interface IExecutionMutation
{
    IReadOnlyList<ExecutionWrite> Writes { get; }
}
