using System.Text.RegularExpressions;
using AIPMS.IntegrationTests.Supervisors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class RubricHierarchyMigrationTests
{
    [Fact]
    public async Task Upgrade_from_flat_schema_and_replay_preserve_legacy_scores_and_keys()
    {
        var database = new SupervisorDatabaseFixture();
        await database.InitializeAsync();
        try
        {
            var s = await database.SeedAsync();
            long evaluationId, criterionId, definitionId, detailId;
            await using (var db = database.CreateContext())
            {
                var org = await db.Departments.Where(d => d.Id == s.DepartmentId).Select(d => d.OrganizationId).SingleAsync();
                var criterion = new M.RubricCriterion { WeightPercent = 100, MaxScore = 10, IsRequired = true,
                    Criterion = new() { Code = Guid.NewGuid().ToString("N"), Name = "Legacy", IsActive = true } };
                var evaluation = new M.Evaluation { EvaluatorId = s.Lecturer, Status = "FINALIZED", EvaluationType = "FINAL", TotalScore = 8,
                    Rubric = new() { Code = Guid.NewGuid().ToString("N"), Name = "Legacy rubric", DepartmentId = s.DepartmentId,
                        CreatedBy = s.Staff, IsActive = true, RubricCriteria = [criterion] },
                    Project = new() { Code = Guid.NewGuid().ToString("N"), Title = "Project", Status = "ACTIVE", CreatedBy = s.Student,
                        Team = new() { Code = Guid.NewGuid().ToString("N"), Name = "Team", Status = "LOCKED", CreatedBy = s.Student,
                            AcademicSemester = new() { Code = Guid.NewGuid().ToString("N"), Name = "Semester", OrganizationId = org,
                                Status = "ACTIVE", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) } } },
                    EvaluationDetails = [new() { RubricCriterion = criterion, Score = 8, Comments = "Original" }] };
                db.Evaluations.Add(evaluation);
                await db.SaveChangesAsync();
                evaluationId = evaluation.Id; criterionId = criterion.Id; definitionId = criterion.CriterionId;
                detailId = evaluation.EvaluationDetails.Single().Id;
                // Reproduce the pre-migration shape only inside this test's owned database.
                await db.Database.ExecuteSqlRawAsync("""
                    ALTER TABLE dbo.rubric_criteria DROP CONSTRAINT fk_rubric_criteria_parent;
                    ALTER TABLE dbo.rubric_criteria DROP CONSTRAINT ck_rubric_criteria_parent;
                    ALTER TABLE dbo.rubric_criteria DROP CONSTRAINT uq_rubric_criteria_id_rubric;
                    DROP INDEX ix_rubric_criteria_parent_id ON dbo.rubric_criteria;
                    ALTER TABLE dbo.rubric_criteria DROP COLUMN parent_id;
                    ALTER TABLE dbo.rubric_criteria ALTER COLUMN max_score DECIMAL(8,2) NOT NULL;
                    """);
            }
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "db", "changes"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var sql = await File.ReadAllTextAsync(Path.Combine(directory.FullName, "db", "changes", "20260922_add_rubric_hierarchy.sql"));
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            for (var replay = 0; replay < 2; replay++)
                foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(batch)) continue;
                    await using var command = new SqlCommand(batch, connection);
                    await command.ExecuteNonQueryAsync();
                }
            await using var verify = database.CreateContext();
            var after = await verify.Evaluations.Include(e => e.EvaluationDetails).ThenInclude(d => d.RubricCriterion)
                .SingleAsync(e => e.Id == evaluationId);
            Assert.Equal(8m, after.TotalScore);
            Assert.Equal("FINALIZED", after.Status);
            var detail = Assert.Single(after.EvaluationDetails);
            Assert.Equal(detailId, detail.Id);
            Assert.Equal(8m, detail.Score);
            Assert.Equal("Original", detail.Comments);
            Assert.Equal(criterionId, detail.RubricCriterionId);
            Assert.Equal(definitionId, detail.RubricCriterion.CriterionId);
            Assert.Null(detail.RubricCriterion.ParentId);
            Assert.Equal(100m, detail.RubricCriterion.WeightPercent);
            Assert.Equal(10m, detail.RubricCriterion.MaxScore);
        }
        finally { await database.DisposeAsync(); }
    }
}
