using Microsoft.Data.SqlClient;

namespace AIPMS.IntegrationTests.StudentRosters;

public sealed class GoogleEnrollmentMigrationTests
{
    [Fact]
    public async Task Legacy_users_are_not_opted_in_and_rerun_preserves_enrollment()
    {
        await using var database = new IsolatedSqlDatabase();
        await database.StartAsync(Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION"), bootstrap: false);
        await using var connection = new SqlConnection(database.ConnectionString); await connection.OpenAsync();
        async Task Execute(string sql) { await using var command = new SqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        await Execute("CREATE TABLE dbo.users(id bigint PRIMARY KEY); INSERT dbo.users VALUES (1)");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "db", "changes"))) dir = dir.Parent;
        var sql = await File.ReadAllTextAsync(Path.Combine(dir!.FullName, "db", "changes", "20261009_add_google_enrollment_pending.sql"));
        await Execute(sql);
        await using (var command = new SqlCommand("SELECT google_enrollment_pending FROM dbo.users WHERE id=1", connection)) Assert.Equal(false, await command.ExecuteScalarAsync());
        await Execute("UPDATE dbo.users SET google_enrollment_pending=1 WHERE id=1");
        await Execute(sql);
        await using (var command = new SqlCommand("SELECT google_enrollment_pending FROM dbo.users WHERE id=1", connection)) Assert.Equal(true, await command.ExecuteScalarAsync());
    }
}
