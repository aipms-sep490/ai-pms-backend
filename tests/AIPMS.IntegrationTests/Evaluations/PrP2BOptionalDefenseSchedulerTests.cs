using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class PrP2BOptionalDefenseSchedulerTests(EvaluationDraftDatabaseFixture database)
    : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static DateTime Now => EvaluationDraftDatabaseFixture.Now;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, JsonOpts)!;
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
    public async Task Optional_defense_scheduler_enforces_buffer_quorum_coi_conflicts_standby_revocation_and_non_blocking_mode()
    {
        var s = await database.Seed(true);
        long itMajorId, mktMajorId, primarySupervisorId;
        long eval1Id, eval2Id, eval3Id, eval4Id, standbyId;
        long roomId, committeeShortId, committeeCoiId, committeeValidId, defenseAssignmentId, project2Id, committee2Id;

        await using (var db = database.CreateContext())
        {
            var project = await db.Projects.SingleAsync(p => p.Id == s.ProjectId);
            project.TrackCode = "IT_MKT";
            itMajorId = await db.ProjectMajors.Where(pm => pm.ProjectId == s.ProjectId).Select(pm => pm.MajorId).FirstAsync();
            var mktDeptId = (await db.Users.SingleAsync(u => u.Id == s.Scope.Users.OtherLecturer)).DepartmentId!.Value;
            var mktMajor = new M.Major { DepartmentId = mktDeptId, Code = "MKT_" + Guid.NewGuid().ToString("N")[..4], Name = "Marketing", IsActive = true };
            db.Majors.Add(mktMajor);
            await db.SaveChangesAsync();
            mktMajorId = mktMajor.Id;
            db.ProjectMajors.Add(new M.ProjectMajor { ProjectId = s.ProjectId, MajorId = mktMajorId });

            primarySupervisorId = await db.SupervisorAssignments
                .Where(a => a.ProjectId == s.ProjectId && a.IsPrimary)
                .Select(a => a.SupervisorProfile.UserId).FirstAsync();

            var lecturerRoleId = await db.Roles.Where(r => r.Code == "LECTURER").Select(r => r.Id).SingleAsync();
            M.User MakeLecturer(string tag, long deptId) => new()
            {
                Email = $"def_{tag}_{Guid.NewGuid():N}@e2e.invalid",
                FullName = $"Lecturer {tag}",
                PasswordHash = "unused",
                Status = "ACTIVE",
                DepartmentId = deptId,
                UserRoleUsers = [new() { RoleId = lecturerRoleId }]
            };
            var u1 = MakeLecturer("E1", s.Scope.Users.DepartmentId);
            var u2 = MakeLecturer("E2", s.Scope.Users.DepartmentId);
            var u3 = MakeLecturer("E3", mktDeptId);
            var u4 = MakeLecturer("E4", mktDeptId);
            var uStandby = MakeLecturer("SB", s.Scope.Users.DepartmentId);
            db.Users.AddRange(u1, u2, u3, u4, uStandby);

            var room = new Room
            {
                Code = "H401_" + Guid.NewGuid().ToString("N")[..4],
                Building = "Alpha",
                Floor = 4,
                Capacity = 40,
                Status = "ACTIVE",
                RoomClusterType = "INTERDISCIPLINARY",
                EquipmentTagsJson = "[\"PROJECTOR\",\"HDMI\"]",
                CreatedAt = Now,
                UpdatedAt = Now
            };
            db.Set<Room>().Add(room);
            await db.SaveChangesAsync();

            eval1Id = u1.Id; eval2Id = u2.Id; eval3Id = u3.Id; eval4Id = u4.Id; standbyId = uStandby.Id;
            roomId = room.Id;

            var cShort = new DefenseCommittee
            {
                ProjectId = s.ProjectId, Name = "Short Committee", Status = "DRAFT", CreatedBy = s.Scope.Users.Staff, CreatedAt = Now,
                Members = [new() { UserId = eval1Id, MajorId = itMajorId, Role = "CHAIR" }, new() { UserId = eval3Id, MajorId = mktMajorId, Role = "MEMBER" }]
            };
            var cCoi = new DefenseCommittee
            {
                ProjectId = s.ProjectId, Name = "COI Committee", Status = "DRAFT", CreatedBy = s.Scope.Users.Staff, CreatedAt = Now,
                Members =
                [
                    new() { UserId = primarySupervisorId, MajorId = itMajorId, Role = "CHAIR" },
                    new() { UserId = eval2Id, MajorId = itMajorId, Role = "MEMBER" },
                    new() { UserId = eval3Id, MajorId = mktMajorId, Role = "SECRETARY" },
                    new() { UserId = eval4Id, MajorId = mktMajorId, Role = "MEMBER" }
                ]
            };
            var cValid = new DefenseCommittee
            {
                ProjectId = s.ProjectId, Name = "Valid 4-Member IT_MKT", Status = "CONFIRMED", CreatedBy = s.Scope.Users.Staff, CreatedAt = Now,
                Members =
                [
                    new() { UserId = eval1Id, MajorId = itMajorId, Role = "CHAIR" },
                    new() { UserId = eval2Id, MajorId = itMajorId, Role = "MEMBER" },
                    new() { UserId = eval3Id, MajorId = mktMajorId, Role = "SECRETARY" },
                    new() { UserId = eval4Id, MajorId = mktMajorId, Role = "MEMBER" }
                ]
            };
            db.Set<DefenseCommittee>().AddRange(cShort, cCoi, cValid);

            // Second project for 3D conflict testing
            var project2 = new M.Project
            {
                Code = "P2B_P2_" + Guid.NewGuid().ToString("N")[..5],
                Title = "Second Project",
                CreatedBy = s.Scope.Users.Student,
                Status = "FINAL_SUBMISSION",
                TrackCode = "IT_ONLY",
                Team = new()
                {
                    Code = "T2_" + Guid.NewGuid().ToString("N")[..5],
                    Name = "Team 2",
                    CreatedBy = s.Scope.Users.Student,
                    AcademicSemesterId = s.Scope.SemesterId,
                    Status = "LOCKED"
                },
                ProjectMajors = [new() { MajorId = itMajorId }]
            };
            db.Projects.Add(project2);
            await db.SaveChangesAsync();
            project2Id = project2.Id;
            await AcademicSnapshotFixture.AddAsync(db, project2Id, s.PeriodId, s.Scope.Users.DepartmentId, s.Scope.Users.Student, Now.AddDays(-1));

            var c2 = new DefenseCommittee
            {
                ProjectId = project2Id, Name = "Project 2 Committee", Status = "CONFIRMED", CreatedBy = s.Scope.Users.Staff, CreatedAt = Now,
                Members = [new() { UserId = eval2Id, MajorId = itMajorId, Role = "CHAIR" }]
            };
            db.Set<DefenseCommittee>().Add(c2);

            var policyVer = new PeriodPolicyVersion
            {
                ProjectPeriodId = s.PeriodId,
                Version = 1,
                Status = "PUBLISHED",
                EffectiveFrom = Now.AddDays(-10),
                EffectiveTo = DateTime.UtcNow.AddDays(60),
                SnapshotJson = "{}",
                ConcurrencyToken = Guid.NewGuid(),
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = Now.AddDays(-10)
            };
            db.Set<PeriodPolicyVersion>().Add(policyVer);
            await db.SaveChangesAsync();

            var scheme = new EvaluationScheme
            {
                Version = 1,
                ProjectId = s.ProjectId,
                ProjectPeriodId = s.PeriodId,
                Name = "Non-Blocking Defense Scheme",
                Status = "PUBLISHED",
                PassThreshold = 5.0m,
                ConcurrencyToken = Guid.NewGuid(),
                PolicyVersionId = policyVer.Id,
                StudentsJson = "[]",
                RegistrationSnapshotJson = "{}",
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = Now,
                PublishedBy = s.Scope.Users.Staff,
                PublishedAt = Now,
                Components =
                [
                    new EvaluationSchemeComponent
                    {
                        Name = "Defense Common",
                        Scope = "COMMON",
                        Stage = "DEFENSE",
                        RubricId = s.RubricId,
                        ProjectWeightPercent = 100m,
                        StudentWeightPercent = 100m,
                        RequiredEvaluators = 1
                    }
                ]
            };
            db.Set<EvaluationScheme>().Add(scheme);
            await db.SaveChangesAsync();

            // Create scoped DEFENSE assignment with defenseSessionId == null to verify non-blocking grading + standby revocation
            var defAssign = new EvaluationAssignment
            {
                ProjectId = s.ProjectId,
                EvaluatorId = eval1Id,
                RubricId = s.RubricId,
                ProjectPeriodId = s.PeriodId,
                DepartmentId = s.Scope.Users.DepartmentId,
                Stage = "DEFENSE",
                DefenseSessionId = null,
                Scope = "COMMON",
                ComponentId = scheme.Components[0].Id,
                PolicyVersionId = policyVer.Id,
                ScopeSnapshotJson = JsonSerializer.Serialize(new { Committee = new { CommitteeId = cValid.Id, Version = cValid.Version } }),
                Status = "ACTIVE",
                AssignedBy = s.Scope.Users.Staff,
                AssignedAt = Now,
                ValidFrom = Now.AddDays(-1),
                ValidUntil = DateTime.UtcNow.AddDays(30),
                ConcurrencyToken = Guid.NewGuid()
            };
            db.Set<EvaluationAssignment>().Add(defAssign);
            await db.SaveChangesAsync();

            committeeShortId = cShort.Id; committeeCoiId = cCoi.Id; committeeValidId = cValid.Id;
            committee2Id = c2.Id; defenseAssignmentId = defAssign.Id;
        }

        using var app = new EvaluationFactory(database);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin, roles: ["ADMIN"]);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var eval1 = app.CreateAuthenticatedClient(eval1Id, roles: ["LECTURER"]);

        // 1. Non-blocking regression: by default EnableDefenseRoomScheduling == false;
        //    - Non-Admin cannot toggle (403)
        //    - Scheduling V4 session is disabled (409)
        //    - Grading DEFENSE assignment with defenseSessionId == null STILL SUCCEEDS!
        var nonAdminToggle = await staff.PatchAsJsonAsync(
            $"/api/v1/academic/project-periods/{s.PeriodId}/defense-room-scheduling",
            new UpdatePeriodDefenseRoomSchedulingRequest(true));
        Assert.Equal(HttpStatusCode.Forbidden, nonAdminToggle.StatusCode);

        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        if (futureDate.DayOfWeek == DayOfWeek.Sunday) futureDate = futureDate.AddDays(1);
        var disabledSchedule = await staff.PostAsJsonAsync("/api/v1/defense-scheduling/v4/sessions",
            new ScheduleDefenseV4Request(s.ProjectId, committeeValidId, roomId, futureDate, 1));
        Assert.Equal(HttpStatusCode.Conflict, disabledSchedule.StatusCode);

        // With EnableDefenseRoomScheduling == false and DefenseSessionId == null, evaluator can access DEFENSE assignment without any defense room
        var nonBlockingAccess = await eval1.GetAsync($"/api/v1/evaluation-assignments/{defenseAssignmentId}");
        Assert.Equal(HttpStatusCode.OK, nonBlockingAccess.StatusCode);

        // 2. Admin enables optional defense room scheduling with coldWindowEndAt + 12h buffer
        var slot1 = DefenseSchedulingRules.SlotV4(futureDate, 1, isInterdisciplinary: true);
        var coldEndTooClose = slot1.Start.UtcDateTime.AddHours(-6);
        var enabled = await Body<PeriodDefenseRoomSchedulingDto>(await admin.PatchAsJsonAsync(
            $"/api/v1/academic/project-periods/{s.PeriodId}/defense-room-scheduling",
            new UpdatePeriodDefenseRoomSchedulingRequest(true, coldEndTooClose, 12)));
        Assert.True(enabled.EnableDefenseRoomScheduling);

        // 3. Early schedule (< coldWindowEndAt + 12h) -> 409 COLD_WINDOW_NOT_COMPLETED
        var tooEarly = await staff.PostAsJsonAsync("/api/v1/defense-scheduling/v4/sessions",
            new ScheduleDefenseV4Request(s.ProjectId, committeeValidId, roomId, futureDate, 1));
        Assert.Equal(HttpStatusCode.Conflict, tooEarly.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ColdWindowNotCompleted, await ReadProblemCode(tooEarly));

        // Move coldWindowEndAt back to 24h before slot1
        await Body<PeriodDefenseRoomSchedulingDto>(await admin.PatchAsJsonAsync(
            $"/api/v1/academic/project-periods/{s.PeriodId}/defense-room-scheduling",
            new UpdatePeriodDefenseRoomSchedulingRequest(true, slot1.Start.UtcDateTime.AddHours(-24), 12)));

        // 4. IT_MKT committee < 4 members -> 422 COUNCIL_QUORUM_UNMET
        var quorumFail = await staff.PostAsJsonAsync("/api/v1/defense-scheduling/v4/sessions",
            new ScheduleDefenseV4Request(s.ProjectId, committeeShortId, roomId, futureDate, 1));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, quorumFail.StatusCode);
        Assert.Equal(WorkflowErrorCodes.CouncilQuorumUnmet, await ReadProblemCode(quorumFail));

        // 5. Committee containing primary supervisor -> 409 EVALUATOR_ROLE_CONFLICT
        var coiFail = await staff.PostAsJsonAsync("/api/v1/defense-scheduling/v4/sessions",
            new ScheduleDefenseV4Request(s.ProjectId, committeeCoiId, roomId, futureDate, 1));
        Assert.Equal(HttpStatusCode.Conflict, coiFail.StatusCode);
        Assert.Equal(WorkflowErrorCodes.EvaluatorRoleConflict, await ReadProblemCode(coiFail));

        // 6. Positive: Tech Check + Schedule valid 4-member IT_MKT session (60m V4_45_60)
        var tc = await Body<DefenseTechCheckDto>(await staff.PostAsJsonAsync("/api/v1/defense-scheduling/v4/tech-checks",
            new RecordDefenseTechCheckRequest(s.ProjectId, roomId, "PASSED", "HDMI and projector checked")));
        Assert.Equal("PASSED", tc.Status);

        var session = await Body<DefenseSessionV4Dto>(await staff.PostAsJsonAsync("/api/v1/defense-scheduling/v4/sessions",
            new ScheduleDefenseV4Request(s.ProjectId, committeeValidId, roomId, futureDate, 1)));
        Assert.Equal(DefenseSchedulingRules.V4PolicyVersion, session.SchedulePolicyVersion);
        Assert.Equal(60, session.SlotDurationMinutes);

        // 7. 3D Conflict: second project booking same room/slot -> 409 SCHEDULE_CONFLICT
        var conflict = await staff.PostAsJsonAsync("/api/v1/defense-scheduling/v4/sessions",
            new ScheduleDefenseV4Request(project2Id, committee2Id, roomId, futureDate, 1));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ScheduleConflict, await ReadProblemCode(conflict));

        // 8. Change Request >= 24h returns SwapCandidates; < 24h returns 422
        var proposal = await Body<DefenseChangeProposalV4Dto>(await staff.PostAsJsonAsync(
            "/api/v1/defense-scheduling/v4/change-requests",
            new CreateDefenseChangeProposalV4Request(session.Id, "SCHEDULE_CLASH", "Evaluator requested alternative slot")));
        Assert.NotEmpty(proposal.SwapCandidates);

        await using (var dbCheck = database.CreateContext())
        {
            var soonSession = new DefenseSession
            {
                ProjectId = project2Id,
                CommitteeId = committee2Id,
                StartAt = Now.AddHours(6),
                EndAt = Now.AddHours(7),
                RoomId = roomId,
                Room = session.RoomCode,
                Status = "SCHEDULED",
                SchedulePolicyVersion = DefenseSchedulingRules.V4PolicyVersion,
                SlotDurationMinutes = 45,
                CreatedBy = s.Scope.Users.Staff,
                CreatedAt = Now,
                ConcurrencyToken = Guid.NewGuid()
            };
            dbCheck.Set<DefenseSession>().Add(soonSession);
            await dbCheck.SaveChangesAsync();
            var lateChange = await staff.PostAsJsonAsync(
                "/api/v1/defense-scheduling/v4/change-requests",
                new CreateDefenseChangeProposalV4Request(soonSession.Id, "SCHEDULE_CLASH", "Too late change request"));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, lateChange.StatusCode);
        }

        // 9. Standby Pool 1-click swap revokes eval1's assignment -> immediate grading attempt returns 403 ASSIGNMENT_REVOKED
        // Pure Admin only toggles period config; Admin cannot directly schedule rooms or swap standby pool (403 SCOPE_ACCESS_DENIED)
        var adminStandbyBlocked = await admin.PostAsJsonAsync("/api/v1/defense-scheduling/v4/standby-pool",
            new AddStandbyPoolMemberRequest(s.PeriodId, standbyId, itMajorId, s.Scope.Users.DepartmentId));
        Assert.Equal(HttpStatusCode.Forbidden, adminStandbyBlocked.StatusCode);
        Assert.Equal(WorkflowErrorCodes.ScopeAccessDenied, await ReadProblemCode(adminStandbyBlocked));

        var adminSwapBlocked = await admin.PostAsJsonAsync(
            $"/api/v1/defense-scheduling/v4/sessions/{session.Id}/standby-swap",
            new StandbySwapRequest(eval1Id, standbyId, "Admin should not intervene"));
        Assert.Equal(HttpStatusCode.Forbidden, adminSwapBlocked.StatusCode);

        await Body<StandbyPoolMemberDto>(await staff.PostAsJsonAsync("/api/v1/defense-scheduling/v4/standby-pool",
            new AddStandbyPoolMemberRequest(s.PeriodId, standbyId, itMajorId, s.Scope.Users.DepartmentId)));

        var swapped = await Body<StandbySwapResultDto>(await staff.PostAsJsonAsync(
            $"/api/v1/defense-scheduling/v4/sessions/{session.Id}/standby-swap",
            new StandbySwapRequest(eval1Id, standbyId, "Evaluator sick leave emergency")));
        Assert.Equal(1, swapped.RevokedAssignmentsCount);
        Assert.Equal(1, swapped.CreatedAssignmentsCount);

        var revokedGrading = await eval1.PostAsync($"/api/v1/evaluation-assignments/{defenseAssignmentId}/evaluation", null);
        Assert.Equal(HttpStatusCode.Forbidden, revokedGrading.StatusCode);
        Assert.Equal(WorkflowErrorCodes.AssignmentRevoked, await ReadProblemCode(revokedGrading));
    }
}
