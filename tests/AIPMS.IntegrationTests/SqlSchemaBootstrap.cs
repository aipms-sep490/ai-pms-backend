using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace AIPMS.IntegrationTests;

internal static class SqlSchemaBootstrap
{
    internal static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "db", "schema.sql")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Cannot find schema.sql.");
        }
    }

    internal static async Task ApplyAsync(string connectionString, string? schema = null, CancellationToken ct = default)
    {
        schema ??= await File.ReadAllTextAsync(Path.Combine(Root, "db", "schema.sql"), ct);
        var start = schema.IndexOf("SET ANSI_NULLS ON;", StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Unexpected schema bootstrap.");
        await ExecuteAsync(connectionString, schema[start..], ct);
        await ReplayAsync(connectionString, ct);
    }

    internal static async Task ReplayAsync(string connectionString, CancellationToken ct = default)
    {
        var manifest = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(
            Path.Combine(Root, "db", "e2e", "migrations.json"), ct))!;
        if (manifest.Length != manifest.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidOperationException("Duplicate migration in manifest.");
        foreach (var name in manifest)
        {
            if (!Regex.IsMatch(name, @"^\d{8}_[a-z0-9_]+\.sql$"))
                throw new InvalidOperationException("Invalid migration path.");
            var sql = await File.ReadAllTextAsync(Path.Combine(Root, "db", "changes", name), ct);
            // Only these historical scripts contain a known database selector.
            if (name is "20260825_add_account_security.sql" or "20260825_add_project_metadata_tags.sql")
                sql = Regex.Replace(sql, @"^USE \[AI_PMS\];[^\r\n]*\r?$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            await ExecuteAsync(connectionString, sql, ct);
        }
    }

    internal static async Task ExecuteAsync(string connectionString, string sql, CancellationToken ct = default)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { MultipleActiveResultSets = false };
        if (!Regex.IsMatch(builder.InitialCatalog, "^AI_PMS_TEST_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Bootstrap requires an owned isolated test database.");
        if (Regex.IsMatch(sql, @"^\s*(USE\s|(?:CREATE|ALTER|DROP)\s+DATABASE\b)", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            throw new InvalidOperationException("Schema must not switch or manage databases.");
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        await using (var options = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET XACT_ABORT ON;", connection))
            await options.ExecuteNonQueryAsync(ct);
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch)) continue;
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
