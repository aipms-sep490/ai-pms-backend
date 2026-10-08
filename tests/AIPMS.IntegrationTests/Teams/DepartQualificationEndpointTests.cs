using System.Net;
using System.Net.Http.Json;
using System.Text;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Features.StudentQualifications.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using FileEntity = AIPMS.Infrastructure.Persistence.Generated.Models.File;

namespace AIPMS.IntegrationTests.Teams;

public sealed partial class TeamEndpointTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Depart_D02_upload_without_project_is_atomic_and_replacement_invalidates_review(bool failAudit)
    {
        var s = await database.SeedAsync();
        var staffId = await AddStaffAsync(s);
        var storage = new DepartUploadStorage();
        using var app = new TeamTestFactory(database, s, failAuditAction: failAudit ? "QUALIFICATION_CERTIFICATE_UPLOADED" : null,
            customizeServices: services => { services.RemoveAll<IFileStorage>(); services.AddSingleton<IFileStorage>(storage); });
        using var student = app.CreateAuthenticatedClient(s.Students[0]);
        using var staff = app.CreateAuthenticatedClient(staffId, roles: ["DEPARTMENT_STAFF"]);
        static MultipartFormDataContent Upload(string text, string mime = "application/pdf", string name = "certificate.pdf")
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
            form.Add(file, "file", name);
            return form;
        }
        const string route = "/api/v1/student-qualifications/me/certificate";
        using var content = Upload("%PDF-1.7 certificate");
        var response = await student.PostAsync(route, content);
        if (failAudit)
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Empty(storage.Objects);
            await using var db = database.CreateContext();
            Assert.False(await db.Files.AnyAsync(f => f.UploadedBy == s.Students[0]));
            Assert.False(await db.Set<StudentQualification>().AnyAsync(q => q.UserId == s.Students[0]));
            return;
        }
        var first = await BodyAsync<StudentQualificationDto>(response);
        var certificate = $"/api/v1/student-qualifications/{first.Id}/certificate";
        var bytes = await staff.GetByteArrayAsync(certificate + "/download");
        var metadata = await BodyAsync<StudentQualificationCertificateDto>(await student.GetAsync(certificate));
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), metadata.ChecksumSha256);
        await BodyAsync<StudentQualificationDto>(await staff.PostAsJsonAsync($"/api/v1/student-qualifications/{first.Id}/verify",
            new { expectedConcurrencyToken = first.ConcurrencyToken }));
        using var replacement = Upload("%PDF-1.7 revised");
        var second = await BodyAsync<StudentQualificationDto>(await student.PostAsync(route, replacement));
        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(first.CertificateFileId, second.CertificateFileId);
        Assert.NotEqual(first.ConcurrencyToken, second.ConcurrencyToken);
        Assert.Equal("PENDING_VERIFICATION", second.VerificationStatus);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/student-qualifications/{first.Id}/verify",
            new { expectedConcurrencyToken = first.ConcurrencyToken })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/api/v1/files/{first.CertificateFileId}/download")).StatusCode);
        using var badType = Upload("text", "text/plain", "file.txt");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await student.PostAsync(route, badType)).StatusCode);
        using var badSignature = Upload("not a PDF");
        Assert.Equal(HttpStatusCode.BadRequest, (await student.PostAsync(route, badSignature)).StatusCode);
    }

    private sealed class DepartUploadStorage : IFileStorage
    {
        public System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Objects { get; } = new();
        public async Task WriteAsync(string key, Stream content, CancellationToken ct)
        { using var output = new MemoryStream(); await content.CopyToAsync(output, ct); Objects[key] = output.ToArray(); }
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => Objects.TryGetValue(key, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes)) : throw new FileNotFoundException();
        public Task DeleteAsync(string key, CancellationToken ct) { Objects.TryRemove(key, out _); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Depart_D04_resubmitted_evidence_rejects_old_token_and_audits_reviewed_version()
    {
        var s = await database.SeedAsync();
        var staffId = await AddStaffAsync(s);
        using var app = new TeamTestFactory(database, s);
        using var student = app.CreateAuthenticatedClient(s.Students[0]);
        using var staff = app.CreateAuthenticatedClient(staffId, roles: ["DEPARTMENT_STAFF"]);
        var request = new SubmitStudentQualificationEvidenceRequest("CAPSTONE_READINESS", "TRAINING_COMPLETED",
            "CERT-FIRST", null, TeamDatabaseFixture.Now.AddDays(-1), TeamDatabaseFixture.Now.AddYears(1));
        var first = await BodyAsync<StudentQualificationDto>(await student.PostAsJsonAsync(
            "/api/v1/student-qualifications/me/evidence", request));
        var second = await BodyAsync<StudentQualificationDto>(await student.PostAsJsonAsync(
            "/api/v1/student-qualifications/me/evidence", request with { CertificateNumber = "CERT-SECOND" }));
        Assert.NotEqual(first.ConcurrencyToken, second.ConcurrencyToken);
        var url = $"/api/v1/student-qualifications/{second.Id}";
        foreach (var decision in new[] { "verify", "reject" })
            Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url + "/" + decision,
                new { expectedConcurrencyToken = first.ConcurrencyToken, reason = "Stale review" })).StatusCode);
        await using (var check = database.CreateContext())
        {
            Assert.Equal("PENDING_VERIFICATION", (await check.Set<StudentQualification>().FindAsync(second.Id))!.VerificationStatus);
            Assert.False(await check.AuditLogs.AnyAsync(a => a.EntityId == second.Id.ToString()
                && (a.Action == "STUDENT_QUALIFICATION_VERIFIED" || a.Action == "STUDENT_QUALIFICATION_REJECTED")));
        }
        var verified = await BodyAsync<StudentQualificationDto>(await staff.PostAsJsonAsync(url + "/verify",
            new { expectedConcurrencyToken = second.ConcurrencyToken }));
        Assert.Equal("VERIFIED", verified.VerificationStatus);
        await using var db = database.CreateContext();
        var audit = await db.AuditLogs.SingleAsync(a => a.EntityId == second.Id.ToString() && a.Action == "STUDENT_QUALIFICATION_VERIFIED");
        Assert.Contains(second.ConcurrencyToken.ToString(), audit.DetailsJson);
    }

    [Fact]
    public async Task Depart_D02_certificate_is_owner_scoped_streamed_and_resolves_current_file_only()
    {
        var s = await database.SeedAsync();
        var outside = await database.SeedAsync();
        var staffId = await AddStaffAsync(s);
        var foreignId = await AddStaffAsync(outside);
        var qualificationId = await AddPendingQualificationAsync(s, s.Students[0]);
        long firstId;
        await using (var setup = database.CreateContext())
        {
            var file = new FileEntity { UploadedBy = s.Students[0], OriginalFileName = "certificate.pdf",
                MimeType = "application/pdf", FileSizeBytes = 4, StoragePath = "private-key",
                FileUrl = "https://must-not-be-returned.invalid/certificate" };
            setup.Files.Add(file);
            await setup.SaveChangesAsync();
            firstId = file.Id;
            (await setup.Set<StudentQualification>().FindAsync(qualificationId))!.CertificateFileId = firstId;
            await setup.SaveChangesAsync();
        }
        using var app = new TeamTestFactory(database, s, customizeServices: services =>
        {
            services.RemoveAll<IFileStorage>();
            services.AddSingleton<IFileStorage, DepartCertificateStorage>();
        });
        using var owner = app.CreateAuthenticatedClient(s.Students[0]);
        using var otherStudent = app.CreateAuthenticatedClient(s.Students[1]);
        using var staff = app.CreateAuthenticatedClient(staffId, roles: ["DEPARTMENT_STAFF"]);
        using var foreign = app.CreateAuthenticatedClient(foreignId, roles: ["DEPARTMENT_STAFF"]);
        using var anonymous = app.CreateClient();
        var url = $"/api/v1/student-qualifications/{qualificationId}/certificate";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        foreach (var denied in new[] { otherStudent, foreign })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync(url + "/download")).StatusCode);
        }
        var metadata = await owner.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        var json = await metadata.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-key", json);
        Assert.DoesNotContain("must-not-be-returned", json);
        var download = await staff.GetAsync(url + "/download");
        Assert.Equal("%PDF", await download.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.True(download.Headers.CacheControl!.NoStore);
        foreach (var actor in new[] { owner, staff, foreign })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await actor.GetAsync($"/api/v1/files/{firstId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await actor.GetAsync($"/api/v1/files/{firstId}/download")).StatusCode);
        }
        await using (var setup = database.CreateContext())
        {
            var replacement = new FileEntity { UploadedBy = s.Students[0], OriginalFileName = "replacement.pdf",
                MimeType = "application/pdf", FileSizeBytes = 4, StoragePath = "missing-key" };
            setup.Files.Add(replacement);
            await setup.SaveChangesAsync();
            (await setup.Set<StudentQualification>().FindAsync(qualificationId))!.CertificateFileId = replacement.Id;
            await setup.SaveChangesAsync();
        }
        Assert.NotEqual(firstId, (await BodyAsync<StudentQualificationCertificateDto>(await owner.GetAsync(url))).FileId);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(url + "/download")).StatusCode);
        await using (var setup = database.CreateContext())
        {
            (await setup.Users.FindAsync(staffId))!.Status = "INACTIVE";
            await setup.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(url)).StatusCode);
    }

    private sealed class DepartCertificateStorage : IFileStorage
    {
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct) => key == "private-key"
            ? Task.FromResult<Stream>(new MemoryStream(Encoding.ASCII.GetBytes("%PDF")))
            : throw new FileNotFoundException();
        public Task WriteAsync(string key, Stream content, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    }
}
