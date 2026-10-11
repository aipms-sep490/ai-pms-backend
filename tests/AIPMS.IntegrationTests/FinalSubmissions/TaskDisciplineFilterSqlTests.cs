using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Tasks.DTOs;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.FinalSubmissions;

public sealed partial class FinalSubmissionEndpointTests
{
    [Fact]
    public async Task Task_major_and_role_match_same_discipline_before_sql_count_and_paging()
    {
        using var app = new Factory(database);
        var s = await database.Seed();
        await SeedEvaluationRoster(s, s.PeriodId);
        long major, supportingId;
        await using (var db = database.CreateContext())
        {
            major = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();
            var other = new M.Major { DepartmentId = s.Users.DepartmentId, Code = Guid.NewGuid().ToString("N"), Name = "Other", IsActive = true };
            var milestone = new M.Milestone { ProjectId = s.ProjectId, Title = "Filter", Status = "PLANNED", CreatedBy = s.Users.Student };
            db.Add(other); db.Add(milestone); await db.SaveChangesAsync();
            var first = new M.Task { MilestoneId = milestone.Id, Title = "First", Status = "TODO", Priority = "NORMAL", CreatedBy = s.Users.Student };
            var second = new M.Task { MilestoneId = milestone.Id, Title = "Second", Status = "TODO", Priority = "NORMAL", CreatedBy = s.Users.Student };
            db.AddRange(first, second); await db.SaveChangesAsync();
            db.TaskDisciplines.AddRange(
                new() { TaskId = first.Id, MajorId = major, Role = "PRIMARY", CreatedBy = s.Users.Student },
                new() { TaskId = first.Id, MajorId = other.Id, Role = "SUPPORTING", CreatedBy = s.Users.Student },
                new() { TaskId = second.Id, MajorId = other.Id, Role = "PRIMARY", CreatedBy = s.Users.Student },
                new() { TaskId = second.Id, MajorId = major, Role = "SUPPORTING", CreatedBy = s.Users.Student });
            await db.SaveChangesAsync(); supportingId = second.Id;
        }
        using var student = app.CreateAuthenticatedClient(s.Users.Student);
        var url = $"/api/v1/tasks/project/{s.ProjectId}?majorId={major}&disciplineRole=SUPPORTING&pageSize=1";
        var result = await Body<PagedResult<TaskDto>>(await student.GetAsync(url));
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(supportingId, Assert.Single(result.Items).Id);
        result = await Body<PagedResult<TaskDto>>(await student.GetAsync(url + "&page=2"));
        Assert.Equal(1, result.TotalCount); Assert.Empty(result.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.GetAsync(url.Replace("SUPPORTING", "SECONDARY"))).StatusCode);
        using var foreign = app.CreateAuthenticatedClient(s.Users.OtherLecturer);
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.GetAsync(url)).StatusCode);
    }
}
