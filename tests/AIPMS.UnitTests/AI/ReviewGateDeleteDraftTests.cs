using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.EntityFrameworkCore;
using Xunit;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.UnitTests.AI;

public sealed class ReviewGateDeleteDraftTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task DeleteTemplate_DeletesDraftWithChildrenAndAudits_ButBlocksPublishedAndInUseTemplates()
    {
        await using var db = SeedDb();
        var audit = new RecordingAudit();
        var svc = new ReviewGateService(
            db,
            new StaffUser(1),
            DirectTransactionProxy.Create(),
            audit,
            new FixedClock(),
            new NoopStorage(),
            null!,
            null!,
            null!,
            null!);

        // 1. Deleting DRAFT removes template, gate definitions, and requirements + writes audit
        await svc.DeleteTemplate(101, Ct);
        Assert.Null(await db.ReviewGateTemplates.FindAsync([101L], Ct));
        Assert.Empty(db.ReviewGateDefinitions.Where(g => g.TemplateId == 101));
        Assert.Empty(db.ReviewGateRequirements.Where(r => r.Id == 1001));
        Assert.Contains(audit.Entries, e => e.Action == "REVIEW_GATE_TEMPLATE_DELETED" && e.EntityId == 101);

        // 2. Deleting PUBLISHED template is rejected with ConflictException
        var pubEx = await Assert.ThrowsAsync<ConflictException>(() => svc.DeleteTemplate(102, Ct));
        Assert.Contains("Bản nháp", pubEx.Message);
        Assert.NotNull(await db.ReviewGateTemplates.FindAsync([102L], Ct));

        // 3. Deleting DRAFT template referenced by a project gate instance is rejected with ConflictException
        var inUseEx = await Assert.ThrowsAsync<ConflictException>(() => svc.DeleteTemplate(103, Ct));
        Assert.Contains("đang được sử dụng", inUseEx.Message);
        Assert.NotNull(await db.ReviewGateTemplates.FindAsync([103L], Ct));
    }

    private static AipmsDbContext SeedDb()
    {
        var options = new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase("ReviewGateDeleteDraft_" + Guid.NewGuid().ToString("N"))
            .Options;
        var db = new AipmsDbContext(options);
        var now = new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);

        db.Organizations.Add(new M.Organization { Id = 1, Code = "FPT", Name = "FPT", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Departments.Add(new M.Department { Id = 10, OrganizationId = 1, Code = "IT", Name = "CNTT", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Majors.Add(new M.Major { Id = 100, DepartmentId = 10, Code = "SE", Name = "Kỹ thuật phần mềm", IsActive = true, CreatedAt = now, UpdatedAt = now });
        db.Roles.Add(new M.Role { Id = 1, Code = "DEPARTMENT_STAFF", Name = "Department Staff", CreatedAt = now });
        db.Users.Add(new M.User { Id = 1, Email = "staff@fpt.edu.vn", FullName = "Staff IT", PasswordHash = "x", Status = "ACTIVE", DepartmentId = 10, CreatedAt = now, UpdatedAt = now });
        db.UserRoles.Add(new M.UserRole { UserId = 1, RoleId = 1, AssignedAt = now });
        db.AcademicSemesters.Add(new M.AcademicSemester { Id = 1, OrganizationId = 1, Code = "FA26", Name = "Fall 2026", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 31), Status = "ACTIVE", CreatedAt = now, UpdatedAt = now });
        db.ProjectPeriods.Add(new M.ProjectPeriod { Id = 50, AcademicSemesterId = 1, Code = "REG26", Name = "Đăng ký FA26", PeriodType = "REGISTRATION", Status = "ACTIVE", StartAt = now.AddDays(-10), EndAt = now.AddDays(30), CreatedAt = now, UpdatedAt = now });

        db.ReviewGateTemplates.AddRange(
            new ReviewGateTemplate
            {
                Id = 101,
                ProjectPeriodId = 50,
                Code = "DRAFT-TPL",
                Name = "Bản nháp có thể xóa",
                Mode = "INTERDISCIPLINARY",
                Major = "",
                Status = "DRAFT",
                Version = 1,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedBy = 1,
                CreatedAt = now,
                UpdatedAt = now,
                Gates =
                [
                    new ReviewGateDefinition
                    {
                        Id = 501,
                        Code = "G1",
                        Sequence = 1,
                        Title = "Cổng 1",
                        Objective = "Kiểm tra tiến độ",
                        Required = true,
                        ReviewerPolicy = "COUNCIL",
                        ReviewScope = "COMMON",
                        Requirements =
                        [
                            new ReviewGateRequirement
                            {
                                Id = 1001,
                                Code = "G1_DOC",
                                Title = "SRS",
                                Type = "FILE",
                                Scope = "COMMON",
                                Verification = "HUMAN",
                                Required = true
                            }
                        ]
                    }
                ]
            },
            new ReviewGateTemplate
            {
                Id = 102,
                ProjectPeriodId = 50,
                Code = "PUB-TPL",
                Name = "Mẫu đã công bố",
                Mode = "INTERDISCIPLINARY",
                Major = "",
                Status = "PUBLISHED",
                Version = 1,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedBy = 1,
                CreatedAt = now,
                UpdatedAt = now
            },
            new ReviewGateTemplate
            {
                Id = 103,
                ProjectPeriodId = 50,
                Code = "INUSE-TPL",
                Name = "Bản nháp đang tham chiếu",
                Mode = "INTERDISCIPLINARY",
                Major = "",
                Status = "DRAFT",
                Version = 1,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedBy = 1,
                CreatedAt = now,
                UpdatedAt = now
            });

        db.ProjectGateInstances.Add(new ProjectGateInstance
        {
            Id = 9001,
            ProjectId = 700,
            TemplateId = 103,
            TemplateVersion = 1,
            Code = "G1",
            Sequence = 1,
            Title = "Gate 1",
            Objective = "Obj",
            Required = true,
            Status = "PLANNED",
            ReviewEligibility = "READY",
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = now
        });

        db.SaveChanges();
        return db;
    }

    private sealed class StaffUser(long id) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public long? UserId => id;
        public string? Email => "staff@fpt.edu.vn";
        public string? FullName => "Staff IT";
        public IReadOnlyCollection<string> Roles => ["DEPARTMENT_STAFF"];
    }

    private sealed class RecordingAudit : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoopStorage : IFileStorage
    {
        public Task WriteAsync(string key, Stream content, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    }

    private class DirectTransactionProxy : DispatchProxy
    {
        public static IProjectRepository Create() => DispatchProxy.Create<IProjectRepository, DirectTransactionProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IProjectRepository.InTransactionAsync) && args is [Delegate action, CancellationToken ct])
            {
                return action.DynamicInvoke(ct);
            }
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
