using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Files;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class PrP0CUploadSnapshotAndPreviewTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static DateTime Now => EvaluationDraftDatabaseFixture.Now;

    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<string?> ReadProblemCode(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (doc.RootElement.TryGetProperty("code", out var code)) return code.GetString();
        return null;
    }

    private static MultipartFormDataContent MakeGateFileForm(long reqId, string token, int expectedVersion, string fileName, string contentType, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "File", fileName);
        form.Add(new StringContent(reqId.ToString()), "RequirementId");
        form.Add(new StringContent(expectedVersion.ToString()), "ExpectedVersion");
        form.Add(new StringContent(token), "ConcurrencyToken");
        return form;
    }

    [Fact]
    public async Task Gate_upload_quarantine_idempotent_snapshot_pinned_review_signed_stream_and_restricted_delete()
    {
        var s = await database.Seed(lockedSubmission: false, confirmedCommittee: false);
        long gateId;
        long reqId;
        long assignmentId;
        long protectedFileId;
        long protectedTaskId;
        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.Status = "ACTIVE";
            var tm = await db.TeamMembers.SingleAsync(m => m.TeamId == project.TeamId && m.UserId == s.Scope.Users.Student);
            tm.IsLeader = true;

            var template = new ReviewGateTemplate
            {
                ProjectPeriodId = s.PeriodId,
                Code = "P0C_" + Guid.NewGuid().ToString("N")[..6],
                Name = "P0C Template",
                Mode = "SINGLE_MAJOR",
                Major = "IT",
                Version = 1,
                Status = "PUBLISHED",
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            db.ReviewGateTemplates.Add(template);
            await db.SaveChangesAsync();

            var gate = new ProjectGateInstance
            {
                ProjectId = s.ProjectId,
                TemplateId = template.Id,
                TemplateVersion = 1,
                Code = "G1",
                Sequence = 1,
                Title = "Gate 1",
                Objective = "Charter",
                Status = "PLANNED",
                ReviewEligibility = "READY",
                WindowStart = Now.AddDays(-1),
                WindowEnd = Now.AddDays(5),
                CreatedAt = Now
            };
            db.ProjectGateInstances.Add(gate);
            await db.SaveChangesAsync();
            gateId = gate.Id;

            var req = new ProjectGateRequirement
            {
                GateInstanceId = gate.Id,
                Code = "REQ-CHARTER",
                Title = "Project Charter",
                Type = "FILE",
                Scope = "COMMON",
                Required = true,
                VerificationStatus = "PENDING"
            };
            db.ProjectGateRequirements.Add(req);
            await db.SaveChangesAsync();
            reqId = req.Id;

            var revAssign = new GateReviewerAssignment
            {
                GateInstanceId = gate.Id,
                ReviewerId = s.Scope.Users.Lecturer,
                Scope = "COMMON",
                Status = "PENDING_REVIEW",
                CreatedAt = Now
            };
            db.GateReviewerAssignments.Add(revAssign);

            // Seed a Milestone + Task + File + ProjectEvidence reference to test 409 RESTRICTED_DELETE
            var milestone = new M.Milestone
            {
                ProjectId = s.ProjectId,
                Title = "M1",
                SortOrder = 1,
                Status = "IN_PROGRESS",
                DueDate = DateOnly.FromDateTime(Now.AddDays(10)),
                CreatedBy = s.Scope.Users.Student,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = Now,
                UpdatedAt = Now
            };
            db.Milestones.Add(milestone);
            await db.SaveChangesAsync();

            var task = new M.Task
            {
                MilestoneId = milestone.Id,
                Title = "Referenced Task",
                Status = "IN_PROGRESS",
                Priority = "MEDIUM",
                CreatedBy = s.Scope.Users.Student,
                CreatedAt = Now,
                UpdatedAt = Now,
                ConcurrencyToken = Guid.NewGuid()
            };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            protectedTaskId = task.Id;

            var file = new M.File
            {
                UploadedBy = s.Scope.Users.Student,
                TaskId = task.Id,
                OriginalFileName = "locked-proof.pdf",
                StoredFileName = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
                StoragePath = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
                MimeType = "application/pdf",
                FileSizeBytes = 128,
                ChecksumSha256 = new string('a', 64),
                ScanStatus = FileScanStatuses.Ready,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            db.Files.Add(file);
            await db.SaveChangesAsync();
            protectedFileId = file.Id;

            db.Set<M.ProjectEvidence>().AddRange(
                new M.ProjectEvidence
                {
                    ProjectId = s.ProjectId,
                    SourceType = "FILE",
                    SourceId = file.Id,
                    FileId = file.Id,
                    VerificationStatus = "VERIFIED",
                    SubmittedBy = s.Scope.Users.Student,
                    SubmittedAt = Now
                },
                new M.ProjectEvidence
                {
                    ProjectId = s.ProjectId,
                    SourceType = "TASK",
                    SourceId = task.Id,
                    TaskId = task.Id,
                    VerificationStatus = "VERIFIED",
                    SubmittedBy = s.Scope.Users.Student,
                    SubmittedAt = Now
                });
            await db.SaveChangesAsync();
            assignmentId = revAssign.Id;
        }

        using var app = new EvaluationFactory(database);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student, roles: ["STUDENT"]);
        using var reviewer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer, roles: ["LECTURER"]);
        using var outsider = app.CreateAuthenticatedClient(s.Scope.Users.OtherLecturer, roles: ["LECTURER"]);

        // 1. Disguised executable uploaded as .pdf => QUARANTINED; blocked on download & blocked on SubmitForReview (422)
        var t0 = await Body<StudentReviewTimelineDto>(await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/review-gates/timeline"));
        var g1Token = t0.Gates.Single(g => g.Code == "G1").ConcurrencyToken;
        using (var fakeForm = MakeGateFileForm(reqId, g1Token, 0, "malware.pdf", "application/pdf", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]))
        {
            var tQuar = await Body<StudentReviewTimelineDto>(await leader.PostAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G1/evidence/file", fakeForm));
            var art = tQuar.Gates.Single(g => g.Code == "G1").Requirements.Single().LatestArtifact!;
            Assert.Equal(FileScanStatuses.Quarantined, art.ScanStatus);
            g1Token = tQuar.Gates.Single(g => g.Code == "G1").ConcurrencyToken;
        }
        var downloadQuar = await leader.GetAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G1/requirements/{reqId}/evidence/1/download");
        Assert.Equal(HttpStatusCode.Conflict, downloadQuar.StatusCode);
        Assert.Equal("FILE_QUARANTINED", await ReadProblemCode(downloadQuar));

        using (var submitQuarReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/projects/{s.ProjectId}/review-gates/G1/submit-for-review")
        {
            Content = JsonContent.Create(new SubmitReviewGateForReviewRequest("Try submit quarantined", g1Token))
        })
        {
            submitQuarReq.Headers.Add("Idempotency-Key", "idem-quar-001");
            var resQuar = await leader.SendAsync(submitQuarReq);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, resQuar.StatusCode);
        }

        // 2. Upload valid v2 PDF => READY; SubmitForReview with Idempotency-Key => creates immutable GatePackageSnapshot
        byte[] validPdf = "%PDF-1.7\nValid Gate Charter v2"u8.ToArray();
        using (var okForm = MakeGateFileForm(reqId, g1Token, 1, "charter-v2.pdf", "application/pdf", validPdf))
        {
            var tOk = await Body<StudentReviewTimelineDto>(await leader.PostAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G1/evidence/file", okForm));
            var art = tOk.Gates.Single(g => g.Code == "G1").Requirements.Single().LatestArtifact!;
            Assert.Equal(2, art.Version);
            Assert.Equal(FileScanStatuses.Ready, art.ScanStatus);
            g1Token = tOk.Gates.Single(g => g.Code == "G1").ConcurrencyToken;
        }

        // TEST-P0-05: Idempotency-Key retry returns same snapshotId; different payload with same key => 409 IDEMPOTENCY_CONFLICT
        long snapshotId;
        using (var sub1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/projects/{s.ProjectId}/review-gates/G1/submit-for-review")
        {
            Content = JsonContent.Create(new SubmitReviewGateForReviewRequest("Round 1", g1Token))
        })
        {
            sub1.Headers.Add("Idempotency-Key", "idem-round-1");
            var tSub1 = await Body<StudentReviewTimelineDto>(await leader.SendAsync(sub1));
            snapshotId = tSub1.Gates.Single(g => g.Code == "G1").LatestPackageId!.Value;
            Assert.Equal(1, tSub1.Gates.Single(g => g.Code == "G1").LatestRoundNumber);
            g1Token = tSub1.Gates.Single(g => g.Code == "G1").ConcurrencyToken;
        }
        // Retry with SAME key & SAME payload => idempotent, NO duplicate snapshot row
        using (var subRetry = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/projects/{s.ProjectId}/review-gates/G1/submit-for-review")
        {
            Content = JsonContent.Create(new SubmitReviewGateForReviewRequest("Round 1", g1Token))
        })
        {
            subRetry.Headers.Add("Idempotency-Key", "idem-round-1");
            var tRetry = await Body<StudentReviewTimelineDto>(await leader.SendAsync(subRetry));
            Assert.Equal(snapshotId, tRetry.Gates.Single(g => g.Code == "G1").LatestPackageId);
        }
        // Same key + DIFFERENT payload => 409 IDEMPOTENCY_CONFLICT
        using (var subConflict = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/projects/{s.ProjectId}/review-gates/G1/submit-for-review")
        {
            Content = JsonContent.Create(new SubmitReviewGateForReviewRequest("Tampered notes", g1Token))
        })
        {
            subConflict.Headers.Add("Idempotency-Key", "idem-round-1");
            var resConflict = await leader.SendAsync(subConflict);
            Assert.Equal(HttpStatusCode.Conflict, resConflict.StatusCode);
            Assert.Equal(WorkflowErrorCodes.IdempotencyConflict, await ReadProblemCode(resConflict));
        }

        // Upload v3 AFTER snapshot was frozen -> snapshot MUST stay pinned to v2!
        using (var v3Form = MakeGateFileForm(reqId, g1Token, 2, "charter-v3.pdf", "application/pdf", "%PDF-1.7\nCharter v3"u8.ToArray()))
        {
            await Body<StudentReviewTimelineDto>(await leader.PostAsync($"/api/v1/projects/{s.ProjectId}/review-gates/G1/evidence/file", v3Form));
        }

        // TEST-P0-03: Reviewer trying to verify v3 (or wrong version 1) against snapshot pinned to v2 => 409 STALE_EVIDENCE_VERSION
        string assignToken;
        await using (var db = database.CreateContext())
        {
            assignToken = (await db.GateReviewerAssignments.SingleAsync(a => a.Id == assignmentId)).ConcurrencyToken.ToString("N");
        }
        var staleVerify = await reviewer.PostAsJsonAsync($"/api/v1/review-gates/assignments/{assignmentId}/evidence/{reqId}/verify",
            new VerifyReviewGateEvidenceRequest(reqId.ToString(), "ACCEPTED", "Stale", assignToken, PackageId: snapshotId, EvidenceVersion: 3));
        Assert.Equal(HttpStatusCode.Conflict, staleVerify.StatusCode);
        Assert.Equal(WorkflowErrorCodes.StaleEvidenceVersion, await ReadProblemCode(staleVerify));

        // Verifying exact pinned v2 inside snapshot succeeds and records PackageId!
        var okVerify = await Body<AssignedReviewItemDto>(await reviewer.PostAsJsonAsync(
            $"/api/v1/review-gates/assignments/{assignmentId}/evidence/{reqId}/verify",
            new VerifyReviewGateEvidenceRequest(reqId.ToString(), "ACCEPTED", "Pinned v2 OK", assignToken, PackageId: snapshotId, EvidenceVersion: 2)));
        Assert.Equal(snapshotId, okVerify.LatestPackageId);

        // 3. PR-P0-C2 Signed Stream Preview (TEST-P0-02): Reviewer issues token & streams PDF; outsider reusing token => 403
        var previewToken = await Body<SignedPreviewTokenDto>(await reviewer.PostAsync(
            $"/api/v1/projects/{s.ProjectId}/review-gates/G1/requirements/{reqId}/evidence/2/preview-token", null));
        Assert.Equal(FileScanStatuses.Ready, previewToken.ScanStatus);
        var streamOk = await reviewer.GetAsync(previewToken.StreamUrl);
        Assert.Equal(HttpStatusCode.OK, streamOk.StatusCode);
        Assert.Equal("%PDF-1.7\nValid Gate Charter v2", await streamOk.Content.ReadAsStringAsync());

        // Outsider trying to replay Reviewer's signed token => 403 FORBIDDEN
        var streamStolen = await outsider.GetAsync(previewToken.StreamUrl);
        Assert.Equal(HttpStatusCode.Forbidden, streamStolen.StatusCode);

        // 4. P0.8 Restricted Delete (409 RESTRICTED_DELETE): deleting referenced File or Task is rejected!
        var delFileRes = await leader.DeleteAsync($"/api/v1/files/{protectedFileId}");
        Assert.Equal(HttpStatusCode.Conflict, delFileRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.RestrictedDelete, await ReadProblemCode(delFileRes));

        var delTaskRes = await leader.DeleteAsync($"/api/v1/tasks/{protectedTaskId}");
        Assert.Equal(HttpStatusCode.Conflict, delTaskRes.StatusCode);
        Assert.Equal(WorkflowErrorCodes.RestrictedDelete, await ReadProblemCode(delTaskRes));
    }
}
