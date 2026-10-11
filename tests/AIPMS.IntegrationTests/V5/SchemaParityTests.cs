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
        const string sql = """
            SELECT CONCAT('COLUMN|' COLLATE DATABASE_DEFAULT,s.name,'.',t.name,'|',c.name,'|',TYPE_NAME(c.user_type_id),'|',c.max_length,'|',c.precision,'|',c.scale,
                '|',c.is_nullable,'|',c.is_identity,'|',c.is_computed,'|',COALESCE(d.definition,''),'|',COALESCE(cc.definition,''))
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id
            LEFT JOIN sys.default_constraints d ON d.object_id=c.default_object_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id WHERE t.is_ms_shipped=0
            UNION ALL
            SELECT CONCAT('CHECK|' COLLATE DATABASE_DEFAULT,OBJECT_SCHEMA_NAME(parent_object_id),'.',OBJECT_NAME(parent_object_id),'|',definition,'|',is_disabled,'|',is_not_trusted)
            FROM sys.check_constraints
            UNION ALL
            SELECT CONCAT('FK|' COLLATE DATABASE_DEFAULT,OBJECT_SCHEMA_NAME(f.parent_object_id),'.',OBJECT_NAME(f.parent_object_id),'|',pc.name,'|',
                OBJECT_SCHEMA_NAME(f.referenced_object_id),'.',OBJECT_NAME(f.referenced_object_id),'|',rc.name,'|',fc.constraint_column_id,'|',
                f.delete_referential_action,'|',f.update_referential_action,'|',f.is_disabled,'|',f.is_not_trusted)
            FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
            JOIN sys.columns pc ON pc.object_id=f.parent_object_id AND pc.column_id=fc.parent_column_id
            JOIN sys.columns rc ON rc.object_id=f.referenced_object_id AND rc.column_id=fc.referenced_column_id
            UNION ALL
            SELECT CONCAT('INDEX|' COLLATE DATABASE_DEFAULT,OBJECT_SCHEMA_NAME(i.object_id),'.',OBJECT_NAME(i.object_id),'|',i.type,'|',i.is_unique,'|',i.is_primary_key,'|',
                i.is_unique_constraint,'|',i.is_disabled,'|',COALESCE(i.filter_definition,''),'|',ic.index_column_id,'|',ic.key_ordinal,'|',
                ic.is_descending_key,'|',ic.is_included_column,'|',c.name)
            FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id
            JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE t.is_ms_shipped=0 AND i.index_id>0
            UNION ALL
            SELECT CONCAT('MODULE|' COLLATE DATABASE_DEFAULT,s.name,'.',o.name,'|',o.type,'|',m.uses_ansi_nulls,'|',m.uses_quoted_identifier,'|',m.definition)
            FROM sys.sql_modules m JOIN sys.objects o ON o.object_id=m.object_id JOIN sys.schemas s ON s.schema_id=o.schema_id WHERE o.is_ms_shipped=0
            """;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(Regex.Replace(reader.GetString(0), @"\s+", " ").Trim());
        return rows.Order(StringComparer.Ordinal).ToArray();
    }
}
