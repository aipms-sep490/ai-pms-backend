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
        var upgradedInventory = await Inventory(upgraded.ConnectionString);
        Assert.True(expected.SequenceEqual(upgradedInventory),
            "Fresh-only schema entries: " + string.Join("; ", expected.Except(upgradedInventory))
            + " | Upgrade-only schema entries: " + string.Join("; ", upgradedInventory.Except(expected)));
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

    [Fact]
    public void DbChangesDirectory_MatchesMigrationsManifest_AndContainsNoDemoPasswords()
    {
        var changesDir = Path.Combine(SqlSchemaBootstrap.Root, "db", "changes");
        var manifestPath = Path.Combine(SqlSchemaBootstrap.Root, "db", "e2e", "migrations.json");
        var diskFiles = Directory.GetFiles(changesDir, "*.sql")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var manifestFiles = System.Text.Json.JsonSerializer.Deserialize<string[]>(System.IO.File.ReadAllText(manifestPath))!
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(manifestFiles, diskFiles);
        foreach (var file in Directory.GetFiles(changesDir, "*.sql"))
        {
            var content = System.IO.File.ReadAllText(file);
            Assert.DoesNotContain("Aipms@123", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task FreshSchemaSqlAlone_MatchesSchemaPlusAllMigrations_AndIndustryTablesDeprecated()
    {
        var source = Environment.GetEnvironmentVariable("AIPMS_TEST_SQL_CONNECTION");
        await using var schemaOnly = new IsolatedSqlDatabase();
        await using var schemaPlusMigrations = new IsolatedSqlDatabase();
        await schemaOnly.StartAsync(source, bootstrap: false);
        await schemaPlusMigrations.StartAsync(source, bootstrap: false);

        // Apply ONLY db/schema.sql to schemaOnly (skipping CREATE/USE [AI_PMS] header, same as SqlSchemaBootstrap.ApplyAsync)
        var rawSchemaSql = await System.IO.File.ReadAllTextAsync(Path.Combine(SqlSchemaBootstrap.Root, "db", "schema.sql"));
        var startIdx = rawSchemaSql.IndexOf("SET ANSI_NULLS ON;", StringComparison.Ordinal);
        Assert.True(startIdx >= 0, "Expected SET ANSI_NULLS ON; header in db/schema.sql.");
        await SqlSchemaBootstrap.ExecuteAsync(schemaOnly.ConnectionString, rawSchemaSql[startIdx..]);

        // Apply db/schema.sql + all migrations.json (twice to verify idempotency) to schemaPlusMigrations
        await SqlSchemaBootstrap.ApplyAsync(schemaPlusMigrations.ConnectionString);
        await SqlSchemaBootstrap.ReplayAsync(schemaPlusMigrations.ConnectionString);

        var onlyInv = await Inventory(schemaOnly.ConnectionString);
        var fullInv = await Inventory(schemaPlusMigrations.ConnectionString);

        // Verify all 6 previously missing tables exist in fresh schema.sql alone
        await using (var conn = new SqlConnection(schemaOnly.ConnectionString))
        {
            await conn.OpenAsync();
            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = new SqlCommand("SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo')", conn))
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync()) tables.Add(r.GetString(0));

            Assert.True(tables.Count >= 207, $"Expected at least 207 tables in fresh schema.sql, found {tables.Count}.");
            foreach (var required in new[] { "rooms", "defense_tech_checks", "defense_schedule_change_requests", "gate_session_scripts", "project_period_calendars", "department_specializations" })
                Assert.Contains(required, tables);
        }

        // Verify mapped EF columns match fresh schema.sql alone
        await VerifyMappedColumns(schemaOnly.ConnectionString);

        // Verify fresh schema.sql alone produces the exact same semantic set of tables/columns/indexes/constraints as schema + all migrations
        // (Distinct handles historical unnamed inline FKs on team_eligibility_* that 20260928_add_team_eligibility_snapshots.sql re-adds with explicit names.)
        var onlyDistinct = onlyInv.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var fullDistinct = fullInv.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.True(onlyDistinct.SequenceEqual(fullDistinct),
            "Schema-only entries: " + string.Join("; ", onlyDistinct.Except(fullDistinct))
            + " | Migration-only entries: " + string.Join("; ", fullDistinct.Except(onlyDistinct)));

        // Negative parity guard (TK-W0-12): dropping a migrated column or index causes parity check to fail
        await SqlSchemaBootstrap.ExecuteAsync(schemaOnly.ConnectionString,
            "DROP INDEX ix_defense_session_room_start ON dbo.defense_sessions;");
        var mutatedOnly = (await Inventory(schemaOnly.ConnectionString)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Assert.False(mutatedOnly.SequenceEqual(fullDistinct), "Expected schema parity check to detect missing index drift.");

        // Verify all 6 industry_* tables carry MS_Description starting with DEPRECATED:
        await using (var conn = new SqlConnection(schemaPlusMigrations.ConnectionString))
        {
            await conn.OpenAsync();
            var deprecated = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using var cmd = new SqlCommand("""
                SELECT t.name, CAST(ep.value AS nvarchar(500))
                FROM sys.tables t
                JOIN sys.extended_properties ep ON ep.major_id = t.object_id AND ep.minor_id = 0 AND ep.name = N'MS_Description'
                WHERE t.schema_id = SCHEMA_ID('dbo') AND t.name LIKE 'industry_%'
                """, conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) deprecated[r.GetString(0)] = r.GetString(1);

            foreach (var tbl in new[] { "industry_experts", "industry_invitations", "industry_challenges", "industry_assignments", "industry_evidence_shares", "industry_feedback" })
            {
                Assert.True(deprecated.TryGetValue(tbl, out var desc), $"Missing MS_Description on dbo.{tbl}");
                Assert.StartsWith("DEPRECATED:", desc, StringComparison.Ordinal);
            }
        }
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
