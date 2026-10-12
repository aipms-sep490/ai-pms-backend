using System.Text.RegularExpressions;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Generated;

public partial class AipmsDbContext
{
    private static void ConfigureAiRuns(ModelBuilder model)
    {
        model.Entity<AiRun>().ToTable("ai_runs");
        foreach (var property in model.Entity<AiRun>().Metadata.GetProperties())
            property.SetColumnName(Regex.Replace(property.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant());
    }
}
