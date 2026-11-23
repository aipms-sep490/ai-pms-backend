using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Infrastructure.Email;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class MeetingEmailNotificationTests
{
    private static AipmsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task BuildMeetingLinksAsync_ResolvesRoleSpecificClickableUrls()
    {
        using var db = CreateDb();
        db.Roles.AddRange(
            new M.Role { Id = 1, Code = AppRoles.Student, Name = "Student" },
            new M.Role { Id = 2, Code = AppRoles.Lecturer, Name = "Lecturer" });
        db.Users.AddRange(
            new M.User { Id = 10, Email = "sv@test.vn", FullName = "Sinh viên A", PasswordHash = "h", Status = "ACTIVE" },
            new M.User { Id = 11, Email = "gvhd@test.vn", FullName = "GVHD B", PasswordHash = "h", Status = "ACTIVE" },
            new M.User { Id = 12, Email = "mentor@test.vn", FullName = "Mentor C", PasswordHash = "h", Status = "ACTIVE" });
        db.UserRoles.AddRange(
            new M.UserRole { UserId = 10, RoleId = 1 },
            new M.UserRole { UserId = 11, RoleId = 2 },
            new M.UserRole { UserId = 12, RoleId = 2 });

        db.Teams.Add(new M.Team { Id = 20, AcademicSemesterId = 1, Code = "T20", Name = "Team 20", Status = "ACTIVE", CreatedBy = 10 });
        db.TeamMembers.Add(new M.TeamMember { Id = 1, TeamId = 20, UserId = 10, IsLeader = true, AcademicSemesterId = 1 });
        db.Projects.Add(new M.Project { Id = 30, TeamId = 20, Code = "PRJ-30", Title = "Đồ án StudySpace", Status = "ACTIVE", RowVersion = [1] });
        db.SupervisorProfiles.AddRange(
            new M.SupervisorProfile { Id = 40, UserId = 11 },
            new M.SupervisorProfile { Id = 41, UserId = 12 });
        db.SupervisorAssignments.AddRange(
            new M.SupervisorAssignment { Id = 50, ProjectId = 30, SupervisorProfileId = 40, IsPrimary = true, AssignmentType = "PRIMARY" },
            new M.SupervisorAssignment { Id = 51, ProjectId = 30, SupervisorProfileId = 41, IsPrimary = false, AssignmentType = "DISCIPLINE_MENTOR", MajorId = 7 });
        await db.SaveChangesAsync();

        var project = await db.Projects.SingleAsync(p => p.Id == 30);
        var videoMeeting = new M.Meeting
        {
            Id = 77, ProjectId = 30, Project = project, Title = "Họp nhóm tuần 5",
            VideoChannel = "IN_APP_VIDEO", MeetingDeliveryMode = "REMOTE", StartAt = new DateTime(2026, 11, 22, 8, 0, 0, DateTimeKind.Utc)
        };

        var svLinks = await MeetingReminderService.BuildMeetingLinksAsync(db, videoMeeting, 10, "https://aipims.khaidz.com", default);
        Assert.Equal("https://aipims.khaidz.com/project/meetings/77/video", svLinks.ActionUrl);
        Assert.Equal("https://aipims.khaidz.com/project/meetings/77", svLinks.DetailUrl);
        Assert.Contains("phòng họp video", svLinks.ActionLabel, StringComparison.OrdinalIgnoreCase);

        var supLinks = await MeetingReminderService.BuildMeetingLinksAsync(db, videoMeeting, 11, "https://aipims.khaidz.com", default);
        Assert.Equal("https://aipims.khaidz.com/supervisor/projects/30/meetings/77/video", supLinks.ActionUrl);

        var mentorLinks = await MeetingReminderService.BuildMeetingLinksAsync(db, videoMeeting, 12, "https://aipims.khaidz.com", default);
        Assert.Equal("https://aipims.khaidz.com/mentor/projects/30/majors/7/meetings/77/video", mentorLinks.ActionUrl);

        var externalMeeting = new M.Meeting
        {
            Id = 78, ProjectId = 30, Project = project, Title = "Họp Google Meet",
            VideoChannel = "EXTERNAL_LINK", OnlineUrl = "https://meet.google.com/abc-defg-hij", StartAt = new DateTime(2026, 11, 22, 8, 0, 0, DateTimeKind.Utc)
        };
        var extLinks = await MeetingReminderService.BuildMeetingLinksAsync(db, externalMeeting, 10, "https://aipims.khaidz.com", default);
        Assert.Equal("https://meet.google.com/abc-defg-hij", extLinks.ActionUrl);
        Assert.Equal("https://aipims.khaidz.com/project/meetings/78", extLinks.DetailUrl);
    }

    [Fact]
    public async Task SmtpSender_EmitsVietnameseHtmlAndPlainEmailWithClickableButtonAndSafeEncoding()
    {
        var transport = new CaptureTransport();
        var settings = new EmailSettings
        {
            Host = "smtp.gmail.com",
            Port = 587,
            SenderAddress = "noreply@aipms.vn",
            SenderName = "AI-PMS",
            Username = "u",
            Password = "p",
            FrontendBaseUrl = "https://aipims.khaidz.com"
        };
        var sender = new SmtpNotificationEmailSender(Options.Create(settings), NullLogger<SmtpNotificationEmailSender>.Instance, transport);

        var delivery = new NotificationEmailDelivery(
            RecipientId: 1,
            AttemptCount: 1,
            Email: "sv@fpt.edu.vn",
            RecipientName: "Nguyễn <Khai>",
            Subject: "Sắp đến giờ họp: Họp nhóm",
            Body: "Cuộc họp sắp bắt đầu lúc 14:48 (Việt Nam, UTC+7).\n• Chi tiết: https://aipims.khaidz.com/project/meetings/77",
            ShouldSend: true,
            ActionUrl: "https://aipims.khaidz.com/project/meetings/77/video",
            ActionLabel: "Tham gia phòng họp video ngay");

        Assert.True(await sender.TrySendAsync(delivery, default));
        Assert.Contains("Xin chào Nguyễn <Khai>,", transport.PlainBody);
        Assert.Contains("https://aipims.khaidz.com/project/meetings/77/video", transport.PlainBody);
        Assert.NotNull(transport.HtmlBody);
        Assert.Contains("href=\"https://aipims.khaidz.com/project/meetings/77/video\"", transport.HtmlBody);
        Assert.Contains("Tham gia phòng họp video ngay", transport.HtmlBody);
        Assert.Contains("href=\"https://aipims.khaidz.com/project/meetings/77\"", transport.HtmlBody);
        Assert.Contains("Nguyễn &lt;Khai&gt;", transport.HtmlBody);
        Assert.DoesNotContain("<Khai>", transport.HtmlBody);
    }

    private sealed class CaptureTransport : ISmtpTransport
    {
        public string PlainBody { get; private set; } = string.Empty;
        public string? HtmlBody { get; private set; }

        public Task SendAsync(EmailSettings settings, MailMessage message, CancellationToken ct)
        {
            PlainBody = message.Body;
            foreach (var view in message.AlternateViews)
            {
                if (view.ContentType.MediaType == "text/html")
                {
                    using var reader = new System.IO.StreamReader(view.ContentStream);
                    HtmlBody = reader.ReadToEnd();
                }
            }
            return Task.CompletedTask;
        }
    }
}
