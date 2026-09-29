using Microsoft.Data.SqlClient;

namespace AIPMS.IntegrationTests.Projects;

public sealed class ProjectRequirementsMigrationTests
{
    [Fact]
    public async Task Migration_rerun_preserves_legacy_snapshots_and_enforces_constraints()
    {
        await using var database = new IsolatedSqlDatabase();
        await database.StartAsync(Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION"), bootstrap: false);
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        async Task Execute(string sql) { await using var cmd = new SqlCommand(sql, connection); await cmd.ExecuteNonQueryAsync(); }
        await Execute("""
            CREATE TABLE projects(id bigint PRIMARY KEY);
            CREATE TABLE majors(id bigint PRIMARY KEY);
            CREATE TABLE project_registration_snapshots(id bigint PRIMARY KEY, snapshot_json nvarchar(max));
            INSERT projects VALUES (1); INSERT majors VALUES (1);
            INSERT project_registration_snapshots VALUES (1, N'{"legacy":true}');
            """);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "db", "changes"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var sql = await File.ReadAllTextAsync(Path.Combine(dir.FullName, "db", "changes", "20260929_add_project_major_requirements.sql"));
        await Execute(sql);
        await Execute("INSERT project_major_requirements(project_id,major_id,min_members,max_members,responsibility) VALUES(1,1,1,3,N'Engineering')");
        await Execute(sql);
        await Assert.ThrowsAsync<SqlException>(() => Execute("INSERT project_major_requirements(project_id,major_id,min_members,max_members,responsibility) VALUES(1,1,1,3,N'Duplicate')"));
        await Assert.ThrowsAsync<SqlException>(() => Execute("UPDATE project_major_requirements SET min_members=4"));
        await Assert.ThrowsAsync<SqlException>(() => Execute("UPDATE project_major_requirements SET major_id=999"));
        await using var check = new SqlCommand("SELECT COUNT(*) FROM project_major_requirements; SELECT snapshot_json FROM project_registration_snapshots", connection);
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.Equal(1, reader.GetInt32(0));
        Assert.True(await reader.NextResultAsync()); Assert.True(await reader.ReadAsync()); Assert.Equal("{\"legacy\":true}", reader.GetString(0));
    }
}
