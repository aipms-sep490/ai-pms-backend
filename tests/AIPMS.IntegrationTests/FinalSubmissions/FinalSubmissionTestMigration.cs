using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace AIPMS.IntegrationTests.FinalSubmissions;

internal static class FinalSubmissionTestMigration
{
    public static async Task Apply(string connectionString)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "db", "changes"))) directory = directory.Parent;
        Assert.NotNull(directory);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var name in new[] { "20260911_add_rubric_versions.sql", "20260911_add_evaluation_assignments_and_drafts.sql",
            "20260912_add_final_submission_drafts.sql", "20260912_add_locked_final_submissions.sql" })
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(directory.FullName, "db", "changes", name));
            foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var command = new SqlCommand(batch, connection);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
