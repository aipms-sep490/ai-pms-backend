using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using DotNet.Testcontainers.Builders;
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

    public async Task StartAsync(string? source, CancellationToken ct = default, bool bootstrap = true)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                // The current SQL Server image ships sqlcmd18 under /opt/mssql-tools18;
                // wait on the database port and let the connection retry below verify readiness.
                container = new MsSqlBuilder()
                    .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1433))
                    .Build();
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
            // Each database has a separate pool; reuse sockets during the large API suite.
            builder.Pooling = true;
            builder.MaxPoolSize = 30;
            ConnectionString = builder.ConnectionString;
            if (!bootstrap) return;
            await SqlSchemaBootstrap.ApplyAsync(ConnectionString, ct: ct);
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
            if (!string.IsNullOrEmpty(ConnectionString))
            {
                using var ownedPool = new SqlConnection(ConnectionString);
                SqlConnection.ClearPool(ownedPool);
            }
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
