using System.Text.RegularExpressions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class RubricDatabaseFixture : IAsyncLifetime
{
    private readonly SupervisorDatabaseFixture database = new();
    public string ConnectionString => database.ConnectionString;
    public AipmsDbContext CreateContext() => database.CreateContext();
    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        try { await Migrate(); }
        catch { await database.DisposeAsync(); throw; }
    }
    public Task DisposeAsync() => database.DisposeAsync();

    public async Task Migrate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "db", "changes"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var script = await File.ReadAllTextAsync(Path.Combine(directory.FullName, "db", "changes", "20260911_add_rubric_versions.sql"));
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task<RubricScenario> Seed()
    {
        var users = await database.SeedAsync();
        await using var db = CreateContext();
        var orgId = await db.Departments.Where(d => d.Id == users.DepartmentId).Select(d => d.OrganizationId).SingleAsync();
        var semester = new M.AcademicSemester { OrganizationId = orgId, Code = Guid.NewGuid().ToString("N"),
            Name = "Rubric Semester", Status = "ACTIVE", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) };
        db.AcademicSemesters.Add(semester);
        await db.SaveChangesAsync();
        return new(users, semester.Id, orgId);
    }
}

public sealed record RubricScenario(SupervisorScenario Users, long SemesterId, long OrganizationId);
