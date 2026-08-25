using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class User
{
    public long Id { get; set; }

    public long? DepartmentId { get; set; }

    public long? MajorId { get; set; }

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string? Phone { get; set; }

    public string? StudentCode { get; set; }

    public string? EmployeeCode { get; set; }

    public string? Title { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int AccessFailedCount { get; set; }

    public DateTime? LockoutEndAt { get; set; }

    public DateTime? PasswordChangedAt { get; set; }

    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public virtual ICollection<DeliverableVersion> DeliverableVersions { get; set; } = new List<DeliverableVersion>();

    public virtual ICollection<Deliverable> Deliverables { get; set; } = new List<Deliverable>();

    public virtual Department? Department { get; set; }

    public virtual ICollection<Evaluation> Evaluations { get; set; } = new List<Evaluation>();

    public virtual ICollection<File> Files { get; set; } = new List<File>();

    public virtual Major? Major { get; set; }

    public virtual ICollection<MeetingParticipant> MeetingParticipants { get; set; } = new List<MeetingParticipant>();

    public virtual ICollection<Meeting> Meetings { get; set; } = new List<Meeting>();

    public virtual ICollection<Milestone> Milestones { get; set; } = new List<Milestone>();

    public virtual ICollection<NotificationRecipient> NotificationRecipients { get; set; } = new List<NotificationRecipient>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();

    public virtual ICollection<ProgressReport> ProgressReports { get; set; } = new List<ProgressReport>();

    public virtual ICollection<ProjectStatusHistory> ProjectStatusHistories { get; set; } = new List<ProjectStatusHistory>();

    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();

    public virtual ICollection<Rubric> Rubrics { get; set; } = new List<Rubric>();

    public virtual SupervisorProfile? SupervisorProfile { get; set; }

    public virtual ICollection<SupervisorRequest> SupervisorRequests { get; set; } = new List<SupervisorRequest>();

    public virtual ICollection<TaskAssignee> TaskAssigneeAssignedByNavigations { get; set; } = new List<TaskAssignee>();

    public virtual ICollection<TaskAssignee> TaskAssigneeUsers { get; set; } = new List<TaskAssignee>();

    public virtual ICollection<TaskStatusHistory> TaskStatusHistories { get; set; } = new List<TaskStatusHistory>();

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();

    public virtual ICollection<TeamInvitation> TeamInvitationInvitedByNavigations { get; set; } = new List<TeamInvitation>();

    public virtual ICollection<TeamInvitation> TeamInvitationInvitedUsers { get; set; } = new List<TeamInvitation>();

    public virtual ICollection<TeamMember> TeamMembers { get; set; } = new List<TeamMember>();

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();

    public virtual ICollection<UserRole> UserRoleAssignedByNavigations { get; set; } = new List<UserRole>();

    public virtual ICollection<UserRole> UserRoleUsers { get; set; } = new List<UserRole>();
}
