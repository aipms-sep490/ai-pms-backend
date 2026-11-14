using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Supervisors;

public sealed class SupervisorEndpointTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static Task<HttpResponseMessage> Update(HttpClient client, long userId, string? bio = "Updated", bool available = true) =>
        client.PutAsJsonAsync($"/api/v1/supervisors/users/{userId}/profile", new UpdateSupervisorProfileRequest(bio, available));
    private static Task<HttpResponseMessage> Expertise(HttpClient client, long id, params SupervisorExpertiseDto[] items) =>
        client.PutAsJsonAsync($"/api/v1/supervisors/{id}/expertise", new ReplaceSupervisorExpertiseRequest(items));

    [Fact]
    public async Task Directory_requires_auth_and_filters_with_stable_pagination()
    {
        var s = await database.SeedAsync();
        using var app = new SupervisorFactory(database);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/supervisors")).StatusCode);
        using var student = app.CreateAuthenticatedClient(s.Student);
        var page = await Body<PagedResult<SupervisorProfileDto>>(await student.GetAsync(
            $"/api/v1/supervisors?departmentId={s.DepartmentId}&expertise=Software&isAvailable=true&pageSize=1"));
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(s.ProfileId, Assert.Single(page.Items).Id);
        var detail = await Body<SupervisorProfileDto>(await student.GetAsync($"/api/v1/supervisors/{s.ProfileId}"));
        Assert.Equal("Original", detail.Bio);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.GetAsync("/api/v1/supervisors?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync("/api/v1/supervisors/9223372036854775807")).StatusCode);
    }

    [Fact]
    public async Task Academic_directories_enforce_persisted_organization_and_department_on_sql()
    {
        var s = await database.SeedAsync();
        long foreignProfileId, sameOrganizationProfileId, foreignSemesterId, foreignPeriodId, ownSemesterId, ownPeriodId;
        string uniqueSearch;
        await using (var context = database.CreateContext())
        {
            var suffix = Guid.NewGuid().ToString("N");
            uniqueSearch = suffix;
            var ownDepartment = await context.Departments.SingleAsync(d => d.Id == s.DepartmentId);
            var foreignOrganization = new Organization { Code = $"ORG_{suffix}", Name = "Foreign", IsActive = true };
            var foreignDepartment = new Department { Code = $"D_{suffix}", Name = "Foreign", IsActive = true, Organization = foreignOrganization };
            var lecturerRoleId = await context.Roles.Where(r => r.Code == AppRoles.Lecturer).Select(r => r.Id).SingleAsync();
            var foreignLecturer = new User
            {
                Email = $"lecturer-{suffix}@example.test", FullName = $"Foreign lecturer {suffix}", PasswordHash = "unused",
                Status = "ACTIVE", AcademicProfileStatus = "VERIFIED", Department = foreignDepartment,
                UserRoleUsers = [new() { RoleId = lecturerRoleId }]
            };
            var foreignProfile = new SupervisorProfile { User = foreignLecturer, IsAvailable = true };
            var sameOrganizationProfile = new SupervisorProfile
            {
                UserId = s.OtherLecturer, IsAvailable = true, Bio = $"Same organization {suffix}"
            };
            context.SupervisorProfiles.AddRange(foreignProfile, sameOrganizationProfile);
            var ownSemester = new AcademicSemester
            {
                OrganizationId = ownDepartment.OrganizationId, Code = $"OWN_{suffix}", Name = "Own semester",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1), Status = "ACTIVE"
            };
            var foreignSemester = new AcademicSemester
            {
                Organization = foreignOrganization, Code = $"FOREIGN_{suffix}", Name = "Foreign semester",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 1), Status = "ACTIVE"
            };
            var ownPeriod = new ProjectPeriod
            {
                AcademicSemester = ownSemester, Code = $"OWN_{suffix}", Name = "Own period",
                PeriodType = "REGISTRATION", Status = "ACTIVE", StartAt = DateTime.UtcNow,
                EndAt = DateTime.UtcNow.AddDays(30)
            };
            var foreignPeriod = new ProjectPeriod
            {
                AcademicSemester = foreignSemester, Code = $"FOREIGN_{suffix}", Name = "Foreign period",
                PeriodType = "REGISTRATION", Status = "ACTIVE", StartAt = DateTime.UtcNow,
                EndAt = DateTime.UtcNow.AddDays(30)
            };
            context.ProjectPeriods.AddRange(ownPeriod, foreignPeriod);
            await context.SaveChangesAsync();
            (foreignProfileId, sameOrganizationProfileId, foreignSemesterId, foreignPeriodId, ownSemesterId, ownPeriodId) =
                (foreignProfile.Id, sameOrganizationProfile.Id, foreignSemester.Id, foreignPeriod.Id, ownSemester.Id, ownPeriod.Id);
        }

        using var app = new SupervisorFactory(database);
        using var student = app.CreateAuthenticatedClient(s.Student, roles: AppRoles.Student);
        using var staff = app.CreateAuthenticatedClient(s.Staff, roles: AppRoles.DepartmentStaff);
        using var admin = app.CreateAuthenticatedClient(s.Admin, roles: AppRoles.Admin);

        var studentSupervisors = await Body<PagedResult<SupervisorProfileDto>>(await student.GetAsync("/api/v1/supervisors?pageSize=100"));
        Assert.DoesNotContain(studentSupervisors.Items, p => p.Id == foreignProfileId);
        Assert.Equal(0, (await Body<PagedResult<SupervisorProfileDto>>(await student.GetAsync($"/api/v1/supervisors?search={uniqueSearch}"))).TotalCount);
        Assert.Equal(1, (await Body<PagedResult<SupervisorProfileDto>>(await admin.GetAsync($"/api/v1/supervisors?search={uniqueSearch}"))).TotalCount);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/api/v1/supervisors/{foreignProfileId}")).StatusCode);
        var staffSupervisors = await Body<PagedResult<SupervisorProfileDto>>(await staff.GetAsync("/api/v1/supervisors?pageSize=100"));
        Assert.Contains(staffSupervisors.Items, p => p.Id == s.ProfileId);
        Assert.All(staffSupervisors.Items, p => Assert.Equal(s.DepartmentId, p.DepartmentId));
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/v1/supervisors/{foreignProfileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync($"/api/v1/supervisors/{sameOrganizationProfileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/v1/supervisors/{sameOrganizationProfileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/supervisors/{foreignProfileId}")).StatusCode);

        var studentSemesterPage = await Body<PagedResult<SemesterDto>>(await student.GetAsync($"/api/v1/academic/semesters?search={uniqueSearch}"));
        var adminSemesterPage = await Body<PagedResult<SemesterDto>>(await admin.GetAsync($"/api/v1/academic/semesters?search={uniqueSearch}"));
        Assert.Equal(1, studentSemesterPage.TotalCount);
        Assert.Equal(2, adminSemesterPage.TotalCount);
        var studentPeriodPage = await Body<PagedResult<ProjectPeriodDto>>(await student.GetAsync($"/api/v1/academic/project-periods?search={uniqueSearch}"));
        var adminPeriodPage = await Body<PagedResult<ProjectPeriodDto>>(await admin.GetAsync($"/api/v1/academic/project-periods?search={uniqueSearch}"));
        Assert.Equal(1, studentPeriodPage.TotalCount);
        Assert.Equal(2, adminPeriodPage.TotalCount);

        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync($"/api/v1/academic/semesters/{ownSemesterId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/api/v1/academic/semesters/{foreignSemesterId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await student.GetAsync($"/api/v1/academic/project-periods/{ownPeriodId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/api/v1/academic/project-periods/{foreignPeriodId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/academic/project-periods/{foreignPeriodId}")).StatusCode);
    }

    [Fact]
    public async Task Profile_upsert_and_expertise_persist_without_changing_capacity()
    {
        var s = await database.SeedAsync();
        using var app = new SupervisorFactory(database);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer, roles: AppRoles.Lecturer);
        var profile = await Body<SupervisorProfileDto>(await Update(lecturer, s.Lecturer, " Trimmed ", false));
        Assert.Equal("Trimmed", profile.Bio);
        Assert.False(profile.IsAvailable);
        var changed = await Body<SupervisorProfileDto>(await Expertise(lecturer, s.ProfileId,
            new(" AI ", " Advanced "), new("Backend", null)));
        Assert.Equal(new[] { "AI", "Backend" }, changed.Expertise.Select(e => e.Name));
        await using var context = database.CreateContext();
        Assert.Equal(3, (await context.SupervisorProfiles.FindAsync(s.ProfileId))!.MaxActiveProjects);
        Assert.Equal(2, await context.SupervisorExpertises.CountAsync(e => e.SupervisorProfileId == s.ProfileId));
        var audit = await context.AuditLogs.SingleAsync(a => a.EntityId == s.ProfileId.ToString() && a.Action == "SUPERVISOR_PROFILE_UPDATED");
        Assert.Equal(s.Lecturer, audit.ActorUserId);
        Assert.Contains("Original", audit.DetailsJson!);
        Assert.Contains("Trimmed", audit.DetailsJson!);
        await Body<SupervisorProfileDto>(await Expertise(lecturer, s.ProfileId));
        Assert.Empty(await context.SupervisorExpertises.AsNoTracking().Where(e => e.SupervisorProfileId == s.ProfileId).ToListAsync());
    }

    [Fact]
    public async Task First_update_provisions_one_profile_and_preserves_false_availability()
    {
        var s = await database.SeedAsync();
        using var app = new SupervisorFactory(database);
        using var lecturer = app.CreateAuthenticatedClient(s.NewLecturer, roles: AppRoles.Lecturer);
        var first = await Body<SupervisorProfileDto>(await Update(lecturer, s.NewLecturer, null, false));
        Assert.False(first.IsAvailable);
        var second = await Body<SupervisorProfileDto>(await Update(lecturer, s.NewLecturer, "New", true));
        Assert.Equal(first.Id, second.Id);
        await using var context = database.CreateContext();
        Assert.Equal(1, await context.SupervisorProfiles.CountAsync(p => p.UserId == s.NewLecturer));
        Assert.Null((await context.SupervisorProfiles.FindAsync(first.Id))!.MaxActiveProjects);
    }

    [Fact]
    public async Task Mutations_enforce_owner_and_department_scope_even_with_stale_role_claims()
    {
        var s = await database.SeedAsync();
        using var app = new SupervisorFactory(database);
        using var student = app.CreateAuthenticatedClient(s.Student, roles: AppRoles.Admin);
        using var other = app.CreateAuthenticatedClient(s.OtherLecturer, roles: AppRoles.Lecturer);
        using var outside = app.CreateAuthenticatedClient(s.OutsideStaff, roles: AppRoles.DepartmentStaff);
        using var staff = app.CreateAuthenticatedClient(s.Staff, roles: AppRoles.DepartmentStaff);
        using var admin = app.CreateAuthenticatedClient(s.Admin, roles: AppRoles.Admin);
        foreach (var client in new[] { student, other, outside })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await Update(client, s.Lecturer)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Expertise(client, s.ProfileId, new SupervisorExpertiseDto("AI", null))).StatusCode);
        }
        await Body<SupervisorProfileDto>(await Update(staff, s.Lecturer));
        await Body<SupervisorProfileDto>(await Expertise(staff, s.ProfileId, new SupervisorExpertiseDto("AI", null)));
        await Body<SupervisorProfileDto>(await Update(admin, s.OtherLecturer));
        Assert.Equal(HttpStatusCode.Conflict, (await Update(admin, s.Student)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Update(admin, long.MaxValue)).StatusCode);
    }

    [Fact]
    public async Task Invalid_expertise_returns_problem_details_without_deleting_existing_rows()
    {
        var s = await database.SeedAsync();
        using var app = new SupervisorFactory(database);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer, roles: AppRoles.Lecturer);
        var response = await Expertise(lecturer, s.ProfileId, new("AI", null), new(" ai ", null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.True(problem!.Extensions.ContainsKey("errors"));
        Assert.Equal(HttpStatusCode.BadRequest, (await lecturer.PutAsJsonAsync($"/api/v1/supervisors/{s.ProfileId}/expertise", new { expertise = (object?)null })).StatusCode);
        await using var context = database.CreateContext();
        Assert.Equal("Software Engineering", (await context.SupervisorExpertises.SingleAsync(e => e.SupervisorProfileId == s.ProfileId)).ExpertiseName);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("department")]
    [InlineData("role")]
    public async Task Directory_hides_inactive_or_non_lecturer_profiles(string change)
    {
        var s = await database.SeedAsync();
        await using (var context = database.CreateContext())
        {
            if (change == "account") (await context.Users.FindAsync(s.Lecturer))!.Status = "INACTIVE";
            if (change == "department") (await context.Departments.FindAsync(s.DepartmentId))!.IsActive = false;
            if (change == "role") context.UserRoles.RemoveRange(await context.UserRoles.Where(r => r.UserId == s.Lecturer).ToListAsync());
            await context.SaveChangesAsync();
        }
        using var app = new SupervisorFactory(database);
        using var student = app.CreateAuthenticatedClient(s.Student);
        if (change == "department")
            Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/v1/supervisors/{s.ProfileId}")).StatusCode);
        using var admin = app.CreateAuthenticatedClient(s.Admin, roles: AppRoles.Admin);
        var reader = change == "department" ? admin : student;
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/api/v1/supervisors/{s.ProfileId}")).StatusCode);
        var page = await Body<PagedResult<SupervisorProfileDto>>(await reader.GetAsync($"/api/v1/supervisors?departmentId={s.DepartmentId}"));
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Failed_audit_rolls_back_profile_creation_update_and_expertise_replacement()
    {
        var s = await database.SeedAsync();
        using var app = new SupervisorFactory(database, true);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer, roles: AppRoles.Lecturer);
        using var newLecturer = app.CreateAuthenticatedClient(s.NewLecturer, roles: AppRoles.Lecturer);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Update(newLecturer, s.NewLecturer)).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Update(lecturer, s.Lecturer)).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Expertise(lecturer, s.ProfileId, new SupervisorExpertiseDto("AI", null))).StatusCode);
        await using var context = database.CreateContext();
        Assert.False(await context.SupervisorProfiles.AnyAsync(p => p.UserId == s.NewLecturer));
        Assert.Equal("Original", (await context.SupervisorProfiles.FindAsync(s.ProfileId))!.Bio);
        Assert.Equal("Software Engineering", (await context.SupervisorExpertises.SingleAsync(e => e.SupervisorProfileId == s.ProfileId)).ExpertiseName);
        Assert.False(await context.AuditLogs.AnyAsync(a => a.EntityId == s.ProfileId.ToString() && a.EntityType == "SUPERVISOR_PROFILE"));
    }

    [Fact]
    public async Task Concurrent_provision_keeps_one_profile_and_returns_success_or_conflict()
    {
        var s = await database.SeedAsync();
        using var app = new SupervisorFactory(database, saveInterceptor: new ProfileCreationBarrier(4));
        using var lecturer = app.CreateAuthenticatedClient(s.NewLecturer, roles: AppRoles.Lecturer);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Update(lecturer, s.NewLecturer, $"Bio {i}")));
        var success = Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.Equal(409, problem!.Status);
            Assert.True(problem.Extensions.ContainsKey("traceId"));
        }
        var winner = await Body<SupervisorProfileDto>(success);
        await using var context = database.CreateContext();
        var profile = await context.SupervisorProfiles.SingleAsync(p => p.UserId == s.NewLecturer);
        Assert.Equal(winner.Id, profile.Id);
        Assert.Equal(winner.Bio, profile.Bio);
        var audit = await context.AuditLogs.SingleAsync(a => a.ActorUserId == s.NewLecturer
            && a.EntityType == "SUPERVISOR_PROFILE");
        Assert.Equal("SUPERVISOR_PROFILE_CREATED", audit.Action);
        Assert.Equal(profile.Id.ToString(), audit.EntityId);
        Assert.Contains(winner.Bio!, audit.DetailsJson!);

        var retry = await Body<SupervisorProfileDto>(await Update(lecturer, s.NewLecturer, "Retried"));
        Assert.Equal(profile.Id, retry.Id);
        Assert.Equal("Retried", retry.Bio);
    }

    [Fact]
    public async Task Concurrent_read_then_update_returns_conflict_not_500()
    {
        var s = await database.SeedAsync();
        var bothRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readers = 0;
        async Task<object> Write(string bio)
        {
            await using var context = database.CreateContext();
            var repository = new SupervisorProfileRepository(context);
            try
            {
                return await repository.InTransactionAsync(async () =>
                {
                    // Both transactions hold the same shared locks before either attempts an update.
                    await repository.GetAccountAsync(s.Lecturer, default);
                    await repository.GetAsync(s.ProfileId, default);
                    if (Interlocked.Increment(ref readers) == 2) bothRead.SetResult();
                    await bothRead.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    return await repository.UpsertAsync(s.Lecturer, bio, true, DateTime.UtcNow, default);
                }, default);
            }
            catch (Exception ex) { return ex; }
        }
        var results = await Task.WhenAll(Write("A"), Write("B"));
        Assert.Single(results.OfType<ConflictException>());
        Assert.Single(results.OfType<AIPMS.Application.Features.Supervisors.Models.SupervisorProfileModel>());
    }

    private sealed class ProfileCreationBarrier(int participants) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource allReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int ready;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker
                .Entries<AIPMS.Infrastructure.Persistence.Generated.Models.SupervisorProfile>()
                .Any(e => e.State == EntityState.Added))
            {
                // Hold every request after it reads the absent profile, before any insert can win.
                if (Interlocked.Increment(ref ready) == participants) allReady.TrySetResult();
                await allReady.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}

internal sealed class SupervisorFactory(SupervisorDatabaseFixture database, bool failAudit = false,
    IInterceptor? saveInterceptor = null, TimeProvider? clock = null) : AipmsWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = database.ConnectionString }));
        if (clock is not null) builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        if (saveInterceptor is not null) builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AipmsDbContext>>();
            services.AddDbContext<AipmsDbContext>(options => options.UseSqlServer(database.ConnectionString)
                .AddInterceptors(saveInterceptor));
        });
        if (failAudit) builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuditTrail>();
            services.AddScoped<IAuditTrail, FailingAudit>();
        });
    }
    private sealed class FailingAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated audit failure");
    }
}
