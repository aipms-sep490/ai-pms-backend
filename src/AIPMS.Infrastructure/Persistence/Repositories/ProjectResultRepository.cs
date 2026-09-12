using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Results.Abstractions;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class ProjectResultRepository(AipmsDbContext db) : IProjectResultRepository
{
    public Task<bool> AnyFinalizedAsync(long projectId, CancellationToken ct) => db.Evaluations.AnyAsync(e => e.ProjectId == projectId
        && (e.Status != "DRAFT" || db.Set<EvaluationFinalization>().Any(f => f.EvaluationId == e.Id)), ct);
    public Task<bool> IsRequiredAsync(long assignmentId, CancellationToken ct) =>
        db.Set<ProjectResultPolicyItem>().AnyAsync(i => i.AssignmentId == assignmentId, ct);
    public async Task<ResultPolicyDto?> PolicyAsync(long projectId, CancellationToken ct)
    {
        var row = await db.Set<ProjectResultPolicy>().AsNoTracking().Include(p => p.Items).SingleOrDefaultAsync(p => p.ProjectId == projectId, ct);
        return row is null ? null : new(projectId, row.PassThreshold, row.ConcurrencyToken.ToString("N"),
            await AnyFinalizedAsync(projectId, ct) || await db.Set<ProjectResult>().AnyAsync(r => r.ProjectId == projectId, ct),
            row.Items.OrderBy(i => i.AssignmentId).Select(i => new ResultAssignmentInput(i.AssignmentId, i.WeightPercent)).ToArray());
    }
    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Result mutations require a project transaction.");
    }
    public async Task<ResultPolicyDto> ConfigureAsync(long projectId, ConfigureResultPolicyRequest input, long actorId, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        var row = await db.Set<ProjectResultPolicy>().Include(p => p.Items).SingleOrDefaultAsync(p => p.ProjectId == projectId, ct);
        if (row is null) { row = new() { ProjectId = projectId }; db.Set<ProjectResultPolicy>().Add(row); }
        foreach (var item in row.Items.Where(i => input.Assignments.All(a => a.AssignmentId != i.AssignmentId)).ToArray())
        { db.Set<ProjectResultPolicyItem>().Remove(item); row.Items.Remove(item); }
        foreach (var assignment in input.Assignments)
        {
            var item = row.Items.SingleOrDefault(i => i.AssignmentId == assignment.AssignmentId);
            if (item is null) { item = new() { AssignmentId = assignment.AssignmentId }; row.Items.Add(item); }
            item.WeightPercent = assignment.WeightPercent;
        }
        row.PassThreshold = input.PassThreshold; row.ConcurrencyToken = Guid.NewGuid(); row.UpdatedBy = actorId; row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return (await PolicyAsync(projectId, ct))!;
    }
    public async Task<ProjectResultDto?> GetAsync(long projectId, CancellationToken ct)
    {
        var row = await db.Set<ProjectResult>().AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == projectId, ct);
        return row is null ? null : JsonSerializer.Deserialize<ProjectResultDto>(row.SnapshotJson)! with { Id = row.Id };
    }
    public async Task<ProjectResultDto> PublishAsync(ProjectResultDto input, CancellationToken ct)
    {
        RequireTransaction();
        if (await db.Projects.Where(p => p.Id == input.ProjectId && p.Status == "FINAL_SUBMISSION")
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "COMPLETED").SetProperty(p => p.UpdatedAt, input.PublishedAt), ct) != 1)
            throw new ConflictException("Project state changed before publication.");
        var row = new ProjectResult { ProjectId = input.ProjectId, FinalSubmissionId = input.FinalSubmissionId,
            PublishedBy = input.PublishedBy, PublishedAt = input.PublishedAt, SnapshotJson = JsonSerializer.Serialize(input),
            Evaluations = input.Contributions.Select(c => new ProjectResultEvaluation { EvaluationId = c.EvaluationId }).ToArray() };
        db.Set<ProjectResult>().Add(row);
        await db.SaveChangesAsync(ct);
        return input with { Id = row.Id };
    }
}
