using System.Text.RegularExpressions;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.Data.SqlClient;

namespace AIPMS.IntegrationTests.Teams;

public sealed class EligibilitySnapshotMigrationTests
{
    private static async Task<string> ReadMigrationSqlAsync()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "db", "changes")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return await File.ReadAllTextAsync(Path.Combine(directory.FullName, "db", "changes", "20260928_add_team_eligibility_snapshots.sql"));
    }

    private static async Task ExecuteMigrationAsync(SqlConnection connection, string sql)
    {
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<bool> ObjectExistsAsync(SqlConnection connection, string query)
    {
        await using var command = new SqlCommand(query, connection);
        var result = await command.ExecuteScalarAsync();
        return result != null && result != DBNull.Value;
    }

    [Fact]
    public async Task Migration_Is_Rerunnable_And_Self_Healing_For_Dropped_Index_And_Constraint()
    {
        var database = new SupervisorDatabaseFixture();
        await database.InitializeAsync();
        try
        {
            var sql = await ReadMigrationSqlAsync();
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.OpenAsync();

            // 1. First execution on clean database
            await ExecuteMigrationAsync(connection, sql);

            // 2. Verify tables, index, and check constraint exist
            Assert.True(await ObjectExistsAsync(connection, "SELECT 1 FROM sys.tables WHERE name = 'team_eligibility_checks'"));
            Assert.True(await ObjectExistsAsync(connection, "SELECT 1 FROM sys.tables WHERE name = 'team_eligibility_issues'"));
            Assert.True(await ObjectExistsAsync(connection, "SELECT 1 FROM sys.indexes WHERE name = 'ux_team_eligibility_checks_team_evaluation_key' AND object_id = OBJECT_ID('dbo.team_eligibility_checks')"));
            Assert.True(await ObjectExistsAsync(connection, "SELECT 1 FROM sys.check_constraints WHERE name = 'ck_team_eligibility_checks_round_integrity' AND parent_object_id = OBJECT_ID('dbo.team_eligibility_checks')"));
            Assert.True(await ObjectExistsAsync(connection, "SELECT 1 FROM sys.indexes WHERE name = 'ux_team_eligibility_issues_check_sort' AND object_id = OBJECT_ID('dbo.team_eligibility_issues')"));

            // 3. Drop one proposal index and one proposal constraint
            await using (var dropCmd = connection.CreateCommand())
            {
                dropCmd.CommandText = @"
                    DROP INDEX ux_team_eligibility_checks_team_evaluation_key ON dbo.team_eligibility_checks;
                    ALTER TABLE dbo.team_eligibility_checks DROP CONSTRAINT ck_team_eligibility_checks_round_integrity;
                ";
                await dropCmd.ExecuteNonQueryAsync();
            }

            // Assert they are dropped
            Assert.False(await ObjectExistsAsync(connection, "SELECT 1 FROM sys.indexes WHERE name = 'ux_team_eligibility_checks_team_evaluation_key' AND object_id = OBJECT_ID('dbo.team_eligibility_checks')"));
            Assert.False(await ObjectExistsAsync(connection, "SELECT 1 FROM sys.check_constraints WHERE name = 'ck_team_eligibility_checks_round_integrity' AND parent_object_id = OBJECT_ID('dbo.team_eligibility_checks')"));

            // 4. Re-run migration: self-healing should restore them
            await ExecuteMigrationAsync(connection, sql);

            // 5. Assert both missing objects are restored
            Assert.True(await ObjectExistsAsync(connection, "SELECT 1 FROM sys.indexes WHERE name = 'ux_team_eligibility_checks_team_evaluation_key' AND object_id = OBJECT_ID('dbo.team_eligibility_checks')"));
            Assert.True(await ObjectExistsAsync(connection, "SELECT 1 FROM sys.check_constraints WHERE name = 'ck_team_eligibility_checks_round_integrity' AND parent_object_id = OBJECT_ID('dbo.team_eligibility_checks')"));

            // 6. Run a third time: assert idempotent success without errors
            await ExecuteMigrationAsync(connection, sql);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    [Fact]
    public async Task Migration_Throws_Clear_Error_When_Existing_Table_Has_Incompatible_Column()
    {
        var database = new SupervisorDatabaseFixture();
        await database.InitializeAsync();
        try
        {
            var sql = await ReadMigrationSqlAsync();
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.OpenAsync();

            // Pre-create table with incompatible column definition (result as INT instead of VARCHAR(10))
            await using (var createCmd = connection.CreateCommand())
            {
                createCmd.CommandText = @"
                    CREATE TABLE dbo.team_eligibility_checks (
                        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        team_id BIGINT NOT NULL,
                        project_period_id BIGINT NOT NULL,
                        result INT NOT NULL
                    );
                ";
                await createCmd.ExecuteNonQueryAsync();
            }

            // Execute migration and verify it fails-fast with the specific error
            var ex = await Assert.ThrowsAsync<SqlException>(() => ExecuteMigrationAsync(connection, sql));
            Assert.Equal(50001, ex.Number);
            Assert.Contains("incompatible column definitions", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }
}
