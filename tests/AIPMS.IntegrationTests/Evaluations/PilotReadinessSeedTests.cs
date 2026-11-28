using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AIPMS.Application.Features.StudentRosters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class PilotReadinessSeedTests(EvaluationDraftDatabaseFixture database)
    : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AIPMS.sln"))
                && File.Exists(Path.Combine(dir.FullName, "db", "pilot", "01_seed_3track_pilot_config.sql")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate ai-pms-backend repository root.");
    }

    private static async Task<(int ExitCode, string Output)> RunPowerShellScriptAsync(string scriptPath, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell" : "pwsh",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\" {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode, stdout + "\n" + stderr);
    }

    [Fact]
    public async Task Pilot_3track_seed_is_idempotent_passes_verify_script_and_sample_xlsx_previews_cleanly()
    {
        var s = await database.Seed(lockedSubmission: false);
        var repoRoot = FindRepoRoot();
        var seedSqlPath = Path.Combine(repoRoot, "db", "pilot", "01_seed_3track_pilot_config.sql");
        var verifyPs1Path = Path.Combine(repoRoot, "scripts", "verify-pilot-readiness.ps1");
        var sampleXlsxPath = Path.Combine(repoRoot, "db", "pilot", "samples", "pilot_students_import_template.xlsx");
        Assert.True(File.Exists(sampleXlsxPath), $"Missing generated pilot Excel sample at {sampleXlsxPath}");

        var seedSql = await File.ReadAllTextAsync(seedSqlPath);

        // 1. Run 01_seed_3track_pilot_config.sql TWICE on the isolated DB to verify strict idempotency
        await using (var sql = new SqlConnection(database.ConnectionString))
        {
            await sql.OpenAsync();
            for (var run = 0; run < 2; run++)
            {
                await using var cmd = sql.CreateCommand();
                cmd.CommandTimeout = 120;
                cmd.CommandText = "EXEC sys.sp_set_session_context @key = N'pilot_actor_id', @value = @actor;\n" + seedSql;
                cmd.Parameters.AddWithValue("@actor", s.Scope.Users.Admin);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        long seMajorId, mktMajorId;
        await using (var db = database.CreateContext())
        {
            var org = await db.Organizations.AsNoTracking().SingleAsync(o => o.Code == "PILOT");
            var depts = await db.Departments.AsNoTracking().Where(d => d.OrganizationId == org.Id).ToListAsync();
            Assert.Equal(2, depts.Count);
            var itDept = depts.Single(d => d.Code == "IT");
            var mktDept = depts.Single(d => d.Code == "MKT");
            seMajorId = await db.Majors.AsNoTracking().Where(m => m.DepartmentId == itDept.Id && m.Code == "SE").Select(m => m.Id).SingleAsync();
            mktMajorId = await db.Majors.AsNoTracking().Where(m => m.DepartmentId == mktDept.Id && m.Code == "MKT").Select(m => m.Id).SingleAsync();

            var sem = await db.AcademicSemesters.AsNoTracking().SingleAsync(x => x.OrganizationId == org.Id && x.Code == "PILOT-FALL2026");
            var periods = await db.ProjectPeriods.AsNoTracking().Where(p => p.AcademicSemesterId == sem.Id).ToListAsync();
            Assert.Equal(6, periods.Count);
            Assert.All(periods, p => Assert.False(p.EnableDefenseRoomScheduling)); // POL-06 non-blocking default = 0

            var blueprints = await db.AcademicBlueprints.AsNoTracking().Where(b => b.AcademicSemesterId == sem.Id && b.Status == "PUBLISHED").ToListAsync();
            Assert.Equal(3, blueprints.Count);
            Assert.Equal(["IT_MKT", "IT_ONLY", "MKT_ONLY"], blueprints.Select(b => b.TrackCode).OrderBy(x => x).ToArray());
        }

        // 2. verify-pilot-readiness.ps1 Positive + Negative (missing semester fails non-zero)
        var (okCode, okOut) = await RunPowerShellScriptAsync(verifyPs1Path,
            $"-ConnectionString \"{database.ConnectionString}\" -SemesterCode \"PILOT-FALL2026\"");
        Assert.True(okCode == 0, $"verify-pilot-readiness.ps1 failed ({okCode}): {okOut}");
        Assert.Contains("PASS:", okOut);

        var (badCode, badOut) = await RunPowerShellScriptAsync(verifyPs1Path,
            $"-ConnectionString \"{database.ConnectionString}\" -SemesterCode \"NONEXISTENT-PILOT-SEM\"");
        Assert.NotEqual(0, badCode);
        Assert.Contains("PILOT_READINESS_FAILED", badOut);

        // 3. Validate pilot_students_import_template.xlsx through real StudentAccountImportController preview endpoint
        using var app = new EvaluationFactory(database);
        using var adminClient = app.CreateAuthenticatedClient(s.Scope.Users.Admin, roles: ["ADMIN"]);
        using var nonAdminClient = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);

        var xlsxBytes = await File.ReadAllBytesAsync(sampleXlsxPath);
        using (var form = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(xlsxBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            form.Add(fileContent, "File", "pilot_students_import_template.xlsx");
            form.Add(new StringContent(seMajorId.ToString()), "MajorId");

            var previewRes = await adminClient.PostAsync("/api/v1/users/student-import/preview", form);
            Assert.Equal(HttpStatusCode.OK, previewRes.StatusCode);
            var preview = await previewRes.Content.ReadFromJsonAsync<StudentAccountImportPreview>();
            Assert.NotNull(preview);
            Assert.Equal(4, preview!.Rows.Count);
            Assert.All(preview.Rows.Take(2), r => Assert.Empty(r.Errors));
            Assert.All(preview.Rows.Skip(2), r => Assert.Contains("SPECIALIZATION_NOT_FOUND", r.Errors));
        }

        using (var mktForm = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(xlsxBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            mktForm.Add(fileContent, "File", "pilot_students_import_template.xlsx");
            mktForm.Add(new StringContent(mktMajorId.ToString()), "MajorId");

            var mktPreviewRes = await adminClient.PostAsync("/api/v1/users/student-import/preview", mktForm);
            Assert.Equal(HttpStatusCode.OK, mktPreviewRes.StatusCode);
            var mktPreview = await mktPreviewRes.Content.ReadFromJsonAsync<StudentAccountImportPreview>();
            Assert.NotNull(mktPreview);
            Assert.All(mktPreview!.Rows.Skip(2), r => Assert.Empty(r.Errors));
        }

        // Negative RBAC: non-Admin cannot call student-import preview (403)
        using (var forbiddenForm = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(xlsxBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            forbiddenForm.Add(fileContent, "File", "pilot_students_import_template.xlsx");
            forbiddenForm.Add(new StringContent(seMajorId.ToString()), "MajorId");

            var forbiddenRes = await nonAdminClient.PostAsync("/api/v1/users/student-import/preview", forbiddenForm);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenRes.StatusCode);
        }
    }
}
