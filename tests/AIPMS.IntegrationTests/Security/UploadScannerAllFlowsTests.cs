using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Application.Features.Files;
using AIPMS.Infrastructure.Services.Files;
using AIPMS.IntegrationTests.Evaluations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Security;

public sealed class UploadScannerAllFlowsTests(EvaluationDraftDatabaseFixture database)
    : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static readonly byte[] ValidPdf = "%PDF-1.7\n1 0 obj <<>> endobj\n%%EOF"u8.ToArray();
    private static readonly byte[] DisguisedExePdf = "MZ\x90\x00\x03\x00\x00\x00FAKE_EXE_IN_PDF"u8.ToArray();

    private sealed class RecordingStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        public Task WriteAsync(string storageKey, Stream content, CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            Objects[storageKey] = ms.ToArray();
            return Task.CompletedTask;
        }
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Objects.TryGetValue(storageKey, out var bytes)
                ? Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
                : throw new FileNotFoundException();
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            Objects.Remove(storageKey);
            return Task.CompletedTask;
        }
    }

    private sealed class ScannerTestFactory(EvaluationDraftDatabaseFixture db, RecordingStorage storage) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = db.ConnectionString,
                    ["Chat:Enabled"] = "true",
                    ["Chat:RealtimeEnabled"] = "false",
                    ["Appeals:Enabled"] = "true"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileStorage>();
                services.AddSingleton<IFileStorage>(storage);
            });
        }
    }

    private static byte[] BuildDocx(bool includeMacro)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", "<Types/>");
            WriteEntry(zip, "word/document.xml", "<document/>");
            if (includeMacro) WriteEntry(zip, "word/vbaProject.bin", "MALICIOUS_MACRO");
        }
        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, string body)
    {
        using var s = zip.CreateEntry(name).Open();
        using var w = new StreamWriter(s);
        w.Write(body);
    }

    private static MultipartFormDataContent MakeFileForm(string fieldName, string fileName, string mimeType, byte[] bytes, IDictionary<string, string>? extras = null)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
        form.Add(fileContent, fieldName, fileName);
        if (extras is not null)
            foreach (var (k, v) in extras)
                form.Add(new StringContent(v), k);
        return form;
    }

    private static async Task<string?> ReadProblemCode(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) return c.GetString();
        if (doc.RootElement.TryGetProperty("errorCode", out var ec) && ec.ValueKind == JsonValueKind.String) return ec.GetString();
        return null;
    }

    [Fact]
    public async Task All_Upload_Flows_Quarantine_Malicious_Payloads_Enforce_Pol06_And_Allow_Clean_Files()
    {
        var s = await database.Seed(false);
        var storage = new RecordingStorage();
        long deliverableId, taskId, itMajorId;

        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            itMajorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();

            // Ensure active EXECUTION period covering both test fixture clock and UTC now
            var execPeriod = new M.ProjectPeriod
            {
                AcademicSemesterId = s.Scope.SemesterId,
                Code = "EXEC_" + Guid.NewGuid().ToString("N")[..8],
                Name = "Execution Window",
                PeriodType = "EXECUTION",
                Status = "ACTIVE",
                StartAt = DateTime.UtcNow.AddYears(-1),
                EndAt = DateTime.UtcNow.AddYears(1)
            };
            db.ProjectPeriods.Add(execPeriod);

            var deliverable = new M.Deliverable
            {
                ProjectId = s.ProjectId,
                Title = "Security Scan Deliverable",
                Status = "OPEN",
                CreatedBy = s.Scope.Users.Student
            };
            db.Deliverables.Add(deliverable);

            var ms = new M.Milestone
            {
                ProjectId = s.ProjectId,
                Title = "MS Scan",
                Status = "IN_PROGRESS",
                SortOrder = 1,
                CreatedBy = s.Scope.Users.Student
            };
            db.Milestones.Add(ms);
            await db.SaveChangesAsync();

            var task = new M.Task
            {
                MilestoneId = ms.Id,
                Title = "Scan Task",
                Status = "IN_PROGRESS",
                Priority = "MEDIUM",
                CreatedBy = s.Scope.Users.Student,
                ConcurrencyToken = Guid.NewGuid()
            };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();

            deliverableId = deliverable.Id;
            taskId = task.Id;
        }

        using var app = new ScannerTestFactory(database, storage);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: [AppRoles.Student]);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: [AppRoles.DepartmentStaff]);

        // 1. Deliverable version: MZ disguised PDF -> 422 FILE_QUARANTINED (0 objects written), .rar -> 400/422, clean PDF -> 201/200
        var badDeliv = await student.PostAsync($"/api/v1/deliverables/{deliverableId}/versions",
            MakeFileForm("file", "payload.pdf", "application/pdf", DisguisedExePdf,
                new Dictionary<string, string> { ["expectedLatestVersion"] = "0", ["note"] = "bad" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badDeliv.StatusCode);
        Assert.Equal(WorkflowErrorCodes.FileQuarantined, await ReadProblemCode(badDeliv));
        Assert.Empty(storage.Objects);

        var rarDeliv = await student.PostAsync($"/api/v1/deliverables/{deliverableId}/versions",
            MakeFileForm("file", "archive.rar", "application/x-rar-compressed", "Rar!"u8.ToArray(),
                new Dictionary<string, string> { ["expectedLatestVersion"] = "0" }));
        Assert.True(rarDeliv.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
        Assert.Empty(storage.Objects);

        var cleanDeliv = await student.PostAsync($"/api/v1/deliverables/{deliverableId}/versions",
            MakeFileForm("file", "clean.pdf", "application/pdf", ValidPdf,
                new Dictionary<string, string> { ["expectedLatestVersion"] = "0", ["note"] = "clean" }));
        Assert.True(cleanDeliv.IsSuccessStatusCode, await cleanDeliv.Content.ReadAsStringAsync());
        Assert.Single(storage.Objects);

        // 2. Direct Project File (Task attachment): DOCX with vbaProject.bin macro -> 422 FILE_QUARANTINED; clean DOCX -> 201
        var countBefore = storage.Objects.Count;
        var macroDocx = BuildDocx(includeMacro: true);
        var badTaskFile = await student.PostAsync("/api/v1/files",
            MakeFileForm("file", "macro.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", macroDocx,
                new Dictionary<string, string> { ["parentType"] = "TASK", ["parentId"] = taskId.ToString() }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badTaskFile.StatusCode);
        Assert.Equal(WorkflowErrorCodes.FileQuarantined, await ReadProblemCode(badTaskFile));
        Assert.Equal(countBefore, storage.Objects.Count);

        var cleanDocx = BuildDocx(includeMacro: false);
        var okTaskFile = await student.PostAsync("/api/v1/files",
            MakeFileForm("file", "clean.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", cleanDocx,
                new Dictionary<string, string> { ["parentType"] = "TASK", ["parentId"] = taskId.ToString() }));
        Assert.True(okTaskFile.IsSuccessStatusCode, await okTaskFile.Content.ReadAsStringAsync());
        Assert.Equal(countBefore + 1, storage.Objects.Count);

        // 3. Chunked Upload Session (PROJECT_FILE): 25 MiB PDF rejected immediately with 413 PayloadTooLarge (POL-06 20 MiB doc cap)
        var oversizePdfSession = await student.PostAsJsonAsync("/api/v1/files/upload-sessions",
            new CreateProjectFileUploadSession("TASK", taskId, "huge.pdf", "application/pdf", 25L * 1024 * 1024));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizePdfSession.StatusCode);

        // 4. Department Document Template: macro DOCX -> 422 FILE_QUARANTINED; clean PDF -> 201
        countBefore = storage.Objects.Count;
        var badDeptDoc = await staff.PostAsync("/api/v1/academic/department-documents",
            MakeFileForm("file", "tpl-macro.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", macroDocx,
                new Dictionary<string, string> { ["majorId"] = itMajorId.ToString(), ["code"] = "TPL_SCAN_BAD", ["title"] = "Bad Template" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badDeptDoc.StatusCode);
        Assert.Equal(WorkflowErrorCodes.FileQuarantined, await ReadProblemCode(badDeptDoc));
        Assert.Equal(countBefore, storage.Objects.Count);

        var okDeptDoc = await staff.PostAsync("/api/v1/academic/department-documents",
            MakeFileForm("file", "tpl-ok.pdf", "application/pdf", ValidPdf,
                new Dictionary<string, string> { ["majorId"] = itMajorId.ToString(), ["code"] = "TPL_SCAN_OK", ["title"] = "Clean Template" }));
        Assert.True(okDeptDoc.IsSuccessStatusCode, await okDeptDoc.Content.ReadAsStringAsync());
        Assert.Equal(countBefore + 1, storage.Objects.Count);

        // 5. Scanner direct unit checks: ZIP path traversal & executable signature quarantined
        var scanner = new BasicSignatureAndArchiveScanner();
        using var traversalZip = new MemoryStream();
        using (var z = new ZipArchive(traversalZip, ZipArchiveMode.Create, leaveOpen: true))
            WriteEntry(z, "../../etc/passwd", "x");
        traversalZip.Position = 0;
        var zipScan = await scanner.ScanAsync(traversalZip, "evil.zip", "application/zip", default);
        Assert.False(zipScan.IsSafe);
        Assert.Equal(FileScanStatuses.Quarantined, zipScan.Status);
    }
}
