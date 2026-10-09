using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace AIPMS.IntegrationTests.StudentRosters;

public sealed class CurriculumMigrationTests
{
    [Fact]
    public async Task Upgrade_and_rerun_preserve_legacy_rows_and_imported_values()
    {
        await using var database = new IsolatedSqlDatabase();
        await database.StartAsync(Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION"), bootstrap: false);
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        async Task Execute(string sql)
        {
            foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var cmd = new SqlCommand(batch, connection);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        await Execute("CREATE TABLE dbo.users(id bigint PRIMARY KEY, full_name nvarchar(100)); INSERT dbo.users VALUES(1,N'Legacy');");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "db", "changes"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var sql = await File.ReadAllTextAsync(Path.Combine(dir.FullName, "db", "changes", "20261009_add_student_curriculum_code.sql"));
        await Execute(sql);
        await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.users WHERE curriculum_code IS NULL AND full_name=N'Legacy'", connection))
            Assert.Equal(1, (int)(await cmd.ExecuteScalarAsync())!);
        await Execute("UPDATE dbo.users SET curriculum_code=N'BIT_SE_18D_Java' WHERE id=1");
        await Execute(sql);
        await using (var cmd = new SqlCommand("SELECT curriculum_code FROM dbo.users WHERE id=1", connection))
            Assert.Equal("BIT_SE_18D_Java", await cmd.ExecuteScalarAsync());
        await using (var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.users') AND name='curriculum_code' AND max_length=200 AND is_nullable=1", connection))
            Assert.Equal(1, (int)(await cmd.ExecuteScalarAsync())!);
    }
}
