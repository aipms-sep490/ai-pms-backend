using Microsoft.Data.SqlClient;

namespace AIPMS.IntegrationTests.Projects;

public sealed class DisciplineEvidenceMigrationTests
{
    [Fact]
    public async Task Migration_rerun_preserves_legacy_and_enforces_source_primary_and_history_constraints()
    {
        await using var database = new IsolatedSqlDatabase();
        await database.StartAsync(Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION"), bootstrap: false);
        await using var connection = new SqlConnection(database.ConnectionString); await connection.OpenAsync();
        async Task Execute(string sql) { await using var command = new SqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        await Execute("""
            CREATE TABLE users(id bigint PRIMARY KEY); CREATE TABLE majors(id bigint PRIMARY KEY);
            CREATE TABLE projects(id bigint PRIMARY KEY); CREATE TABLE tasks(id bigint PRIMARY KEY);
            CREATE TABLE deliverables(id bigint PRIMARY KEY); CREATE TABLE meetings(id bigint PRIMARY KEY);
            CREATE TABLE progress_reports(id bigint PRIMARY KEY); CREATE TABLE files(id bigint PRIMARY KEY);
            CREATE TABLE team_academic_configurations(team_id bigint PRIMARY KEY);
            CREATE TABLE team_major_requirements(team_id bigint, major_id bigint, PRIMARY KEY(team_id,major_id));
            CREATE TABLE project_registration_snapshots(id bigint PRIMARY KEY, snapshot_json nvarchar(max));
            INSERT users VALUES(1); INSERT majors VALUES(1),(2); INSERT projects VALUES(1); INSERT tasks VALUES(1),(2);
            INSERT files VALUES(1); INSERT team_academic_configurations VALUES(1); INSERT team_major_requirements VALUES(1,1);
            INSERT project_registration_snapshots VALUES(1,N'{"legacy":true}');
            """);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "db", "changes"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var sql = await File.ReadAllTextAsync(Path.Combine(dir.FullName, "db", "changes", "20260929_add_discipline_evidence.sql"));
        await Execute(sql);
        await Execute("""
            INSERT task_disciplines(task_id,major_id,role,created_by) VALUES(1,1,'PRIMARY',1);
            INSERT team_major_responsibilities(team_id,major_id,content,sort_order,created_by) VALUES(1,1,N'Design',0,1);
            INSERT project_evidence(project_id,source_type,source_id,file_id,submitted_by) VALUES(1,'FILE',1,1,1);
            """);
        await Execute(sql);
        foreach (var invalid in new[]
        {
            "INSERT task_disciplines(task_id,major_id,role,created_by) VALUES(1,2,'PRIMARY',1)",
            "INSERT task_disciplines(task_id,major_id,role,created_by) VALUES(1,1,'SUPPORTING',1)",
            "INSERT team_major_responsibilities(team_id,major_id,content,sort_order,created_by) VALUES(1,2,N'Foreign requirement',0,1)",
            "INSERT project_evidence(project_id,source_type,source_id,file_id,submitted_by) VALUES(1,'FILE',1,1,1)",
            "INSERT project_evidence(project_id,source_type,source_id,task_id,file_id,submitted_by) VALUES(1,'TASK',1,1,1,1)",
            "INSERT project_evidence(project_id,source_type,source_id,task_id,submitted_by) VALUES(1,'TASK',2,1,1)",
            "INSERT project_evidence(project_id,source_type,source_id,task_id,submitted_by) VALUES(1,'TASK',999,999,1)",
            "DELETE files WHERE id=1"
        }) await Assert.ThrowsAsync<SqlException>(() => Execute(invalid));
        await using var check = new SqlCommand("""
            SELECT COUNT(*) FROM project_evidence;
            SELECT verification_status,major_id FROM project_evidence;
            SELECT COUNT(*) FROM task_disciplines WHERE task_id=2;
            SELECT responsibility_version FROM team_academic_configurations;
            SELECT snapshot_json FROM project_registration_snapshots;
            """, connection);
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.Equal(1, reader.GetInt32(0));
        Assert.True(await reader.NextResultAsync()); Assert.True(await reader.ReadAsync()); Assert.Equal("PENDING", reader.GetString(0)); Assert.True(reader.IsDBNull(1));
        Assert.True(await reader.NextResultAsync()); Assert.True(await reader.ReadAsync()); Assert.Equal(0, reader.GetInt32(0));
        Assert.True(await reader.NextResultAsync()); Assert.True(await reader.ReadAsync()); Assert.True(reader.IsDBNull(0));
        Assert.True(await reader.NextResultAsync()); Assert.True(await reader.ReadAsync()); Assert.Equal("{\"legacy\":true}", reader.GetString(0));
    }
}
