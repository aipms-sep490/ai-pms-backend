using Microsoft.Data.SqlClient;

namespace AIPMS.IntegrationTests.Supervisors;

public sealed class GovernanceMigrationTests
{
    [Fact]
    public async Task Legacy_policy_and_assignment_rows_survive_upgrade_and_rerun()
    {
        await using var database = new IsolatedSqlDatabase();
        await database.StartAsync(Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION"), bootstrap: false);
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await Execute("""
            CREATE TABLE dbo.users(id bigint PRIMARY KEY);
            CREATE TABLE dbo.majors(id bigint PRIMARY KEY);
            CREATE TABLE dbo.project_periods(id bigint PRIMARY KEY);
            CREATE TABLE dbo.supervisor_requests(id bigint PRIMARY KEY, project_id bigint NOT NULL,
                supervisor_profile_id bigint NOT NULL, status nvarchar(20) NOT NULL);
            CREATE UNIQUE INDEX ux_supervisor_requests_pending ON dbo.supervisor_requests(project_id, supervisor_profile_id) WHERE status = N'PENDING';
            CREATE TABLE dbo.supervisor_assignments(id bigint PRIMARY KEY, project_id bigint NOT NULL,
                supervisor_profile_id bigint NOT NULL, supervisor_request_id bigint NOT NULL, is_primary bit NOT NULL, ended_at datetime2 NULL,
                CONSTRAINT uq_supervisor_assignments_project_supervisor UNIQUE(project_id, supervisor_profile_id));
            INSERT dbo.project_periods VALUES(1);
            INSERT dbo.supervisor_requests VALUES(1, 10, 20, N'ACCEPTED');
            INSERT dbo.supervisor_assignments VALUES(1, 10, 20, 1, 1, NULL), (2, 10, 21, 2, 0, '2026-01-01');
            """);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "db", "changes"))) directory = directory.Parent;
        Assert.NotNull(directory);
        for (var run = 0; run < 2; run++)
            foreach (var file in new[] { "20260925_add_project_period_governance_policy.sql", "20260925_add_supervisor_assignment_types.sql" })
                await Execute(await File.ReadAllTextAsync(Path.Combine(directory!.FullName, "db", "changes", file)));
        await using var check = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.project_periods WHERE id = 1 AND policy_version = 1
                AND allowed_project_modes = 'SINGLE_MAJOR,INTERDISCIPLINARY'
                AND allowed_proposal_sources = 'PUBLISHED_TOPIC,STUDENT_PROPOSAL';
            SELECT COUNT(*) FROM dbo.supervisor_assignments WHERE assignment_type = 'PRIMARY' AND major_id IS NULL
                AND assigned_by IS NULL AND ((id = 1 AND is_primary = 1 AND ended_at IS NULL) OR (id = 2 AND is_primary = 0 AND ended_at IS NOT NULL));
            """, connection);
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.Equal(1, reader.GetInt32(0));
        Assert.True(await reader.NextResultAsync()); Assert.True(await reader.ReadAsync()); Assert.Equal(2, reader.GetInt32(0));

        async Task Execute(string sql)
        {
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
