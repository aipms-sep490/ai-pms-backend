using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class ImmutableScoreTriggersTests(EvaluationDraftDatabaseFixture database)
    : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {raw}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Insert_succeeds_via_ef_UseSqlOutputClause_false_and_direct_update_or_delete_throws_sql_51009()
    {
        var s = await database.Seed(lockedSubmission: true, confirmedCommittee: true);
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);

        long majorId;
        await using (var setupDb = database.CreateContext())
        {
            majorId = await setupDb.ProjectMajors
                .Where(pm => pm.ProjectId == s.ProjectId)
                .Select(pm => pm.MajorId)
                .FirstAsync();
        }

        // 1. Publish scheme, score & finalize evaluation (inserts EvaluationFinalization via EF UseSqlOutputClause(false)),
        // then publish ProjectResult & StudentResult, and insert ResultRevision.
        var schemeDraft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(
                s.ProjectId, s.PeriodId, "Trigger Immutability Scheme", 5.0m,
                [
                    new("Common Ongoing", "COMMON", null, s.RubricId, 100m, 80m, 1, Stage: "ON_GOING", TargetScope: "PROJECT"),
                    new("Individual Component", "INDIVIDUAL", majorId, s.RubricId, 0m, 20m, 1, Stage: "ON_GOING", TargetScope: "STUDENT")
                ],
                Policy: new(false, "APPROVED", "QD-TRIG-2026", CalculationVersion: "LEGACY_V1"))));

        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync(
            $"/api/v1/evaluation-schemes/{schemeDraft.Id}/publish", new SchemeTokenRequest(schemeDraft.ConcurrencyToken)));

        var commonComp = scheme.Components.Single(c => c.Scope == "COMMON");
        var indivComp = scheme.Components.Single(c => c.Scope == "INDIVIDUAL");

        long evaluationId = 0;
        foreach (var comp in new[] { commonComp, indivComp })
        {
            var assign = await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(
                $"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
                new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "COMMITTEE", comp.Scope, comp.MajorId,
                    comp.Scope == "INDIVIDUAL" ? s.Scope.Users.Student : null, comp.Id)));
            var draft = await Body<EvaluationDraftDto>(await evaluator.PostAsync(
                $"/api/v1/evaluation-assignments/{assign.Id}/evaluation", null));
            draft = await Body<EvaluationDraftDto>(await evaluator.PutAsJsonAsync(
                $"/api/v1/evaluations/{draft.Id}/draft",
                new SaveEvaluationDraftRequest(draft.ConcurrencyToken, "Score for trigger check",
                    [new(s.Criteria[0], 8.0m, null), new(s.Criteria[1], 16.0m, null)])));
            var finalized = await Body<EvaluationDraftDto>(await evaluator.PostAsJsonAsync(
                $"/api/v1/evaluations/{draft.Id}/finalize", new { draft.ConcurrencyToken }));
            if (evaluationId == 0) evaluationId = finalized.Id;
        }

        var preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/result/preview"));
        Assert.True(preview.CanPublish);
        var batch = await Body<BatchPublishResultDto>(await staff.PostAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/results/publish-batch",
            new BatchPublishResultRequest(preview.ConfirmationToken)));
        var projectResultId = batch.ProjectResult.Id;
        var studentResultId = Assert.Single(batch.StudentResults).Id;

        long revisionId;
        await using (var revDb = database.CreateContext())
        {
            var bundle = new ResultCorrectionBundle
            {
                ProjectId = s.ProjectId,
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = EvaluationDraftDatabaseFixture.Now,
                Reason = "Trigger baseline bundle",
                Status = "PUBLISHED",
                BaseStamp = "v1",
                PublishedBy = s.Scope.Users.OutsideStaff,
                PublishedAt = EvaluationDraftDatabaseFixture.Now,
            };
            revDb.Add(bundle);
            await revDb.SaveChangesAsync();

            var revision = new ResultRevision
            {
                BundleId = bundle.Id,
                ProjectId = s.ProjectId,
                TargetKey = s.Scope.Users.Student,
                StudentId = s.Scope.Users.Student,
                Version = 2,
                SnapshotJson = "{}",
                PublishedBy = s.Scope.Users.Staff,
                PublishedAt = EvaluationDraftDatabaseFixture.Now,
            };
            revDb.Add(revision);
            await revDb.SaveChangesAsync();
            revisionId = revision.Id;
        }

        var s2 = await database.Seed(lockedSubmission: true);
        long s2EvalId;
        using (var s2Staff = app.CreateAuthenticatedClient(s2.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]))
        using (var s2Eval = app.CreateAuthenticatedClient(s2.Scope.Users.Lecturer))
        {
            var s2SchemeDraft = await Body<EvaluationSchemeDto>(await s2Staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
                new SaveEvaluationSchemeRequest(
                    s2.ProjectId, s2.PeriodId, "Trigger S2 Unlinked Finalization", 5.0m,
                    [new("Common", "COMMON", null, s2.RubricId, 100m, 100m, 1, Stage: "ON_GOING", TargetScope: "PROJECT")],
                    Policy: new(false, "APPROVED", "QD-TRIG-S2", CalculationVersion: "LEGACY_V1"))));
            var s2Scheme = await Body<EvaluationSchemeDto>(await s2Staff.PostAsJsonAsync(
                $"/api/v1/evaluation-schemes/{s2SchemeDraft.Id}/publish", new SchemeTokenRequest(s2SchemeDraft.ConcurrencyToken)));
            var s2Comp = Assert.Single(s2Scheme.Components);
            var s2Assign = await Body<EvaluationAssignmentDto>(await s2Staff.PostAsJsonAsync(
                $"/api/v1/projects/{s2.ProjectId}/evaluation-assignments",
                new AssignEvaluatorRequest(s2.Scope.Users.Lecturer, s2.PeriodId, "COMMITTEE", s2Comp.Scope, s2Comp.MajorId, null, s2Comp.Id)));
            var s2Draft = await Body<EvaluationDraftDto>(await s2Eval.PostAsync(
                $"/api/v1/evaluation-assignments/{s2Assign.Id}/evaluation", null));
            s2Draft = await Body<EvaluationDraftDto>(await s2Eval.PutAsJsonAsync(
                $"/api/v1/evaluations/{s2Draft.Id}/draft",
                new SaveEvaluationDraftRequest(s2Draft.ConcurrencyToken, "Unlinked finalization",
                    [new(s2.Criteria[0], 8.0m, null), new(s2.Criteria[1], 16.0m, null)])));
            s2EvalId = (await Body<EvaluationDraftDto>(await s2Eval.PostAsJsonAsync(
                $"/api/v1/evaluations/{s2Draft.Id}/finalize", new { s2Draft.ConcurrencyToken }))).Id;
        }

        long orphanPrId, orphanSrId;
        await using (var orphanDb = database.CreateContext())
        {
            var s2FinalSubId = await orphanDb.Set<AIPMS.Infrastructure.Persistence.Models.FinalSubmission>()
                .Where(fs => fs.ProjectId == s2.ProjectId)
                .Select(fs => fs.Id)
                .FirstAsync();
            var orphanPr = new ProjectResult
            {
                ProjectId = s2.ProjectId,
                FinalSubmissionId = s2FinalSubId,
                PublishedBy = s2.Scope.Users.Staff,
                PublishedAt = EvaluationDraftDatabaseFixture.Now,
                SnapshotJson = "{}",
            };
            orphanDb.Add(orphanPr);

            var existingSr = await orphanDb.Set<StudentResult>().AsNoTracking().FirstAsync(x => x.Id == studentResultId);
            var orphanSr = new StudentResult
            {
                ProjectId = existingSr.ProjectId,
                StudentId = s.Scope.Users.OtherLecturer,
                MajorId = existingSr.MajorId,
                SchemeId = existingSr.SchemeId,
                TotalScore = 7.50m,
                PassThreshold = 5.00m,
                Outcome = "PASSED",
                OutcomeDetail = "PASSED",
                CalculationRule = existingSr.CalculationRule,
                PublishedBy = s.Scope.Users.Staff,
                PublishedAt = EvaluationDraftDatabaseFixture.Now,
                SnapshotJson = "{}",
            };
            orphanDb.Add(orphanSr);
            await orphanDb.SaveChangesAsync();
            orphanPrId = orphanPr.Id;
            orphanSrId = orphanSr.Id;
        }

        // 2. Direct SQL UPDATE and DELETE must fail with SQL Error 51009 on all 4 immutable score tables, preserving rows.
        await using var sql = new SqlConnection(database.ConnectionString);
        await sql.OpenAsync();

        var activeTriggerCount = Convert.ToInt32(await ExecuteScalarAsync(sql, """
            SELECT COUNT(*) FROM sys.triggers
            WHERE name IN (
                N'tr_immutable_evaluation_finalizations',
                N'tr_immutable_project_results',
                N'tr_immutable_student_results',
                N'tr_immutable_result_revisions'
            ) AND is_disabled = 0
            """));
        Assert.Equal(4, activeTriggerCount);

        var statements = new (string Table, string UpdateSql, string DeleteSql, string CountSql)[]
        {
            (
                "dbo.evaluation_finalizations",
                $"UPDATE dbo.evaluation_finalizations SET snapshot_json = N'{{\"tampered\":true}}' WHERE evaluation_id = {s2EvalId}",
                $"DELETE FROM dbo.evaluation_finalizations WHERE evaluation_id = {s2EvalId}",
                $"SELECT COUNT(*) FROM dbo.evaluation_finalizations WHERE evaluation_id = {s2EvalId}"
            ),
            (
                "dbo.project_results",
                $"UPDATE dbo.project_results SET snapshot_json = N'{{\"tampered\":true}}' WHERE id = {orphanPrId}",
                $"DELETE FROM dbo.project_results WHERE id = {orphanPrId}",
                $"SELECT COUNT(*) FROM dbo.project_results WHERE id = {orphanPrId}"
            ),
            (
                "dbo.student_results",
                $"UPDATE dbo.student_results SET total_score = 10.00 WHERE id = {orphanSrId}",
                $"DELETE FROM dbo.student_results WHERE id = {orphanSrId}",
                $"SELECT COUNT(*) FROM dbo.student_results WHERE id = {orphanSrId}"
            ),
            (
                "dbo.result_revisions",
                $"UPDATE dbo.result_revisions SET snapshot_json = N'{{\"tampered\":true}}' WHERE id = {revisionId}",
                $"DELETE FROM dbo.result_revisions WHERE id = {revisionId}",
                $"SELECT COUNT(*) FROM dbo.result_revisions WHERE id = {revisionId}"
            ),
        };

        foreach (var (table, updateSql, deleteSql, countSql) in statements)
        {
            var updEx = await Assert.ThrowsAsync<SqlException>(() => ExecuteNonQueryAsync(sql, updateSql));
            Assert.True(updEx.Number == 51009, $"UPDATE {table} threw {updEx.Number}: {updEx.Message}");

            var delEx = await Assert.ThrowsAsync<SqlException>(() => ExecuteNonQueryAsync(sql, deleteSql));
            Assert.True(delEx.Number == 51009, $"DELETE {table} threw {delEx.Number}: {delEx.Message}");

            Assert.Equal(1, Convert.ToInt32(await ExecuteScalarAsync(sql, countSql)));
        }
    }

    private static async Task<object?> ExecuteScalarAsync(SqlConnection conn, string sqlText)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sqlText;
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task ExecuteNonQueryAsync(SqlConnection conn, string sqlText)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sqlText;
        await cmd.ExecuteNonQueryAsync();
    }
}
