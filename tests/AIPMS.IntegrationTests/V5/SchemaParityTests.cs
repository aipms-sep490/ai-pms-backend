using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.V5;

public sealed class SchemaParityTests
{
    [Fact]
    public async Task Fresh_upgrade_and_rerun_have_identical_schema_and_preserve_snapshots()
    {
        var source = Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION");
        await using var fresh = new IsolatedSqlDatabase();
        await using var upgraded = new IsolatedSqlDatabase();
        await fresh.StartAsync(source, bootstrap: false);
        await upgraded.StartAsync(source, bootstrap: false);
        await SqlSchemaBootstrap.ApplyAsync(fresh.ConnectionString);

        var baseline = Path.Combine(SqlSchemaBootstrap.Root, "db", "e2e", "baselines", "schema-09abf95.sql.gz");
        await using var file = System.IO.File.OpenRead(baseline);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var text = new StreamReader(gzip);
        var schema = await text.ReadToEndAsync();
        await SqlSchemaBootstrap.ApplyAsync(upgraded.ConnectionString, schema);
        var expected = await Inventory(fresh.ConnectionString);
        Assert.Equal(expected, await Inventory(upgraded.ConnectionString));
        var inventoryPath = Environment.GetEnvironmentVariable("AIPMS_SCHEMA_INVENTORY_OUTPUT");
        if (!string.IsNullOrWhiteSpace(inventoryPath))
            await System.IO.File.WriteAllTextAsync(inventoryPath, System.Text.Json.JsonSerializer.Serialize(expected));
        await VerifyMappedColumns(fresh.ConnectionString);

        await SqlSchemaBootstrap.ExecuteAsync(upgraded.ConnectionString, """
            DECLARE @org bigint, @semester bigint, @user bigint, @team bigint, @project bigint;
            INSERT dbo.organizations(code,name) VALUES('V5','V5 schema fixture'); SET @org=SCOPE_IDENTITY();
            INSERT dbo.academic_semesters(organization_id,code,name,start_date,end_date,status)
                VALUES(@org,'V5','V5','2026-01-01','2026-12-31','ACTIVE'); SET @semester=SCOPE_IDENTITY();
            INSERT dbo.users(email,password_hash,full_name,status) VALUES('v5@schema.invalid','unused','V5','ACTIVE'); SET @user=SCOPE_IDENTITY();
            INSERT dbo.teams(academic_semester_id,code,name,status,created_by) VALUES(@semester,'V5','V5','FORMING',@user); SET @team=SCOPE_IDENTITY();
            INSERT dbo.projects(team_id,code,title,status,created_by) VALUES(@team,'V5','V5','DRAFT',@user); SET @project=SCOPE_IDENTITY();
            INSERT dbo.contribution_snapshots(project_id,user_id,snapshot_at,snapshot_hash,activity_score,evidence_count,snapshot_json)
                VALUES(@project,@user,'2026-10-11',REPLICATE('a',64),0,0,N'{"insufficientEvidence":true}');
            """);
        var before = await SnapshotHash(upgraded.ConnectionString);
        await SqlSchemaBootstrap.ReplayAsync(upgraded.ConnectionString);
        Assert.Equal(expected, await Inventory(upgraded.ConnectionString));
        Assert.Equal(before, await SnapshotHash(upgraded.ConnectionString));

        await using (var auditConnection = new SqlConnection(upgraded.ConnectionString))
        {
            await auditConnection.OpenAsync();
            await using var audit = new SqlCommand(await System.IO.File.ReadAllTextAsync(
                Path.Combine(SqlSchemaBootstrap.Root, "db", "e2e", "v5-integrity-readonly.sql")), auditConnection);
            await using var reader = await audit.ExecuteReaderAsync();
            var signals = new Dictionary<string, long>();
            while (await reader.ReadAsync()) signals.Add(reader.GetString(0), reader.GetInt64(1));
            Assert.Equal(0, signals["DUPLICATE_ACTIVE_PRIMARY"]);
            Assert.Equal(0, signals["PRIMARY_EVALUATOR_CONFLICT"]);
        }
        Assert.Equal(before, await SnapshotHash(upgraded.ConnectionString));

        // Reproduce the shared database's four confirmed structural gaps, without touching shared data.
        await SqlSchemaBootstrap.ExecuteAsync(upgraded.ConnectionString, """
            ALTER TABLE dbo.meetings DROP CONSTRAINT df_meetings_delivery_mode;
            ALTER TABLE dbo.meetings DROP CONSTRAINT df_meetings_video_channel;
            ALTER TABLE dbo.supervisor_requests DROP CONSTRAINT ck_supervisor_requests_assignment_type;
            ALTER TABLE dbo.supervisor_assignments DROP CONSTRAINT ck_supervisor_assignments_assignment_type;
            """);
        await SqlSchemaBootstrap.ReplayAsync(upgraded.ConnectionString);
        Assert.Equal(expected, await Inventory(upgraded.ConnectionString));
        Assert.Equal(before, await SnapshotHash(upgraded.ConnectionString));

        // The parity check must notice a missing index, even when all tables still exist.
        await SqlSchemaBootstrap.ExecuteAsync(upgraded.ConnectionString,
            "DROP INDEX ux_contribution_snapshots_hash ON dbo.contribution_snapshots;");
        Assert.NotEqual(expected, await Inventory(upgraded.ConnectionString));
    }

    [Fact]
    public async Task Bootstrap_refuses_shared_database_and_database_switching()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => SqlSchemaBootstrap.ExecuteAsync(
            "Server=localhost;Database=AI_PMS;Integrated Security=true", "SELECT 1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SqlSchemaBootstrap.ExecuteAsync(
            "Server=localhost;Database=AI_PMS_TEST_" + Guid.NewGuid().ToString("N"), "USE [AI_PMS];"));
    }

    private static async Task<string> SnapshotHash(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT * FROM dbo.contribution_snapshots ORDER BY id FOR JSON PATH", connection);
        var json = (string)(await command.ExecuteScalarAsync())!;
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    private static async Task VerifyMappedColumns(string connectionString)
    {
        await using var db = new AipmsDbContext(new DbContextOptionsBuilder<AipmsDbContext>()
            .UseSqlServer(connectionString).Options);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT CONCAT(s.name,'.',t.name,'.',c.name)
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
            JOIN sys.columns c ON c.object_id=t.object_id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        var missing = db.Model.GetRelationalModel().Tables.SelectMany(t => t.Columns
            .Select(c => $"{t.Schema ?? "dbo"}.{t.Name}.{c.Name}"))
            .Where(c => !columns.Contains(c)).Order(StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0, "Missing mapped columns: " + string.Join(", ", missing));
    }

    private static async Task<string[]> Inventory(string connectionString)
    {
        var sql = await System.IO.File.ReadAllTextAsync(Path.Combine(SqlSchemaBootstrap.Root, "db", "e2e", "schema-inventory.sql"));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0).Replace("\r\n", "\n").Trim());
        return rows.Order(StringComparer.Ordinal).ToArray();
    }
}
