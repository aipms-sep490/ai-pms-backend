using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace AIPMS.IntegrationTests;

// Both legacy repository tests and BE-02 tests use an owned, unique database.
// Neither the connection string's catalog nor a shared database name is a cleanup target.
internal sealed class IsolatedSqlDatabase : IAsyncDisposable
{
    private readonly string name = "AI_PMS_TEST_" + Guid.NewGuid().ToString("N");
    private MsSqlContainer? container;
    private string masterConnection = "";
    private bool created;
    public string ConnectionString { get; private set; } = "";

    public async Task StartAsync(string? source, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                container = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
                await container.StartAsync(ct);
                source = container.GetConnectionString();
            }
            var builder = new SqlConnectionStringBuilder(source) { InitialCatalog = "master", Pooling = false };
            masterConnection = builder.ConnectionString;
            ValidateOwnedName();
            await using (var connection = new SqlConnection(masterConnection))
            {
                for (var attempt = 0; ; attempt++)
                {
                    try { await connection.OpenAsync(ct); break; }
                    catch (SqlException) when (attempt < 9) { await Task.Delay(1000, ct); }
                }
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE [{name}]";
                await command.ExecuteNonQueryAsync(ct);
                created = true;
            }
            builder.InitialCatalog = name;
            ConnectionString = builder.ConnectionString;
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "db", "schema.sql")))
                directory = directory.Parent;
            if (directory is null) throw new InvalidOperationException("Cannot find schema.sql.");
            var schema = await File.ReadAllTextAsync(Path.Combine(directory.FullName, "db", "schema.sql"), ct);
            var start = schema.IndexOf("SET ANSI_NULLS ON;", StringComparison.Ordinal);
            if (start < 0) throw new InvalidOperationException("Unexpected schema bootstrap.");
            schema = schema[start..];
            if (Regex.IsMatch(schema, @"\bUSE\s|\b(?:CREATE|DROP|ALTER)\s+DATABASE\b", RegexOptions.IgnoreCase))
                throw new InvalidOperationException("Schema must not switch or manage databases.");

            await using var target = new SqlConnection(ConnectionString);
            await target.OpenAsync(ct);
            foreach (var batch in Regex.Split(schema, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var command = new SqlCommand(batch, target);
                await command.ExecuteNonQueryAsync(ct);
            }
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    private void ValidateOwnedName()
    {
        if (!Regex.IsMatch(name, "^AI_PMS_TEST_[a-f0-9]{32}$"))
            throw new InvalidOperationException("Refusing to manage an unowned database.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!created) return;
            ValidateOwnedName();
            await using var connection = new SqlConnection(masterConnection);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{name}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{name}];
                END
                """;
            await command.ExecuteNonQueryAsync();
            created = false;
        }
        finally
        {
            if (container is not null)
            {
                await container.DisposeAsync();
                container = null;
            }
        }
    }
}
