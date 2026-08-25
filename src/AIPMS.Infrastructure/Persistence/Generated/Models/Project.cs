using System;
using System.Collections.Generic;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Project
{
    public long Id { get; set; }

    public long TeamId { get; set; }

    public string Code { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string? Objectives { get; set; }

    public string Status { get; set; } = null!;

    public DateTime RegisteredAt { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public long CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? ProblemStatement { get; set; }

    public string? ExpectedOutput { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual User CreatedByNavigation { get; set; } = null!;

    public virtual ICollection<Deliverable> Deliverables { get; set; } = new List<Deliverable>();

    public virtual ICollection<Evaluation> Evaluations { get; set; } = new List<Evaluation>();

    public virtual ICollection<Meeting> Meetings { get; set; } = new List<Meeting>();

    public virtual ICollection<Milestone> Milestones { get; set; } = new List<Milestone>();

    public virtual ICollection<ProgressReport> ProgressReports { get; set; } = new List<ProgressReport>();

    public virtual ICollection<ProjectMajor> ProjectMajors { get; set; } = new List<ProjectMajor>();

    public virtual ICollection<ProjectStatusHistory> ProjectStatusHistories { get; set; } = new List<ProjectStatusHistory>();

    public virtual ICollection<ProjectTag> ProjectTags { get; set; } = new List<ProjectTag>();

    public virtual SupervisorAssignment? SupervisorAssignment { get; set; }

    public virtual ICollection<SupervisorFeedback> SupervisorFeedbacks { get; set; } = new List<SupervisorFeedback>();

    public virtual ICollection<SupervisorRequest> SupervisorRequests { get; set; } = new List<SupervisorRequest>();

    public virtual Team Team { get; set; } = null!;
}
