using System.Data;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.TaskComments.Abstractions;
using AIPMS.Application.Features.TaskComments.DTOs;
using AIPMS.Application.Features.TaskComments.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using TaskCommentEntity = AIPMS.Infrastructure.Persistence.Generated.Models.TaskComment;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class TaskCommentRepository(AipmsDbContext db) : ITaskCommentRepository
{
    public async System.Threading.Tasks.Task<T> InTransactionAsync<T>(Func<CancellationToken, System.Threading.Tasks.Task<T>> action, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try { var result = await action(ct); await tx.CommitAsync(ct); return result; }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            for (Exception? cause = ex; cause is not null; cause = cause.InnerException)
                if (cause is DbUpdateConcurrencyException || cause is SqlException { Number: 1205 or 1222 or 547 })
                    throw new ConflictException("The task or project changed. Reload and retry.");
            throw;
        }
    }

    public async System.Threading.Tasks.Task LockProjectAsync(long projectId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("A transaction is required.");
        var rows = await db.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {projectId}").ToListAsync(ct);
        if (rows.Count == 0) throw new NotFoundException("Project", projectId);
    }

    public System.Threading.Tasks.Task<TaskCommentAccess?> GetAccessAsync(long taskId, long actorId, CancellationToken ct) =>
        db.Tasks.AsNoTracking().Where(t => t.Id == taskId).Select(t => new TaskCommentAccess(
            t.Id, t.Milestone.ProjectId, t.Milestone.Project.Status,
            db.Users.Any(u => u.Id == actorId && u.Status == "ACTIVE" && u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT")
                && t.Milestone.Project.Team.TeamMembers.Any(m => m.UserId == actorId && m.LeftAt == null)),
            db.Users.Any(u => u.Id == actorId && u.Status == "ACTIVE" && u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT")
                && t.Milestone.Project.Team.TeamMembers.Any(m => m.UserId == actorId && m.LeftAt == null && m.IsLeader)),
            db.Users.Any(u => u.Id == actorId && u.Status == "ACTIVE" && u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER")
                && db.SupervisorAssignments.Any(a => a.ProjectId == t.Milestone.ProjectId && a.EndedAt == null && a.SupervisorProfile.UserId == actorId)))).SingleOrDefaultAsync(ct);

    public async System.Threading.Tasks.Task<PagedResult<TaskCommentDto>> ListAsync(TaskCommentSearch search, CancellationToken ct)
    {
        var q = db.TaskComments.AsNoTracking().Where(c => c.TaskId == search.TaskId);
        var count = await q.LongCountAsync(ct);
        var items = await q.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).Skip((search.Page - 1) * search.PageSize).Take(search.PageSize)
            .Select(c => new TaskCommentDto(c.Id, c.TaskId, c.AuthorId, c.Author.FullName, c.Content, c.CreatedAt, c.UpdatedAt)).ToListAsync(ct);
        return new(items, search.Page, search.PageSize, count);
    }

    public async System.Threading.Tasks.Task<TaskCommentDto> CreateAsync(long taskId, long actorId, string content, DateTime now, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Task comments require a transaction.");
        var entity = new TaskCommentEntity { TaskId = taskId, AuthorId = actorId, Content = content, CreatedAt = now, UpdatedAt = now };
        db.TaskComments.Add(entity);
        await db.SaveChangesAsync(ct);
        return await db.TaskComments.AsNoTracking().Where(c => c.Id == entity.Id)
            .Select(c => new TaskCommentDto(c.Id, c.TaskId, c.AuthorId, c.Author.FullName, c.Content, c.CreatedAt, c.UpdatedAt)).SingleAsync(ct);
    }
}

