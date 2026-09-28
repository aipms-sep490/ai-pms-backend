using AIPMS.Application.Abstractions.Projects;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ExecutionConcurrencyBehavior<TRequest, TResponse>(
    AipmsDbContext db, IConfiguration configuration, ICurrentUser actor, IProjectAccessService access)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not IExecutionMutation mutation) return await next();
        var writes = mutation.Writes;
        var required = bool.TryParse(configuration["ExecutionConcurrency:RequireTokens"], out var configured) && configured;
        // Legacy clients may omit tokens, but mutations and audit still commit together.
        var userId = actor.UserId ?? throw new UnauthorizedException();
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var projects = new Dictionary<(ExecutionResource, long), long>();
            foreach (var write in writes.DistinctBy(w => (w.Resource, w.Id)))
            {
                long? projectId = write.Resource switch
                {
                    ExecutionResource.Task => await db.Tasks.Where(x => x.Id == write.Id).Select(x => (long?)x.Milestone.ProjectId).SingleOrDefaultAsync(ct),
                    ExecutionResource.Milestone => await db.Milestones.Where(x => x.Id == write.Id).Select(x => (long?)x.ProjectId).SingleOrDefaultAsync(ct),
                    ExecutionResource.ProgressReport => await db.ProgressReports.Where(x => x.Id == write.Id).Select(x => (long?)x.ProjectId).SingleOrDefaultAsync(ct),
                    ExecutionResource.Meeting => await db.Meetings.Where(x => x.Id == write.Id).Select(x => (long?)x.ProjectId).SingleOrDefaultAsync(ct),
                    _ => throw new InvalidOperationException("Unknown execution resource.")
                };
                if (projectId is null) throw new NotFoundException(write.Resource.ToString(), write.Id);
                projects[(write.Resource, write.Id)] = projectId.Value;
            }
            // Match lifecycle lock order (team, project) before locking child aggregates.
            var teamIds = await db.Projects.Where(p => projects.Values.Distinct().Contains(p.Id))
                .Select(p => p.TeamId).Distinct().OrderBy(x => x).ToArrayAsync(ct);
            foreach (var id in teamIds)
                await db.Teams.FromSqlInterpolated($"SELECT * FROM dbo.teams WITH (UPDLOCK,HOLDLOCK) WHERE id={id}").AsNoTracking().SingleAsync(ct);
            foreach (var id in projects.Values.Distinct().Order())
            {
                var project = await db.Projects.FromSqlInterpolated($"SELECT * FROM dbo.projects WITH (UPDLOCK,HOLDLOCK) WHERE id={id}").AsNoTracking().SingleAsync(ct);
                if (!await access.CanAccessAsync(userId, id, ct)) throw new ForbiddenException();
                if (project.Status != "ACTIVE") throw new ConflictException("Project is not ACTIVE.");
            }
            foreach (var write in writes.OrderBy(w => w.Resource).ThenBy(w => w.Id))
            {
                if (!required && write.ConcurrencyToken is null) continue;
                if (!Guid.TryParse(write.ConcurrencyToken, out var expected))
                    throw new ConflictException("A current concurrencyToken is required. Reload the resource.", WorkflowErrorCodes.StaleConcurrencyToken);
                var table = write.Resource switch
                {
                    ExecutionResource.Task => "tasks", ExecutionResource.Milestone => "milestones",
                    ExecutionResource.ProgressReport => "progress_reports", ExecutionResource.Meeting => "meetings",
                    _ => throw new InvalidOperationException()
                };
                // The table comes only from the fixed enum; all values remain SQL parameters.
                #pragma warning disable EF1002
                var changed = await db.Database.ExecuteSqlRawAsync(
                    $"UPDATE dbo.{table} SET concurrency_token=NEWID() WHERE id=@id AND concurrency_token=@token",
                    [new SqlParameter("@id", write.Id), new SqlParameter("@token", expected)], ct);
                #pragma warning restore EF1002
                if (changed != 1) throw new ConflictException("The resource changed. Reload before saving.", WorkflowErrorCodes.StaleConcurrencyToken);
            }
            var result = await next();
            if (tx is not null) await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException || ex is SqlException { Number: 1205 })
                throw new ConflictException("The resource changed concurrently. Reload before saving.", WorkflowErrorCodes.StaleConcurrencyToken);
            throw;
        }
    }
}
