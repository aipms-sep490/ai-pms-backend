using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.Results.Abstractions;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Results.Services;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ResultHistoryService(AipmsDbContext db, ProjectResultWorkflow projects, IEvaluationSchemeService students) : IResultHistoryService
{
    public async Task<PagedResult<ResultRevisionSummary>> History(long projectId, long? studentId, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 1000 || pageSize is < 1 or > 100)
            throw new ValidationException(new Dictionary<string, string[]> { ["page"] = ["Page 1..1000 and pageSize 1..100 required."] });
        ResultRevisionSummary root;
        if (studentId.HasValue)
        {
            await students.GetStudentAsync(projectId, studentId.Value, ct);
            var original = PublishedResultResolver.Root(await db.Set<StudentResult>().AsNoTracking()
                .SingleAsync(r => r.ProjectId == projectId && r.StudentId == studentId.Value, ct));
            root = Summary(original);
        }
        else
        {
            await projects.Get(projectId, ct);
            root = Summary(PublishedResultResolver.Root(await db.Set<ProjectResult>().AsNoTracking().SingleAsync(r => r.ProjectId == projectId, ct)));
        }
        var key = studentId ?? 0;
        var query = db.Set<ResultRevision>().AsNoTracking().Where(r => r.ProjectId == projectId && r.TargetKey == key);
        var count = await query.CountAsync(ct);
        var offset = (page - 1) * pageSize;
        var rows = await query.OrderByDescending(r => r.Version).Skip(offset).Take(pageSize).ToArrayAsync(ct);
        // Do not expose correction reasons, scorer notes or another student's inputs in history.
        var items = rows.Select(r => studentId.HasValue ? Summary(PublishedResultResolver.Student(r))
            : Summary(PublishedResultResolver.Project(r))).ToList();
        if (offset <= count && offset + pageSize > count) items.Add(root);
        return new(items, page, pageSize, count + 1);
    }
    public async Task<ProjectResultDto> ProjectVersion(long projectId, int version, CancellationToken ct)
    {
        await projects.Get(projectId, ct);
        var dto = version == 1 ? PublishedResultResolver.Root(await db.Set<ProjectResult>().AsNoTracking().SingleAsync(r => r.ProjectId == projectId, ct))
            : PublishedResultResolver.Project(await Revision(projectId, 0, version, ct));
        return dto with { FrozenInputs = null, Breakdown = ResultSnapshotReader.Read(dto.FrozenInputs, dto.CalculationRule) };
    }
    public async Task<StudentResultDto> StudentVersion(long projectId, long studentId, int version, CancellationToken ct)
    {
        await students.GetStudentAsync(projectId, studentId, ct);
        var dto = version == 1 ? PublishedResultResolver.Root(await db.Set<StudentResult>().AsNoTracking()
                .SingleAsync(r => r.ProjectId == projectId && r.StudentId == studentId, ct))
            : PublishedResultResolver.Student(await Revision(projectId, studentId, version, ct));
        return dto with { SnapshotJson = "{}", Breakdown = ResultSnapshotReader.Read(dto.SnapshotJson, dto.CalculationRule, studentId, dto.MajorId) };
    }
    private async Task<ResultRevision> Revision(long project, long target, int version, CancellationToken ct) =>
        await db.Set<ResultRevision>().AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == project && r.TargetKey == target && r.Version == version, ct)
        ?? throw new NotFoundException("ResultVersion", version);
    private static ResultRevisionSummary Summary(StudentResultDto r) => new(r.RevisionId, r.Version, r.SupersedesVersion,
        r.StudentId, r.TotalScore, r.Outcome, r.PublishedBy, r.PublishedAt, null, null);
    private static ResultRevisionSummary Summary(ProjectResultDto r) => new(r.RevisionId, r.Version, r.SupersedesVersion,
        null, r.TotalScore, r.Outcome, r.PublishedBy, r.PublishedAt, null, null);
}
